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
using System.Text;
using System.Xml;
using p2pncs.Net.Overlay.DFS.MMLC;

namespace p2pncs
{
	public static class XmlHelper
	{
		public const string DefaultDateFormat = "yyyy/MM/dd HH:mm:ss";

		public static XmlDocument CreateEmptyDocument ()
		{
			XmlDocument doc = new XmlDocument ();
			doc.AppendChild (doc.CreateElement ("page"));
			return doc;
		}

		public static XmlElement CreateElement (this XmlDocument doc, string name, string[][] atts, XmlNode[] children)
		{
			XmlElement element = doc.CreateElement (name);
			if (atts != null) {
				for (int i = 0; i < atts.Length; i ++) {
					if (atts[i] == null) continue;
					if (atts[i].Length == 2)
						element.SetAttribute (atts[i][0], atts[i][1]);
				}
			}
			if (children != null) {
				for (int i = 0; i < children.Length; i ++) {
					if (children[i] != null) {
						element.AppendChild (children[i]);
					}
				}
			}
			return element;
		}

		public static XmlText CreateTextNodeSafe (this XmlDocument doc, string text)
		{
			StringBuilder sb = null;
			int start = 0;

			// Check Character Range (http://www.w3.org/TR/REC-xml/#charsets)
			for (int i = 0; i < text.Length; i ++) {
				int c = (int)text[i];
				if (c == 0x9 || c == 0xA || c == 0xD || (c >= 0x20 && c <= 0xD7FF) || (c >= 0xE000 && c <= 0xFFFD) || (c >= 0x10000 && c <= 0x10FFFF))
					continue;

				if (sb == null)
					sb = new StringBuilder (text.Length);
				if (i > start)
					sb.Append (text, start, i - start);
				start = i + 1;
			}

			if (sb == null) {
				return doc.CreateTextNode (text);
			} else {
				if (text.Length > start)
					sb.Append (text, start, text.Length - start);
				return doc.CreateTextNode (sb.ToString ());
			}
		}

		public static XmlElement CreateMergeableFileElement (XmlDocument doc, MergeableFileHeader header)
		{
			return CreateMergeableFileElement (doc, header, null);
		}

		public static XmlElement CreateMergeableFileElement (XmlDocument doc, MergeableFileHeader header, MergeableFileRecord[] records)
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

			root.SetAttribute ("type", (header.Content as IMergeableFile).WebUIHelper.ContentType);
			root.AppendChild ((header.Content as IMergeableFile).WebUIHelper.CreateHeaderElement (doc, header));

			if (records == null)
				return root;

			XmlElement records_element = (XmlElement)root.AppendChild (doc.CreateElement ("records"));
			foreach (MergeableFileRecord record in records) {
				XmlElement record_element = (XmlElement)records_element.AppendChild (doc.CreateElement ("record", new string[][] {
					new[] {"hash", record.Hash.ToUriSafeBase64String ()},
					new[] {"authidx", record.AuthorityIndex.ToString ()},
					new[] {"created", record.CreatedTime.ToLocalTime().ToString (DefaultDateFormat)}
				}, null));
				record_element.AppendChild ((header.Content as IMergeableFile).WebUIHelper.CreateRecordElement (doc, record));
			}

			return root;
		}
	}
}
