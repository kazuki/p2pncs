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
using System.Collections.Specialized;
using System.Text;
using System.Xml;
using p2pncs.Net.Overlay.DFS.MMLC;
using p2pncs.Security.Cryptography;
using p2pncs.Wiki.Engine;
using Kazuki.Net.HttpServer;

namespace p2pncs.Wiki
{
	class WikiWebUIHelper : IMergeableFileWebUIHelper
	{
		static WikiWebUIHelper _instance = new WikiWebUIHelper ();
		WikiWebUIHelper () { }

		public static WikiWebUIHelper Instance {
			get { return _instance; }
		}

		public XmlElement CreateHeaderElement (XmlDocument doc, MergeableFileHeader header)
		{
			WikiHeader content = header.Content as WikiHeader;
			return doc.CreateElement ("wiki");
		}

		public XmlElement CreateRecordElement (XmlDocument doc, MergeableFileRecord record)
		{
			WikiRecord content = record.Content as WikiRecord;
			return doc.CreateElement ("wiki", new string[][] {
				new string[] {"markup-type", content.MarkupType.ToString ()}
			}, new[] {
				doc.CreateElement ("title", null, new[] {
					doc.CreateTextNodeSafe (content.PageName)
				}),
				doc.CreateElement ("title-for-url", null, new[] {
					doc.CreateTextNodeSafe (WikiWebApp.WikiTitleToUrl (content.PageName))
				}),
				doc.CreateElement ("name", null, new[] {
					doc.CreateTextNodeSafe (content.Name)
				}),
				doc.CreateElement ("body", null, new[] {
					CreateWikiBody (doc, content)
				}),
				doc.CreateElement ("raw-body", null, new[] {
					doc.CreateTextNodeSafe (content.Body)
				}),
			});
		}

		XmlNode CreateWikiBody (XmlDocument doc, WikiRecord content)
		{
			switch (content.MarkupType) {
				case WikiMarkupType.PukiWiki:
					return CreateWikiBodyFromPukiWikiMarkup (doc, content);
				default:
					return CreateWikiBodyFromPlainText (doc, content);
			}
		}

		XmlNode CreateWikiBodyFromPlainText (XmlDocument doc, WikiRecord content)
		{
			return doc.CreateTextNodeSafe (content.Body);
		}

		XmlNode CreateWikiBodyFromPukiWikiMarkup (XmlDocument doc, WikiRecord content)
		{
			WikiRootElement root = PukiWikiMarkupParser.Instance.Parse (content.Body);
			return doc.CreateTextNodeSafe (WikiHtmlRenderer.Instance.Render (root, PukiWikiMarkupParser.Instance));
		}

		public IHashComputable CreateHeaderContent (NameValueCollection c)
		{
			return new WikiHeader ();
		}

		public string ContentType {
			get { return "wiki"; }
		}

		public string ManagePageXslFileName {
			get { return "manage-wiki.xsl"; }
		}

		public string ViewUrl {
			get { return "/wiki/"; }
		}

		public object ProcessGetRequest (Node node, IHttpRequest req, HttpResponseHeader res, MergeableFileHeader header, string url_tail)
		{
			return WikiWebApp.Instance.ProcessGetRequest (node, req, res, header, url_tail);
		}

		public object ProcessPutRequest (Node node, IHttpRequest req, HttpResponseHeader res, MergeableFileHeader header, string url_tail)
		{
			return WikiWebApp.Instance.ProcessGetRequest (node, req, res, header, url_tail);
		}
	}
}
