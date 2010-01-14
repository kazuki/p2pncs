/*
 * Copyright (C) 2009-2010 Kazuki Oikawa
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
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;
using Kazuki.Net.HttpServer;
using openCrypto.EllipticCurve;
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.DFS.MMLC;
using p2pncs.Security.Captcha;
using p2pncs.Security.Cryptography;

namespace p2pncs
{
	partial class WebApp
	{
		object Process_NewMergeableFilePage (IHttpRequest req, HttpResponseHeader res, IMergeableFileCommonProcess comm)
		{
			XmlDocument doc = XmlHelper.CreateEmptyDocument ();
			if (req.HttpMethod == HttpMethod.POST) {
				Dictionary<string, string> dic = HttpUtility.ParseUrlEncodedStringToDictionary (Encoding.ASCII.GetString (req.GetContentBody (MaxRequestBodySize)), Encoding.UTF8);
				XmlElement validationRoot = (XmlElement)doc.DocumentElement.AppendChild (doc.CreateElement ("validation"));
				string title = Helpers.GetValueSafe (dic, "title").Trim ();
				string auth = Helpers.GetValueSafe (dic, "auth").Trim ();
				string state = Helpers.GetValueSafe (dic, "state").Trim ();
				MergeableFileHeaderFlags flags = MergeableFileHeaderFlags.None;
				bool reedit = Helpers.GetValueSafe (dic, "re-edit").Length > 0;
				bool auth_status = true, header_size_over = false, record_size_over = false, title_check = true;
				try {
					AuthServerInfo.ParseArray (auth);
				} catch {
					auth_status = false;
				}
				if (title.Length == 0 || title.Length > 64)
					title_check = false;

				IHashComputable header_content;
				IHashComputable[] record_contents;
				bool comm_check = comm.ParseNewPagePostData (dic, out header_content, out record_contents);

				if (comm_check && auth_status && title_check && !reedit) {
					MergeableFileRecord[] records = null;
					if (record_contents != null && record_contents.Length > 0) {
						records = new MergeableFileRecord[record_contents.Length];
						for (int i = 0; i < records.Length; i++)
							records[i] = new MergeableFileRecord (record_contents[i], DateTime.UtcNow, DateTime.UtcNow, null, null, null, byte.MaxValue, new byte[DefaultAlgorithm.ECDomainBytes + 1]);
					}
					AuthServerInfo[] auth_servers = AuthServerInfo.ParseArray (auth);
					if (state == "confirm") {
						try {
							MergeableFileHeader header = new MergeableFileHeader (title, flags, header_content, auth_servers);
							header = _node.MMLC.CreateNew (header, records);
							doc.DocumentElement.AppendChild (doc.CreateElement ("created", new string[][] {
								new[] {"key", header.Key.ToUriSafeBase64String ()}
							}, null));
							state = "success";
						} catch (OutOfMemoryException) {
							header_size_over = true;
							record_size_over = true;
							state = "";
						}
					} else {
						state = "confirm";
						Key tmp_key = new Key (new byte[DefaultAlgorithm.ECDomainBytes + 1]);
						MergeableFileHeader header = new MergeableFileHeader (tmp_key, title, flags, DateTime.UtcNow, DateTime.UtcNow, header_content, auth_servers, null, null);
						if (Serializer.Instance.Serialize (header).Length > MMLC.MergeableFileHeaderMaxSize) {
							header_size_over = true;
							state = "";
						}
						if (records != null) {
							for (int i = 0; i < records.Length; i++) {
								if (Serializer.Instance.Serialize (records[i]).Length > MMLC.MergeableFileRecordMaxSize) {
									record_size_over = true;
									state = "";
									break;
								}
							}
						}
					}
				} else {
					state = string.Empty;
				}

				if (header_size_over) {
					validationRoot.AppendChild (doc.CreateElement ("error", new string[][] {
						new string[]{"type", "header-size-over"},
						new string[]{"limit", MMLC.MergeableFileHeaderMaxSize.ToString ()},
					}, null));
				}
				if (record_size_over) {
					validationRoot.AppendChild (doc.CreateElement ("error", new string[][] {
						new string[]{"type", "record-size-over"},
						new string[]{"limit", MMLC.MergeableFileRecordMaxSize.ToString ()},
					}, null));
				}

				validationRoot.AppendChild (doc.CreateElement ("data", new string[][] { new[] { "name", "title" }, new[] { "status", title_check ? "ok" : "error" } }, new[] {
					doc.CreateElement ("value", null, new[]{doc.CreateTextNode (title)}),
					title_check ? null : doc.CreateElement ("msg", null, new[]{doc.CreateTextNode ("タイトルは1文字～64文字に収まらなければいけません")})
				}));
				validationRoot.AppendChild (doc.CreateElement ("data", new string[][] { new[] { "name", "auth" }, new[] { "status", auth_status ? "ok" : "error" } }, new[] {
					doc.CreateElement ("value", null, new[]{doc.CreateTextNode (auth)}),
					auth_status ? null : doc.CreateElement ("msg", null, new[]{doc.CreateTextNode ("認識できません。入力ミスがないか確認してください")})
				}));
				validationRoot.AppendChild (doc.CreateElement ("data", new string[][] { new[] { "name", "state" } }, new[] {
					doc.CreateElement ("value", null, new[]{doc.CreateTextNode (state)})
				}));
				comm.OutputNewPageData (dic, validationRoot);
			}
			return _xslTemplate.Render (req, res, doc, Path.Combine (DefaultTemplatePath, comm.NewPageXSL));
		}

		object Process_ViewMergeableFilePage (IHttpRequest req, HttpResponseHeader res, Key key, MergeableFileHeader header, string url_tail)
		{
			if (req.HttpMethod == HttpMethod.POST) {
				if (req.QueryData.Count > 0) {
					return (header.Content as IMergeableFile).WebUIHelper.ProcessPutRequest (_node, req, res, header, url_tail);
				}
				return Process_Post (req, res, header, url_tail);
			}

			if (!_fastView || header == null || header.RecordsetHash.IsZero ()) {
				ManualResetEvent done = new ManualResetEvent (false);
				MergeCometInfo mci = new MergeCometInfo (_node, key, url_tail);
				CometInfo info = new CometInfo (done, req, res, null, DateTime.Now + TimeSpan.FromSeconds (5), mci.CometHandler);
				_node.MMLC.StartMerge (key, delegate (object sender, MergeDoneCallbackArgs args) {
					if (done.SafeWaitHandle.IsClosed)
						return;
					done.Set ();
				}, null);
				return info;
			} else {
				_node.MMLC.StartMerge (header.Key, null, null);
			}

			return (header.Content as IMergeableFile).WebUIHelper.ProcessGetRequest (_node, req, res, header, url_tail);
		}

		object Process_Post (IHttpRequest req, HttpResponseHeader res, MergeableFileHeader header, string url_tail)
		{
			WebApp.IMergeableFileCommonProcess comm = (header.Content as IMergeableFile).WebUIMergeableFileCommon;
			Dictionary<string, string> dic = HttpUtility.ParseUrlEncodedStringToDictionary (req.GetContentBody (WebApp.MaxRequestBodySize));
			res[HttpHeaderNames.ContentType] = "text/xml; charset=UTF-8";
			try {
				string auth = Helpers.GetValueSafe (dic, "auth").Trim ();
				string token = Helpers.GetValueSafe (dic, "token").Trim ();
				string answer = Helpers.GetValueSafe (dic, "answer").Trim ();
				string prev = Helpers.GetValueSafe (dic, "prev").Trim ();
				ECKeyPair privateKey = _node.MMLC.SelectPrivateKey (header.Key);
				if (header.AuthServers == null || header.AuthServers.Length == 0 || privateKey != null) {
					_node.MMLC.AppendRecord (header.Key, new MergeableFileRecord (comm.ParseNewPostData (dic), DateTime.UtcNow, header.LastManagedTime, null, null, null, 0, null), privateKey);
					return "<result status=\"OK\" />";
				} else {
					byte auth_idx = byte.Parse (auth);
					MergeableFileRecord record;
					if (token.Length > 0 && answer.Length > 0 && prev.Length > 0) {
						record = (MergeableFileRecord)Serializer.Instance.Deserialize (Convert.FromBase64String (prev));
						byte[] sign = _node.MMLC.VerifyCaptchaChallenge (header.AuthServers[auth_idx], record.Hash.GetByteArray (),
							Convert.FromBase64String (token), Encoding.ASCII.GetBytes (answer));
						if (sign != null) {
							record.AuthorityIndex = auth_idx;
							record.Authentication = sign;
							if (record.Verify (header)) {
								_node.MMLC.AppendRecord (header.Key , record, null);
								return "<result status=\"OK\" />";
							} else {
								return "<result status=\"ERROR\" code=\"1\" />";
							}
						}
					}

					record = new MergeableFileRecord (comm.ParseNewPostData (dic), DateTime.UtcNow, header.LastManagedTime, null, null, null, 0, null);
					byte[] raw = Serializer.Instance.Serialize (record);
					if (raw.Length > MMLC.MergeableFileRecordMaxSize)
						throw new OutOfMemoryException ("データが多すぎます");
					CaptchaChallengeData captchaData = _node.MMLC.GetCaptchaChallengeData (header.AuthServers[auth_idx], record.Hash.GetByteArray ());
					return string.Format ("<result status=\"CAPTCHA\"><img>{0}</img><token>{1}</token><prev>{2}</prev></result>",
						Convert.ToBase64String (captchaData.Data), Convert.ToBase64String (captchaData.Token), Convert.ToBase64String (raw));
				}
			} catch (Exception exception) {
				return "<result status=\"ERROR\" code=\"0\">" +
					exception.Message.Replace ("&", "&amp;").Replace ("<", "&lt;").Replace (">", "&gt;").Replace ("\"", "&quot;") + "</result>";
			}
		}

		class MergeCometInfo
		{
			Key _key;
			string _tail;
			Node _node;

			public MergeCometInfo (Node node, Key key, string tail_url)
			{
				_node = node;
				_key = key;
				_tail = tail_url;
			}

			public object CometHandler (CometInfo info)
			{
				info.WaitHandle.Close ();
				MergeableFileHeader header = _node.MMLC.GetMergeableFileHeader (_key);
				if (header == null)
					throw new HttpException (HttpStatusCode.NotFound);
				return (header.Content as IMergeableFile).WebUIHelper.ProcessGetRequest
					(_node, info.Request, info.Response, header, _tail);
			}
		}

		public interface IMergeableFileCommonProcess
		{
			bool ParseNewPagePostData (Dictionary<string, string> dic, out IHashComputable header, out IHashComputable[] records);
			void OutputNewPageData (Dictionary<string, string> dic, XmlElement validationRoot);
			string NewPageXSL { get; }

			IHashComputable ParseNewPostData (Dictionary<string, string> dic);
		}
	}
}
