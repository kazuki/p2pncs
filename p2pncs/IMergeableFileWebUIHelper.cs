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

using System.Collections.Specialized;
using System.Xml;
using p2pncs.Net.Overlay.DFS.MMLC;
using p2pncs.Security.Cryptography;
using Kazuki.Net.HttpServer;

namespace p2pncs
{
	interface IMergeableFileWebUIHelper
	{
		XmlElement CreateHeaderElement (XmlDocument doc, MergeableFileHeader header);
		XmlElement CreateRecordElement (XmlDocument doc, MergeableFileRecord record);
		IHashComputable CreateHeaderContent (NameValueCollection c);
		string ContentType { get; }
		string ManagePageXslFileName { get; }
		string ViewUrl { get; }

		// GETメソッドに対する応答
		object ProcessGetRequest (Node node, IHttpRequest req, HttpResponseHeader res, MergeableFileHeader header, string url_tail);
		
		// PUTメソッドに対する応答 (但し、クエリ文字列が含まれる場合)
		object ProcessPutRequest (Node node, IHttpRequest req, HttpResponseHeader res, MergeableFileHeader header, string url_tail);
	}
}
