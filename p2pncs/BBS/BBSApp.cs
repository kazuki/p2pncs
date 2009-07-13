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
		object ProcessBbsNewPage (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			XmlDocument doc = XmlHelper.CreateEmptyDocument ();
			if (req.HttpMethod == HttpMethod.POST) {
				Dictionary<string, string> dic = HttpUtility.ParseUrlEncodedStringToDictionary (Encoding.ASCII.GetString (req.GetContentBody (MaxRequestBodySize)), Encoding.UTF8);
				XmlNode validationRoot = doc.DocumentElement.AppendChild (doc.CreateElement ("validation"));
				string title = Helpers.GetValueSafe (dic, "title").Trim ();
				string auth = Helpers.GetValueSafe (dic, "auth").Trim ();
				string fpname = Helpers.GetValueSafe (dic, "fpname").Trim ();
				string fpbody = Helpers.GetValueSafe (dic, "fpbody").Trim ();
				string state = Helpers.GetValueSafe (dic, "state").Trim ();
				bool reedit = Helpers.GetValueSafe (dic, "re-edit").Length > 0;
				bool title_status = title.Length != 0 && title.Length <= 64;
				bool auth_status = true;
				bool all_status = true;
				try {
					AuthServerInfo.ParseArray (auth);
				} catch {
					auth_status = false;
				}
				if (title_status && auth_status && !reedit) {
					SimpleBBSHeader header2 = new SimpleBBSHeader (title);
					MergeableFileRecord record = null;
					if (fpbody.Length > 0)
						record = new MergeableFileRecord (new SimpleBBSRecord (fpname, fpbody), DateTime.UtcNow, DateTime.UtcNow, null, null, null, byte.MaxValue, new byte[DefaultAlgorithm.ECDomainBytes + 1]);
					AuthServerInfo[] auth_servers = AuthServerInfo.ParseArray (auth);
					if (state == "confirm") {
						try {
							MergeableFileHeader header = _node.MMLC.CreateNew (header2, auth_servers, record == null ? null : new MergeableFileRecord[] { record });
							doc.DocumentElement.AppendChild (doc.CreateElement ("created", new string[][] { new[] { "key", header.Key.ToUriSafeBase64String () } }, null));
							state = "success";
						} catch (OutOfMemoryException) {
							all_status = false;
							state = "";
						}
					} else {
						state = "confirm";
						MergeableFileHeader header = new MergeableFileHeader (ECKeyPair.Create (DefaultAlgorithm.ECDomainName), DateTime.UtcNow, DateTime.UtcNow, header2, auth_servers);
						if (Serializer.Instance.Serialize (header).Length > MMLC.MergeableFileHeaderMaxSize || (record != null && Serializer.Instance.Serialize (record).Length > MMLC.MergeableFileRecordMaxSize)) {
							all_status = false;
							state = "";
						}
					}
				} else {
					state = string.Empty;
				}

				if (!all_status) {
					validationRoot.AppendChild (doc.CreateElement ("all", null, new[]{
						doc.CreateTextNode (string.Format ("ヘッダサイズが{0}バイトを超えたか、最初の投稿が{1}バイトを超えました. タイトルや認証サーバまたは投稿文の情報量を減らしてください", MMLC.MergeableFileHeaderMaxSize, MMLC.MergeableFileRecordMaxSize))
					}));
				}
				validationRoot.AppendChild (doc.CreateElement ("data", new string[][] { new[] { "name", "title" }, new[] { "status", title_status ? "ok" : "error" } }, new[] {
					doc.CreateElement ("value", null, new[]{doc.CreateTextNode (title)}),
					title_status ? null : doc.CreateElement ("msg", null, new[]{doc.CreateTextNode ("タイトルは1文字～64文字に収まらなければいけません")})
				}));
				validationRoot.AppendChild (doc.CreateElement ("data", new string[][] { new[] { "name", "auth" }, new[] { "status", auth_status ? "ok" : "error" } }, new[] {
					doc.CreateElement ("value", null, new[]{doc.CreateTextNode (auth)}),
					auth_status ? null : doc.CreateElement ("msg", null, new[]{doc.CreateTextNode ("認識できません。入力ミスがないか確認してください")})
				}));
				validationRoot.AppendChild (doc.CreateElement ("data", new string[][] { new[] { "name", "fpname" } }, new[] {
					doc.CreateElement ("value", null, new[]{doc.CreateTextNode (fpname)})
				}));
				validationRoot.AppendChild (doc.CreateElement ("data", new string[][] { new[] { "name", "fpbody" } }, new[] {
					doc.CreateElement ("value", null, new[]{doc.CreateTextNode (fpbody)})
				}));
				validationRoot.AppendChild (doc.CreateElement ("data", new string[][] { new[] { "name", "state" } }, new[] {
					doc.CreateElement ("value", null, new[]{doc.CreateTextNode (state)})
				}));
			}
			return _xslTemplate.Render (server, req, res, doc, Path.Combine (DefaultTemplatePath, "bbs_new.xsl"));
		}

		object ProcessBbsOpenPage (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
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
			return _xslTemplate.Render (server, req, res, doc, Path.Combine (DefaultTemplatePath, "bbs_open.xsl"));
		}

		object ProcessBbsListPage (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			XmlDocument doc = XmlHelper.CreateEmptyDocument ();
			XmlElement rootNode = doc.DocumentElement;
			MergeableFileHeader[] headers = _node.MMLC.GetHeaderList ();
			foreach (MergeableFileHeader header in headers) {
				rootNode.AppendChild (XmlHelper.CreateMergeableFileElement (doc, header));
			}
			return _xslTemplate.Render (server, req, res, doc, Path.Combine (DefaultTemplatePath, "bbs.xsl"));
		}

		object ProcessBBS (IHttpServer server, IHttpRequest req, HttpResponseHeader res, bool callByCallback)
		{
			string str_key = req.Url.AbsolutePath.Substring (5);
			Key key;
			try {
				if (str_key.Length == (DefaultAlgorithm.ECDomainBytes + 1) * 2)
					key = Key.Parse (str_key);
				else if (str_key.Length == (DefaultAlgorithm.ECDomainBytes + 1) * 4 / 3)
					key = Key.FromUriSafeBase64String (str_key);
				else
					throw new HttpException (HttpStatusCode.NotFound);
			} catch {
				throw new HttpException (HttpStatusCode.NotFound);
			}

			MergeableFileHeader header;
			if (req.HttpMethod == HttpMethod.POST) {
				// posting...
				res[HttpHeaderNames.ContentType] = "text/xml; charset=UTF-8";
				string name = Helpers.GetQueryValue (req, "name").Trim ();
				string body = Helpers.GetQueryValue (req, "body").Trim ();
				string auth = Helpers.GetQueryValue (req, "auth").Trim ();
				string token = Helpers.GetQueryValue (req, "token").Trim ();
				string answer = Helpers.GetQueryValue (req, "answer").Trim ();
				string prev = Helpers.GetQueryValue (req, "prev").Trim ();
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

			List<MergeableFileRecord> records = _node.MMLC.GetRecords (key, out header);
			if (!callByCallback) {
				if (!_fastView || header == null) {
					ManualResetEvent done = new ManualResetEvent (false);
					CometInfo info = new CometInfo (done, req, res, null, DateTime.Now + TimeSpan.FromSeconds (5), ProcessBBS_CometHandler);
					_node.MMLC.StartMerge (key, ProcessBBS_Callback, done);
					return info;
				} else {
					_node.MMLC.StartMerge (key, null, null);
				}
			}
			if (records == null)
				throw new HttpException (HttpStatusCode.NotFound);

			XmlDocument doc = XmlHelper.CreateEmptyDocument ();
			doc.DocumentElement.AppendChild (XmlHelper.CreateMergeableFileElement (doc, header, records.ToArray ()));
			return _xslTemplate.Render (server, req, res, doc, Path.Combine (DefaultTemplatePath, "bbs_view.xsl"));
		}

		void ProcessBBS_Callback (object sender, MergeDoneCallbackArgs args)
		{
			ManualResetEvent done = args.State as ManualResetEvent;
			if (done.SafeWaitHandle.IsClosed)
				return;
			done.Set ();
		}

		object ProcessBBS_CometHandler (CometInfo info)
		{
			info.WaitHandle.Close ();
			return ProcessBBS (null, info.Request, info.Response, true);
		}
	}
}
