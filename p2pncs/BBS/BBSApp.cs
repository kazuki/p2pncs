/*
 * Copyright (C) 2009 Kazuki Oikawa
 * 
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;
using Kazuki.Net.HttpServer;
using openCrypto.EllipticCurve;
using p2pncs.BBS;
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.DFS.MMLC;
using p2pncs.Security.Captcha;
using p2pncs.Security.Cryptography;

namespace p2pncs
{
	partial class WebApp
	{
		object ProcessBbsOpenPage (IHttpRequest req, HttpResponseHeader res)
		{
			if (req.HttpMethod == HttpMethod.POST && req.HasContentBody ()) {
				Dictionary<string, string> dic = HttpUtility.ParseUrlEncodedStringToDictionary (Encoding.ASCII.GetString (req.GetContentBody (MaxRequestBodySize)), Encoding.UTF8);
				string key;
				if (dic.TryGetValue ("bbsid", out key)) {
					res[HttpHeaderNames.Location] = "/bbs/" + key;
					throw new HttpException (req.HttpVersion == HttpVersion.Http10 ? HttpStatusCode.Found : HttpStatusCode.SeeOther);
				}
			}
			XmlDocument doc = XmlHelper.CreateEmptyDocument ();
			return _xslTemplate.Render (req, res, doc, Path.Combine (DefaultTemplatePath, "bbs_open.xsl"));
		}

		object ProcessBbsListPage (IHttpRequest req, HttpResponseHeader res)
		{
			XmlDocument doc = XmlHelper.CreateEmptyDocument ();
			XmlElement rootNode = doc.DocumentElement;
			MergeableFileHeader[] headers = _node.MMLC.GetHeaderList ();
			foreach (MergeableFileHeader header in headers) {
				rootNode.AppendChild (XmlHelper.CreateMergeableFileElement (doc, header));
			}
			return _xslTemplate.Render (req, res, doc, Path.Combine (DefaultTemplatePath, "bbs.xsl"));
		}

		object ProcessBBS (IHttpRequest req, HttpResponseHeader res, MergeableFileHeader header, string tail_url)
		{
			if (req.HttpMethod == HttpMethod.POST) {
				// posting...
				Key key = header.Key;
				Dictionary<string, string> dic = HttpUtility.ParseUrlEncodedStringToDictionary (req.GetContentBody (MaxRequestBodySize));
				res[HttpHeaderNames.ContentType] = "text/xml; charset=UTF-8";
				string name = Helpers.GetValueSafe (dic, "name").Trim ();
				string body = Helpers.GetValueSafe (dic, "body").Trim ();
				string auth = Helpers.GetValueSafe (dic, "auth").Trim ();
				string token = Helpers.GetValueSafe (dic, "token").Trim ();
				string answer = Helpers.GetValueSafe (dic, "answer").Trim ();
				string prev = Helpers.GetValueSafe (dic, "prev").Trim ();
				if (body.Length > 0) {
					header = _node.MMLC.GetMergeableFileHeader (key);
					if (header == null)
						return "<result status=\"ERROR\" />";
					MergeableFileRecord record;
					try {
						if (header.AuthServers == null || header.AuthServers.Length == 0) {
							_node.MMLC.AppendRecord (key, new MergeableFileRecord (new SimpleBBSRecord (name, body), DateTime.UtcNow, header.LastManagedTime, null, null, null, 0, null));
							return "<result status=\"OK\" />";
						} else {
							byte auth_idx = byte.Parse (auth);
							if (token.Length > 0 && answer.Length > 0 && prev.Length > 0) {
								record = (MergeableFileRecord)Serializer.Instance.Deserialize (Convert.FromBase64String (prev));
								byte[] sign = _node.MMLC.VerifyCaptchaChallenge (header.AuthServers[auth_idx], record.Hash.GetByteArray (),
									Convert.FromBase64String (token), Encoding.ASCII.GetBytes (answer));
								if (sign != null) {
									record.AuthorityIndex = auth_idx;
									record.Authentication = sign;
									if (record.Verify (header)) {
										_node.MMLC.AppendRecord (key, record);
										return "<result status=\"OK\" />";
									} else {
										return "<result status=\"ERROR\" code=\"1\" />";
									}
								}
							}

							record = new MergeableFileRecord (new SimpleBBSRecord (name, body), DateTime.UtcNow, header.LastManagedTime, null, null, null, 0, null);
							CaptchaChallengeData captchaData = _node.MMLC.GetCaptchaChallengeData (header.AuthServers[auth_idx], record.Hash.GetByteArray ());
							return string.Format ("<result status=\"CAPTCHA\"><img>{0}</img><token>{1}</token><prev>{2}</prev></result>",
								Convert.ToBase64String (captchaData.Data), Convert.ToBase64String (captchaData.Token),
								Convert.ToBase64String (Serializer.Instance.Serialize (record)));
						}
					} catch {
						return "<result status=\"ERROR\" code=\"0\" />";
					}
				}
				return "<result status=\"EMPTY\" />";
			}

			List<MergeableFileRecord> records = _node.MMLC.GetRecords (header.Key, out header);
			if (records == null)
				throw new HttpException (HttpStatusCode.NotFound);

			XmlDocument doc = XmlHelper.CreateEmptyDocument ();
			doc.DocumentElement.AppendChild (XmlHelper.CreateMergeableFileElement (doc, header, records.ToArray ()));
			return _xslTemplate.Render (req, res, doc, Path.Combine (DefaultTemplatePath, "bbs_view.xsl"));
		}
	}
}

namespace p2pncs.BBS
{
	class BBSWebApp : WebApp.IMergeableFileCommonProcess
	{
		static BBSWebApp _instance = new BBSWebApp ();
		public static BBSWebApp Instance {
			get { return _instance; }
		}
		BBSWebApp () {}

		#region IMergeableFileCommonProcess Members

		void ParseNewPagePostData (Dictionary<string, string> dic, out string title, out string fpname, out string fpbody, out bool title_check)
		{
			title = Helpers.GetValueSafe (dic, "title").Trim ();
			fpname = Helpers.GetValueSafe (dic, "fpname").Trim ();
			fpbody = Helpers.GetValueSafe (dic, "fpbody").Trim ();
			title_check = (title.Length > 0 && title.Length <= 64);
		}

		public bool ParseNewPagePostData (Dictionary<string, string> dic, out IHashComputable header, out IHashComputable[] records)
		{
			string title, fpname, fpbody;
			bool title_check;
			ParseNewPagePostData (dic, out title, out fpname, out fpbody, out title_check);

			if (!title_check) {
				header = null;
				records = null;
				return false;
			}

			header = new SimpleBBSHeader (title);
			if (fpbody.Length == 0) {
				records = null;
			} else {
				records = new IHashComputable[] {
					new SimpleBBSRecord (fpname, fpbody)
				};
			}
			return true;
		}

		public void OutputNewPageData (Dictionary<string, string> dic, XmlElement validationRoot)
		{
			XmlDocument doc = validationRoot.OwnerDocument;
			string title, fpname, fpbody;
			bool title_check;
			ParseNewPagePostData (dic, out title, out fpname, out fpbody, out title_check);

			validationRoot.AppendChild (doc.CreateElement ("data", new string[][] { new[] { "name", "title" }, new[] { "status", title_check ? "ok" : "error" } }, new[] {
				doc.CreateElement ("value", null, new[]{doc.CreateTextNode (title)}),
				title_check ? null : doc.CreateElement ("msg", null, new[]{doc.CreateTextNode ("タイトルは1文字～64文字に収まらなければいけません")})
			}));
			validationRoot.AppendChild (doc.CreateElement ("data", new string[][] { new[] { "name", "fpname" } }, new[] {
				doc.CreateElement ("value", null, new[]{doc.CreateTextNode (fpname)})
			}));
			validationRoot.AppendChild (doc.CreateElement ("data", new string[][] { new[] { "name", "fpbody" } }, new[] {
				doc.CreateElement ("value", null, new[]{doc.CreateTextNode (fpbody)})
			}));
		}

		public string NewPageXSL {
			get { return "bbs_new.xsl"; }
		}

		#endregion
	}
}