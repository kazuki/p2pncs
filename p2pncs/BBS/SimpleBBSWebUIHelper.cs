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
using System.Xml;
using p2pncs.Net.Overlay.DFS.MMLC;
using p2pncs.Security.Cryptography;
using Kazuki.Net.HttpServer;

namespace p2pncs.BBS
{
	class SimpleBBSWebUIHelper : IMergeableFileWebUIHelper
	{
		static SimpleBBSWebUIHelper _instance = new SimpleBBSWebUIHelper ();
		SimpleBBSWebUIHelper () {}

		public static SimpleBBSWebUIHelper Instance {
			get { return _instance; }
		}

		public XmlElement CreateHeaderElement (XmlDocument doc, MergeableFileHeader header)
		{
			SimpleBBSHeader content = header.Content as SimpleBBSHeader;
			return doc.CreateElement ("bbs", null, new[] {
				doc.CreateElement ("title", null, new[] {
					doc.CreateTextNodeSafe (content.Title)
				})
			});
		}

		public XmlElement CreateRecordElement (XmlDocument doc, MergeableFileRecord record)
		{
			SimpleBBSRecord record_content = record.Content as SimpleBBSRecord;
			return doc.CreateElement ("bbs", new string[][] {
				new string[] {"short-id", record_content.GetShortId (record)}
			}, new[] {
				doc.CreateElement ("name", null, new[] {
					doc.CreateTextNodeSafe (record_content.Name)
				}),
				doc.CreateElement ("body", null, new[] {
					doc.CreateTextNodeSafe (record_content.Body)
				})
			});
		}

		public IHashComputable CreateHeaderContent (NameValueCollection c)
		{
			return new SimpleBBSHeader (c["title"]);
		}

		public string ContentType {
			get { return "simple-bbs"; }
		}

		public string ManagePageXslFileName {
			get { return "manage-simplebbs.xsl"; }
		}

		public string ViewUrl {
			get { return "/bbs/"; }
		}

		public object ProcessGetRequest (Node node, IHttpRequest req, HttpResponseHeader res, MergeableFileHeader header, string url_tail)
		{
			return BBS.BBSWebApp.Instance.ProcessGetRequest (node, req, res, header, url_tail);
		}

		public object ProcessPutRequest (Node node, IHttpRequest req, HttpResponseHeader res, MergeableFileHeader header, string url_tail)
		{
			throw new HttpException (HttpStatusCode.NotFound);
		}
	}
}
