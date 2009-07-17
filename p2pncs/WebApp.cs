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
using Kazuki.Net.HttpServer.TemplateEngines;
using openCrypto.EllipticCurve;
using p2pncs.Net.Overlay.DFS.MMLC;
using p2pncs.Security.Cryptography;

namespace p2pncs
{
	partial class WebApp : IHttpApplication, IDisposable
	{
		const string DefaultTemplatePath = "templates";
		const string DefaultStaticFilePath = "htdocs";
		const int MaxRequestBodySize = 33554432; // 32MB

		Node _node;
		ManualResetEvent _exitWaitHandle = new ManualResetEvent (false);
		XslTemplateEngine _xslTemplate = new XslTemplateEngine ();
		bool _fastView = true;

		public WebApp (Node node)
		{
			_node = node;
			node.MMLC.Register (BBS.SimpleBBSParser.Instance);
			node.MMLC.Register (Wiki.WikiParser.Instance);
		}

		public void Dispose ()
		{
		}

		public object Process (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			string absPath = req.Url.AbsolutePath;
			if (absPath == "/")
				return ProcessMainPage (server, req, res);
			if (absPath == "/net/init")
				return ProcessNetInitPage (server, req, res);
			if (absPath == "/net/exit")
				return ProcessNetExitPage (server, req, res);
			if (absPath == "/bbs" || absPath == "/bbs/")
				return ProcessBbsListPage (server, req, res);
			if (absPath == "/bbs/new")
				return Process_NewMergeableFilePage (server, req, res, BBS.BBSWebApp.Instance);
			if (absPath == "/bbs/open")
				return ProcessBbsOpenPage (server, req, res);
			if (absPath.StartsWith ("/bbs/"))
				return ProcessBBS (server, req, res, false);
			if (absPath == "/wiki" || absPath == "/wiki/")
				return ProcessWikiListPage (server, req, res);
			if (absPath == "/wiki/new")
				return Process_NewMergeableFilePage (server, req, res, Wiki.WikiWebApp.Instance);
			if (absPath.StartsWith ("/wiki/"))
				return ProcessWikiPage (server, req, res, false);
			if (absPath == "/manage" || absPath == "/manage/")
				return ProcessManageTop (server, req, res);
			if (absPath.StartsWith ("/manage/"))
				return ProcessManageFile (server, req, res);
			if (absPath == "/statistics")
				return ProcessStatistics (server, req, res);
			return ProcessStaticFile (server, req, res);
		}

		object ProcessMainPage (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			XmlDocument doc = XmlHelper.CreateEmptyDocument ();
			doc.DocumentElement.SetAttribute ("ver", System.Reflection.Assembly.GetEntryAssembly ().GetName ().Version.ToString ());
			return _xslTemplate.Render (server, req, res, doc, Path.Combine (DefaultTemplatePath, "main.xsl"));
		}

		object ProcessStaticFile (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			string path = req.Url.AbsolutePath;
			if (path.Contains ("/../"))
				throw new HttpException (HttpStatusCode.BadRequest);
			path = path.Replace ('/', Path.DirectorySeparatorChar).Substring (1);
			path = Path.Combine (DefaultStaticFilePath, path);
			if (!File.Exists (path))
				throw new HttpException (HttpStatusCode.NotFound);
			DateTime lastModified = File.GetLastWriteTimeUtc (path);
			string etag = lastModified.Ticks.ToString ("x");
			res[HttpHeaderNames.ETag] = etag;
			res[HttpHeaderNames.LastModified] = lastModified.ToString ("r");
			if (req.Headers.ContainsKey (HttpHeaderNames.IfNoneMatch) && req.Headers[HttpHeaderNames.IfNoneMatch] == etag)
				throw new HttpException (HttpStatusCode.NotModified);

			res[HttpHeaderNames.ContentType] = MIMEDatabase.GetMIMEType (Path.GetExtension (path));
			bool supportGzip = req.Headers.ContainsKey (HttpHeaderNames.AcceptEncoding) && req.Headers[HttpHeaderNames.AcceptEncoding].Contains("gzip");
			string gzip_path = path + ".gz";
			if (supportGzip && File.Exists (gzip_path) && File.GetLastWriteTimeUtc (gzip_path) >= lastModified) {
				path = gzip_path;
				res[HttpHeaderNames.ContentEncoding] = "gzip";
			}
			using (FileStream strm = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
				byte[] raw = new byte[strm.Length];
				strm.Read (raw, 0, raw.Length);
				return raw;
			}
		}

		public WaitHandle ExitWaitHandle {
			get { return _exitWaitHandle; }
		}

		#region Common Process for Mergeable Files
		object Process_NewMergeableFilePage (IHttpServer server, IHttpRequest req, HttpResponseHeader res, IMergeableFileCommonProcess comm)
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
						for (int i = 0; i < records.Length; i ++)
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
							for (int i = 0; i < records.Length; i ++) {
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
			return _xslTemplate.Render (server, req, res, doc, Path.Combine (DefaultTemplatePath, comm.NewPageXSL));
		}

		public interface IMergeableFileCommonProcess
		{
			bool ParseNewPagePostData (Dictionary<string, string> dic, out IHashComputable header, out IHashComputable[] records);
			void OutputNewPageData (Dictionary<string, string> dic, XmlElement validationRoot);
			string NewPageXSL { get; }
		}
		#endregion
	}
}