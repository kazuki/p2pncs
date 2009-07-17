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
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;
using Kazuki.Net.HttpServer;
using openCrypto.EllipticCurve;
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.DFS.MMLC;
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
				string auth = Helpers.GetValueSafe (dic, "auth").Trim ();
				string state = Helpers.GetValueSafe (dic, "state").Trim ();
				bool reedit = Helpers.GetValueSafe (dic, "re-edit").Length > 0;
				bool auth_status = true, header_size_over = false, record_size_over = false;
				try {
					AuthServerInfo.ParseArray (auth);
				} catch {
					auth_status = false;
				}

				IHashComputable header_content;
				IHashComputable[] record_contents;
				bool comm_check = comm.ParseNewPagePostData (dic, out header_content, out record_contents);

				if (comm_check && auth_status && !reedit) {
					MergeableFileRecord[] records = null;
					if (record_contents != null && record_contents.Length > 0) {
						records = new MergeableFileRecord[record_contents.Length];
						for (int i = 0; i < records.Length; i++)
							records[i] = new MergeableFileRecord (record_contents[i], DateTime.UtcNow, DateTime.UtcNow, null, null, null, byte.MaxValue, new byte[DefaultAlgorithm.ECDomainBytes + 1]);
					}
					AuthServerInfo[] auth_servers = AuthServerInfo.ParseArray (auth);
					if (state == "confirm") {
						try {
							MergeableFileHeader header = _node.MMLC.CreateNew (header_content, auth_servers, records);
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
						MergeableFileHeader header = new MergeableFileHeader (ECKeyPair.Create (DefaultAlgorithm.ECDomainName), DateTime.UtcNow, DateTime.UtcNow, header_content, auth_servers);
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

		object Process_ViewMergeableFilePage (IHttpRequest req, HttpResponseHeader res, MergeableFileHeader header, string url_tail)
		{
			if (req.HttpMethod == HttpMethod.POST) {
				throw new HttpException (HttpStatusCode.NotFound);
			}

			if (!_fastView || header == null || header.RecordsetHash.IsZero ()) {
				ManualResetEvent done = new ManualResetEvent (false);
				MergeCometInfo mci = new MergeCometInfo (this, header, url_tail);
				CometInfo info = new CometInfo (done, req, res, null, DateTime.Now + TimeSpan.FromSeconds (5), mci.CometHandler);
				_node.MMLC.StartMerge (header.Key, delegate (object sender, MergeDoneCallbackArgs args) {
					if (done.SafeWaitHandle.IsClosed)
						return;
					done.Set ();
				}, null);
				return info;
			} else {
				_node.MMLC.StartMerge (header.Key, null, null);
			}

			return Process_ViewMergeableFilePage_Switch (req, res, header, url_tail);
		}

		object Process_ViewMergeableFilePage_Switch (IHttpRequest req, HttpResponseHeader res, MergeableFileHeader header, string url_tail)
		{
			if (header.Content is BBS.SimpleBBSHeader)
				return ProcessBBS (req, res, header, url_tail);
			if (header.Content is Wiki.WikiHeader)
				return ProcessWikiPage (req, res, header, url_tail);
			throw new HttpException (HttpStatusCode.NotFound);
		}

		class MergeCometInfo
		{
			public MergeableFileHeader FileHeader;
			string TailUrl;
			WebApp App;

			public MergeCometInfo (WebApp app, MergeableFileHeader header, string tail_url)
			{
				App = app;
				FileHeader = header;
				TailUrl = tail_url;
			}

			public object CometHandler (CometInfo info)
			{
				info.WaitHandle.Close ();
				return App.Process_ViewMergeableFilePage_Switch (info.Request, info.Response, FileHeader, TailUrl);
			}
		}

		public interface IMergeableFileCommonProcess
		{
			bool ParseNewPagePostData (Dictionary<string, string> dic, out IHashComputable header, out IHashComputable[] records);
			void OutputNewPageData (Dictionary<string, string> dic, XmlElement validationRoot);
			string NewPageXSL { get; }
		}
	}
}
