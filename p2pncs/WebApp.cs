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
using System.IO;
using System.Threading;
using System.Xml;
using Kazuki.Net.HttpServer;
using Kazuki.Net.HttpServer.TemplateEngines;
using p2pncs.Net.Overlay.DFS.MMLC;

namespace p2pncs
{
	partial class WebApp : IHttpApplication, IDisposable
	{
		const string DefaultTemplatePath = "templates";
		const string DefaultStaticFilePath = "htdocs";
		const string DefaultDateFormat = "yyyy/MM/dd HH:mm:ss";

		const int MaxRetries = 2;
		static TimeSpan Timeout = TimeSpan.FromSeconds (2);
		const int RetryBufferSize = 512;
		const int DuplicationCheckBufferSize = 512;
		const int MaxStreamSocketSegmentSize = 500;
		const int MaxRequestBodySize = 33554432; // 32MB

		Node _node;
		ManualResetEvent _exitWaitHandle = new ManualResetEvent (false);
		XslTemplateEngine _xslTemplate = new XslTemplateEngine ();
		bool _fastView = true;

		public WebApp (Node node)
		{
			_node = node;
			node.MMLC.Register (BBS.SimpleBBSParser.Instance);
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
				return ProcessBbsNewPage (server, req, res);
			if (absPath == "/bbs/open")
				return ProcessBbsOpenPage (server, req, res);
			if (absPath.StartsWith ("/bbs/"))
				return ProcessBBS (server, req, res, false);
			if (absPath == "/manage" || absPath == "/manage/")
				return ProcessManageTop (server, req, res);
			if (absPath.StartsWith ("/manage/"))
				return ProcessManageFile (server, req, res);
			if (absPath == "/statistics")
				return ProcessStatistics (server, req, res);
			return ProcessStaticFile (server, req, res);
		}

		public static XmlDocument CreateEmptyDocument ()
		{
			XmlDocument doc = new XmlDocument ();
			doc.AppendChild (doc.CreateElement ("page"));
			return doc;
		}

		object ProcessMainPage (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			XmlDocument doc = CreateEmptyDocument ();
			doc.DocumentElement.SetAttribute ("ver", System.Reflection.Assembly.GetEntryAssembly ().GetName ().Version.ToString ());
			return _xslTemplate.Render (server, req, res, doc, Path.Combine (DefaultTemplatePath, "main.xsl"));
		}

		XmlElement CreateMergeableFileElement (XmlDocument doc, MergeableFileHeader header)
		{
			return CreateMergeableFileElement (doc, header, null);
		}

		XmlElement CreateMergeableFileElement (XmlDocument doc, MergeableFileHeader header, MergeableFileRecord[] records)
		{
			XmlElement root = doc.CreateElement ("file", new string[][] {
				new[] {"key", header.Key.ToUriSafeBase64String ()},
				new[] {"recordset", header.RecordsetHash.ToUriSafeBase64String ()},
				new[] {"created", header.CreatedTime.ToLocalTime().ToString (DefaultDateFormat)},
				new[] {"lastManaged", header.LastManagedTime.ToLocalTime().ToString (DefaultDateFormat)},
				new[] {"lastModified", header.LastModifiedTime.ToLocalTime().ToString (DefaultDateFormat)},
				new[] {"records", header.NumberOfRecords.ToString ()},
			}, null);
			XmlNode authServers = root.AppendChild (doc.CreateElement ("auth-servers"));
			if (header.AuthServers != null && header.AuthServers.Length > 0) {
				for (int i = 0; i < header.AuthServers.Length; i++) {
					authServers.AppendChild (doc.CreateElement ("auth-server", new string[][] {
						new[] {"index", i.ToString ()}
					}, new[]{
						doc.CreateElement ("public-key", null, new[]{doc.CreateTextNode (header.AuthServers[i].PublicKey.ToBase64String ())}),
						doc.CreateElement ("serialize", null, new[]{doc.CreateTextNode (header.AuthServers[i].ToParsableString ())})
					}));
				}
			}
			if (IsBBSHeader (header)) {
				root.SetAttribute ("type", "simple-bbs");
				root.AppendChild (CreateBBSHeaderElement (doc, header));
			}

			if (records == null)
				return root;

			XmlElement records_element = (XmlElement)root.AppendChild (doc.CreateElement ("records"));
			foreach (MergeableFileRecord record in records) {
				XmlElement record_element = (XmlElement)records_element.AppendChild (doc.CreateElement ("record", new string[][] {
					new[] {"hash", record.Hash.ToUriSafeBase64String ()},
					new[] {"authidx", record.AuthorityIndex.ToString ()},
					new[] {"created", record.CreatedTime.ToLocalTime().ToString (DefaultDateFormat)}
				}, null));
				if (IsBBSRecord (record)) {
					record_element.SetAttribute ("type", "simple-bbs");
					record_element.AppendChild (CreateBBSRecordElement (doc, record));
				}
			}

			return root;
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
	}
}