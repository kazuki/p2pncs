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

namespace p2pncs
{
	partial class WebApp : IHttpApplication, IDisposable
	{
		public const string DefaultTemplatePath = "templates";
		const string DefaultStaticFilePath = "htdocs";
		public const int MaxRequestBodySize = 33554432; // 32MB

		Node _node;
		ManualResetEvent _exitWaitHandle = new ManualResetEvent (false);
		static XslTemplateEngine _xslTemplate = new XslTemplateEngine ();
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
				return ProcessMainPage (req, res);
			if (absPath == "/net/init")
				return ProcessNetInitPage (req, res);
			if (absPath == "/net/exit")
				return ProcessNetExitPage (req, res);
			if (absPath == "/bbs" || absPath == "/bbs/")
				return ProcessBbsListPage (req, res);
			if (absPath == "/bbs/new")
				return Process_NewMergeableFilePage (req, res, BBS.BBSWebApp.Instance);
			if (absPath == "/bbs/open")
				return ProcessBbsOpenPage (req, res);
			/*if (absPath.StartsWith ("/bbs/"))
				return ProcessBBS (server, req, res, false);*/
			if (absPath == "/wiki" || absPath == "/wiki/")
				return ProcessWikiListPage (req, res);
			if (absPath == "/wiki/new")
				return Process_NewMergeableFilePage (req, res, Wiki.WikiWebApp.Instance);
			/*if (absPath.StartsWith ("/wiki/"))
				return ProcessWikiPage (server, req, res, false);*/
			if (absPath == "/manage" || absPath == "/manage/")
				return ProcessManageTop (req, res);
			if (absPath.StartsWith ("/manage/"))
				return ProcessManageFile (req, res);
			if (absPath == "/statistics")
				return ProcessStatistics (req, res);
			
			string ext = Path.GetExtension (req.Url.AbsolutePath);
			if (ext.Equals (".css", StringComparison.InvariantCultureIgnoreCase) ||
				ext.Equals (".js", StringComparison.InvariantCultureIgnoreCase) ||
				ext.Equals (".png", StringComparison.InvariantCultureIgnoreCase) ||
				ext.Equals (".jpg", StringComparison.InvariantCultureIgnoreCase)) {
				return ProcessStaticFile (req, res);
			}
			return Process_File (req, res);
		}

		object ProcessMainPage (IHttpRequest req, HttpResponseHeader res)
		{
			XmlDocument doc = CreateDocumentWithNetworkState ();
			doc.DocumentElement.SetAttribute ("ver", System.Reflection.Assembly.GetEntryAssembly ().GetName ().Version.ToString ());
			return _xslTemplate.Render (req, res, doc, Path.Combine (DefaultTemplatePath, "main.xsl"));
		}

		object ProcessStaticFile (IHttpRequest req, HttpResponseHeader res)
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

		public static XslTemplateEngine Template {
			get { return _xslTemplate; }
		}

		public XmlDocument CreateDocumentWithNetworkState ()
		{
			XmlDocument doc = XmlHelper.CreateEmptyDocument ();
			AddNetworkState (doc);
			return doc;
		}

		void AddNetworkState (XmlDocument doc)
		{
			doc.DocumentElement.AppendChild (doc.CreateElement ("network-state", null, new XmlNode[] {
				doc.CreateElement ("mmlc-mcr", null, new XmlNode[] {
					doc.CreateTextNode (_node.MMLC.MCRInfo.Status.ToString ())
				})
			}));
		}
	}
}