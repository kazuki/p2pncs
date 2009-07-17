﻿/*
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
using p2pncs.Wiki;

namespace p2pncs
{
	partial class WebApp
	{
		object ProcessWikiListPage (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			XmlDocument doc = XmlHelper.CreateEmptyDocument ();
			XmlElement rootNode = doc.DocumentElement;
			MergeableFileHeader[] headers = _node.MMLC.GetHeaderList ();
			foreach (MergeableFileHeader header in headers) {
				rootNode.AppendChild (XmlHelper.CreateMergeableFileElement (doc, header));
			}
			return _xslTemplate.Render (server, req, res, doc, Path.Combine (DefaultTemplatePath, "wiki_list.xsl"));
		}

		object ProcessWikiPage (IHttpServer server, IHttpRequest req, HttpResponseHeader res, bool callByCallback)
		{
			string str_key = req.Url.AbsolutePath.Substring (6);
			Key key;
			string page_title;
			int len = str_key.IndexOf ('/');
			if (len < 0) {
				res[HttpHeaderNames.Location] = req.Url.AbsolutePath + "/";
				throw new HttpException (req.HttpVersion == HttpVersion.Http10 ? HttpStatusCode.Found : HttpStatusCode.SeeOther);
			}
			try {
				if (len == (DefaultAlgorithm.ECDomainBytes + 1) * 2)
					key = Key.Parse (str_key.Substring (0, len));
				else if (len == (DefaultAlgorithm.ECDomainBytes + 1) * 4 / 3)
					key = Key.FromUriSafeBase64String (str_key.Substring (0, len));
				else
					throw new HttpException (HttpStatusCode.NotFound);
				page_title = HttpUtility.UrlDecode (len == str_key.Length ? string.Empty : str_key.Substring (len).TrimStart ('/'), Encoding.UTF8);
				if (page_title == "StartPage" || page_title == "FrontPage")
					page_title = string.Empty;
			} catch {
				throw new HttpException (HttpStatusCode.NotFound);
			}

			MergeableFileHeader header;
			List<MergeableFileRecord> records = _node.MMLC.GetRecords (key, out header);
			if (!callByCallback) {
				if (!_fastView || header == null || header.RecordsetHash.IsZero ()) {
					ManualResetEvent done = new ManualResetEvent (false);
					CometInfo info = new CometInfo (done, req, res, null, DateTime.Now + TimeSpan.FromSeconds (5), ProcessWikiPage_CometHandler);
					_node.MMLC.StartMerge (key, ProcessBBS_Callback, done);
					return info;
				} else {
					_node.MMLC.StartMerge (key, null, null);
				}
			}
			if (header == null)
				throw new HttpException (HttpStatusCode.NotFound);

			switch (GetSpecialPageType (page_title)) {
				case SpecialPageType.None:
					return ProcessWikiPage_Default (server, req, res, header, records, page_title);
				case SpecialPageType.TitleIndex:
					return ProcessWikiPage_TitleIndex (server, req, res, header, records);
				default:
					throw new HttpException (HttpStatusCode.InternalServerError);
			}
		}

		object ProcessWikiPage_CometHandler (CometInfo info)
		{
			info.WaitHandle.Close ();
			return ProcessWikiPage (null, info.Request, info.Response, true);
		}

		object ProcessWikiPage_Default (IHttpServer server, IHttpRequest req, HttpResponseHeader res, MergeableFileHeader header, List<MergeableFileRecord> records, string page_title)
		{
			XmlDocument doc = XmlHelper.CreateEmptyDocument ();
			MergeableFileRecord record = GetLatestPage (records, page_title);
			if (req.HttpMethod == HttpMethod.POST && req.HasContentBody ()) {
				Dictionary<string, string> dic = HttpUtility.ParseUrlEncodedStringToDictionary (Encoding.ASCII.GetString (req.GetContentBody (MaxRequestBodySize)), Encoding.UTF8);
				record = new MergeableFileRecord (
						new WikiRecord (page_title, null, Helpers.GetValueSafe (dic, "name"), WikiMarkupType.PukiWiki,
						Encoding.UTF8.GetBytes (Helpers.GetValueSafe (dic, "body")), WikiCompressType.None, WikiDiffType.None),
						DateTime.UtcNow, header.LastManagedTime, null, null, null, 0, null);
				if (dic.ContainsKey ("preview")) {
					doc.DocumentElement.SetAttribute ("state", "preview");
				} else if (dic.ContainsKey ("update")) {
					_node.MMLC.AppendRecord (header.Key, record);
					res[HttpHeaderNames.Location] = "/wiki/" + header.Key.ToUriSafeBase64String () + "/" + HttpUtility.UrlEncode (page_title, Encoding.UTF8);
					throw new HttpException (req.HttpVersion == HttpVersion.Http10 ? HttpStatusCode.Found : HttpStatusCode.SeeOther);
				} else {
					throw new HttpException (HttpStatusCode.Forbidden);
				}
			}
			if (record == null) {
				doc.DocumentElement.AppendChild (XmlHelper.CreateMergeableFileElement (doc, header));
			} else {
				doc.DocumentElement.AppendChild (XmlHelper.CreateMergeableFileElement (doc, header, new MergeableFileRecord[] {record}));
			}
			doc.DocumentElement.AppendChild (doc.CreateElement ("page-title", null, new []{doc.CreateTextNode (page_title)}));
			doc.DocumentElement.AppendChild (doc.CreateElement ("page-title-for-url", null, new[]{doc.CreateTextNode (WikiTitleToUrl (page_title))}));

			string xsl = "wiki.xsl";
			if (req.QueryData.ContainsKey ("edit"))
				xsl = "wiki_edit.xsl";
			return _xslTemplate.Render (server, req, res, doc, Path.Combine (DefaultTemplatePath, xsl));
		}

		object ProcessWikiPage_TitleIndex (IHttpServer server, IHttpRequest req, HttpResponseHeader res, MergeableFileHeader header, List<MergeableFileRecord> records)
		{
			XmlDocument doc = XmlHelper.CreateEmptyDocument ();
			Dictionary<string, MergeableFileRecord> dic = new Dictionary<string,MergeableFileRecord> ();
			for (int i = 0; i < records.Count; i ++) {
				WikiRecord wr = records[i].Content as WikiRecord;
				MergeableFileRecord r;
				if (!dic.TryGetValue (wr.PageName, out r) || r.CreatedTime < records[i].CreatedTime)
					dic[wr.PageName] = records[i];
			}

			List<MergeableFileRecord> list = new List<MergeableFileRecord> (dic.Values);
			doc.DocumentElement.AppendChild (XmlHelper.CreateMergeableFileElement (doc, header, list.ToArray ()));
			return _xslTemplate.Render (server, req, res, doc, Path.Combine (DefaultTemplatePath, "wiki_titleindex.xsl"));
		}

		SpecialPageType GetSpecialPageType (string page_title)
		{
			if (page_title == "TitleIndex")
				return SpecialPageType.TitleIndex;
			return SpecialPageType.None;
		}

		MergeableFileRecord GetLatestPage (List<MergeableFileRecord> records, string page_title)
		{
			MergeableFileRecord latest = null;
			for (int i = 0; i < records.Count; i ++) {
				WikiRecord r = records[i].Content as WikiRecord;
				if (r.PageName == page_title && (latest == null || latest.CreatedTime < records[i].CreatedTime))
					latest = records[i];
			}
			return latest;
		}

		public static string WikiTitleToUrl (string title)
		{
			title = Uri.EscapeUriString (title);
			title = title.Replace ("/", "%2F");
			return title;
		}

		enum SpecialPageType
		{
			None,
			TitleIndex
		}
	}
}
namespace p2pncs.Wiki
{
	class WikiWebApp : WebApp.IMergeableFileCommonProcess
	{
		static WikiWebApp _instance = new WikiWebApp ();
		public static WikiWebApp Instance {
			get { return _instance; }
		}
		WikiWebApp () {}

		#region IMergeableFileCommonProcess Members

		void ParseNewPagePostData (Dictionary<string, string> dic, out string title, out bool title_check)
		{
			title = Helpers.GetValueSafe (dic, "title").Trim ();
			title_check = (title.Length > 0 && title.Length <= 64);
		}

		public bool ParseNewPagePostData (Dictionary<string, string> dic, out IHashComputable header, out IHashComputable[] records)
		{
			string title;
			bool title_check;
			ParseNewPagePostData (dic, out title, out title_check);
			
			if (!title_check) {
				header = null;
				records = null;
				return false;
			}

			header = new WikiHeader (title, false);
			records = null;
			return true;
		}

		public void OutputNewPageData (Dictionary<string, string> dic, XmlElement validationRoot)
		{
			XmlDocument doc = validationRoot.OwnerDocument;
			string title;
			bool title_check;
			ParseNewPagePostData (dic, out title, out title_check);

			validationRoot.AppendChild (doc.CreateElement ("data", new string[][] { new[] { "name", "title" }, new[] { "status", title_check ? "ok" : "error" } }, new[] {
				doc.CreateElement ("value", null, new[]{doc.CreateTextNode (title)}),
				title_check ? null : doc.CreateElement ("msg", null, new[]{doc.CreateTextNode ("タイトルは1文字～64文字に収まらなければいけません")})
			}));
		}

		public string NewPageXSL {
			get { return "wiki_new.xsl"; }
		}

		#endregion
	}
}