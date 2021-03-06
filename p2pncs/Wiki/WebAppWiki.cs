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

		public bool ParseNewPagePostData (Dictionary<string, string> dic, out IHashComputable header, out IHashComputable[] records)
		{
			header = new WikiHeader ();
			records = null;
			return true;
		}

		public void OutputNewPageData (Dictionary<string, string> dic, XmlElement validationRoot)
		{
		}

		public string NewPageXSL {
			get { return "wiki_new.xsl"; }
		}

		public IHashComputable ParseNewPostData (Dictionary<string, string> dic)
		{
			string title = Helpers.GetValueSafe (dic, "title").Trim ();
			string name = Helpers.GetValueSafe (dic, "name").Trim ();
			string body = Helpers.GetValueSafe (dic, "body").Trim ();
			bool use_lzma = Helpers.GetValueSafe (dic, "lzma").Trim().Length > 0;
			string str_parent = Helpers.GetValueSafe (dic, "parent").Trim ();
			if (body.Length == 0)
				throw new ArgumentException ("本文には文字を入力する必要があります");
			Key parentHash = null;
			if (str_parent.Length > 0) {
				try {
					parentHash = Key.FromUriSafeBase64String (str_parent);
				} catch {}
			}
			byte[] raw_body = Encoding.UTF8.GetBytes (body);
			WikiCompressType ctype = WikiCompressType.None;
			if (use_lzma) {
				byte[] compressed = p2pncs.Utility.LzmaUtility.Compress (raw_body);
				if (compressed.Length < raw_body.Length) {
					raw_body = compressed;
					ctype = WikiCompressType.LZMA;
				}
			}
			return new WikiRecord (title, parentHash == null ? null : new Key[] {parentHash}, name, WikiMarkupType.PukiWiki, body, raw_body, ctype, WikiDiffType.None);
		}

		#endregion

		public object ProcessGetRequest (Node node, IHttpRequest req, HttpResponseHeader res, MergeableFileHeader header, string url_tail)
		{
			string page_title = HttpUtility.UrlDecode (url_tail.Trim ().Trim ('/'), Encoding.UTF8);
			if (page_title == "StartPage" || page_title == "FrontPage")
				page_title = string.Empty;

			List<MergeableFileRecord> records = node.MMLC.GetRecords (header.Key, out header);
			if (records == null)
				throw new HttpException (HttpStatusCode.NotFound);

			switch (GetSpecialPageType (page_title)) {
				case SpecialPageType.None:
					return ProcessWikiPage (node, req, res, header, records, page_title);
				case SpecialPageType.TitleIndex:
					return ProcessWikiPage_TitleIndex (req, res, header, records);
				default:
					throw new HttpException (HttpStatusCode.InternalServerError);
			}
		}

		object ProcessWikiPage (Node node, IHttpRequest req, HttpResponseHeader res, MergeableFileHeader header, List<MergeableFileRecord> records, string page_title)
		{
			XmlDocument doc = XmlHelper.CreateEmptyDocument ();
			MergeableFileRecord record = null;
			if (req.HttpMethod == HttpMethod.POST && req.HasContentBody ()) {
				Dictionary<string, string> dic = HttpUtility.ParseUrlEncodedStringToDictionary (Encoding.ASCII.GetString (req.GetContentBody (WebApp.MaxRequestBodySize)), Encoding.UTF8);
				record = new MergeableFileRecord (ParseNewPostData (dic),
						DateTime.UtcNow, header.LastManagedTime, null, null, null, 0, null);
				if (dic.ContainsKey ("preview")) {
					doc.DocumentElement.SetAttribute ("state", "preview");
				} else {
					throw new HttpException (HttpStatusCode.Forbidden);
				}
			}

			string xsl = "wiki.xsl";
			bool edit_mode = req.QueryData.ContainsKey ("edit");
			bool history_mode = req.QueryData.ContainsKey ("history") & !edit_mode;
			if (record == null && !history_mode)
				record = GetLatestPage (records, page_title);
			if (!history_mode) {
				if (edit_mode) {
					xsl = "wiki_edit.xsl";
					if (req.HttpMethod == HttpMethod.GET && record != null)
						(record.Content as WikiRecord).Name = "";
				}
				if (record == null)
					doc.DocumentElement.AppendChild (XmlHelper.CreateMergeableFileElement (doc, header));
				else
					doc.DocumentElement.AppendChild (XmlHelper.CreateMergeableFileElement (doc, header, new MergeableFileRecord[] { record }));
			} else if (history_mode) {
				records.RemoveAll (delegate (MergeableFileRecord ri) {
					WikiRecord wr = (WikiRecord)ri.Content;
					return !wr.PageName.Equals (page_title);
				});
				doc.DocumentElement.AppendChild (XmlHelper.CreateMergeableFileElement (doc, header, records.ToArray ()));
				xsl = "wiki_history.xsl";
			}
			doc.DocumentElement.AppendChild (doc.CreateElement ("page-title", null, new[] { doc.CreateTextNode (page_title) }));
			doc.DocumentElement.AppendChild (doc.CreateElement ("page-title-for-url", null, new[] { doc.CreateTextNode (WikiTitleToUrl (page_title)) }));

			return WebApp.Template.Render (req, res, doc, Path.Combine (WebApp.DefaultTemplatePath, xsl));
		}

		object ProcessWikiPage_TitleIndex (IHttpRequest req, HttpResponseHeader res, MergeableFileHeader header, List<MergeableFileRecord> records)
		{
			XmlDocument doc = XmlHelper.CreateEmptyDocument ();
			Dictionary<string, MergeableFileRecord> dic = new Dictionary<string, MergeableFileRecord> ();
			for (int i = 0; i < records.Count; i++) {
				WikiRecord wr = records[i].Content as WikiRecord;
				MergeableFileRecord r;
				if (!dic.TryGetValue (wr.PageName, out r) || r.CreatedTime < records[i].CreatedTime)
					dic[wr.PageName] = records[i];
			}

			List<MergeableFileRecord> list = new List<MergeableFileRecord> (dic.Values);
			doc.DocumentElement.AppendChild (XmlHelper.CreateMergeableFileElement (doc, header, list.ToArray ()));
			return WebApp.Template.Render (req, res, doc, Path.Combine (WebApp.DefaultTemplatePath, "wiki_titleindex.xsl"));
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
			for (int i = 0; i < records.Count; i++) {
				WikiRecord r = records[i].Content as WikiRecord;
				if (r.PageName == page_title && (latest == null || latest.CreatedTime < records[i].CreatedTime))
					latest = records[i];
			}
			return latest;
		}

		public static string WikiTitleToUrl (string title)
		{
			title = Kazuki.Net.HttpServer.HttpUtility.UrlEncode (title, Encoding.UTF8);
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