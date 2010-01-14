/*
 * Copyright (C) 2009-2010 Kazuki Oikawa
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

using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Xml;
using Kazuki.Net.HttpServer;
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.DFS.MMLC;
using p2pncs.Security.Cryptography;

namespace p2pncs
{
	partial class WebApp
	{
		object ProcessManageTop (IHttpRequest req, HttpResponseHeader res)
		{
			XmlDocument doc = XmlHelper.CreateEmptyDocument ();
			MergeableFileHeader[] headers = _node.MMLC.GetOwnMergeableFiles ();

			foreach (MergeableFileHeader header in headers) {
				doc.DocumentElement.AppendChild (XmlHelper.CreateMergeableFileElement (doc, header));
			}

			return _xslTemplate.Render (req, res, doc, Path.Combine (DefaultTemplatePath, "manage.xsl"));
		}

		object ProcessManageFile (IHttpRequest req, HttpResponseHeader res)
		{
			string str_key = req.Url.AbsolutePath.Substring (8);
			Key key;
			try {
				if (str_key.Length == (DefaultAlgorithm.ECDomainBytes + 1) * 2)
					key = Key.Parse (str_key);
				else if (str_key.Length == (DefaultAlgorithm.ECDomainBytes + 1) * 4 / 3)
					key = Key.FromUriSafeBase64String (str_key);
				else
					throw new HttpException (HttpStatusCode.NotFound);
			} catch {
				throw new HttpException (HttpStatusCode.NotFound);
			}

			MergeableFileHeader header;
			IMergeableFileWebUIHelper header_helper = null;
			if (req.HttpMethod == HttpMethod.POST && req.HasContentBody ()) {
				header = _node.MMLC.GetMergeableFileHeader (key);
				if (header == null)
					throw new HttpException (HttpStatusCode.NotFound);
				header_helper = (header.Content as IMergeableFile).WebUIHelper;
				NameValueCollection c = HttpUtility.ParseUrlEncodedStringToNameValueCollection (Encoding.ASCII.GetString (req.GetContentBody (MaxRequestBodySize)), Encoding.UTF8);
				AuthServerInfo[] auth_servers = AuthServerInfo.ParseArray (c["auth"]);
				List<Key> list = new List<Key> ();
				string[] keep_array = c.GetValues ("record");
				if (keep_array != null) {
					for (int i = 0; i < keep_array.Length; i++) {
						list.Add (Key.FromUriSafeBase64String (keep_array[i]));
					}
				}
				IHashComputable new_header_content = header_helper.CreateHeaderContent (c);
				string title = c["title"];
				if (title == null || title.Length == 0 || title.Length > 64)
					throw new HttpException (HttpStatusCode.InternalServerError);
				MergeableFileHeader new_header = new MergeableFileHeader (key, title, header.Flags, header.CreatedTime, new_header_content, auth_servers);
				_node.MMLC.Manage (new_header, list.ToArray (), null);

				res[HttpHeaderNames.Location] = header_helper.ViewUrl + key.ToUriSafeBase64String ();
				throw new HttpException (req.HttpVersion == HttpVersion.Http10 ? HttpStatusCode.Found : HttpStatusCode.SeeOther);
			}

			List<MergeableFileRecord> records = _node.MMLC.GetRecords (key, out header);
			if (header == null || records == null)
				throw new HttpException (HttpStatusCode.NotFound);
			header_helper = (header.Content as IMergeableFile).WebUIHelper;

			XmlDocument doc = XmlHelper.CreateEmptyDocument ();
			doc.DocumentElement.AppendChild (XmlHelper.CreateMergeableFileElement (doc, header, records.ToArray ()));

			return _xslTemplate.Render (req, res, doc, Path.Combine (DefaultTemplatePath, header_helper.ManagePageXslFileName));
		}
	}
}
