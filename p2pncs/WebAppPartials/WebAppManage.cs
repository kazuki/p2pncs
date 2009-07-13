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
		object ProcessManageTop (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			XmlDocument doc = CreateEmptyDocument ();
			MergeableFileHeader[] headers = _node.MMLC.GetOwnMergeableFiles ();

			foreach (MergeableFileHeader header in headers) {
				doc.DocumentElement.AppendChild (CreateMergeableFileElement (doc, header));
			}

			return _xslTemplate.Render (server, req, res, doc, Path.Combine (DefaultTemplatePath, "manage.xsl"));
		}

		object ProcessManageFile (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
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
			if (req.HttpMethod == HttpMethod.POST && req.HasContentBody ()) {
				header = _node.MMLC.GetMergeableFileHeader (key);
				if (header == null)
					throw new HttpException (HttpStatusCode.NotFound);
				NameValueCollection c = HttpUtility.ParseUrlEncodedStringToNameValueCollection (Encoding.ASCII.GetString (req.GetContentBody (MaxRequestBodySize)), Encoding.UTF8);
				AuthServerInfo[] auth_servers = AuthServerInfo.ParseArray (c["auth"]);
				List<Key> list = new List<Key> ();
				string[] keep_array = c.GetValues ("record");
				if (keep_array != null) {
					for (int i = 0; i < keep_array.Length; i++) {
						list.Add (Key.FromUriSafeBase64String (keep_array[i]));
					}
				}
				IHashComputable new_header_content;
				if (IsBBSHeader (header)) {
					new_header_content = ProcessBBS_ManageCreateNewHeader (c);
				} else {
					throw new HttpException (HttpStatusCode.InternalServerError);
				}
				_node.MMLC.Manage (key, new_header_content, auth_servers, list.ToArray (), null);

				res[HttpHeaderNames.Location] = "/bbs/" + key.ToUriSafeBase64String ();
				throw new HttpException (req.HttpVersion == HttpVersion.Http10 ? HttpStatusCode.Found : HttpStatusCode.SeeOther);
			}

			List<MergeableFileRecord> records = _node.MMLC.GetRecords (key, out header);
			if (header == null || records == null)
				throw new HttpException (HttpStatusCode.NotFound);

			XmlDocument doc = CreateEmptyDocument ();
			doc.DocumentElement.AppendChild (CreateMergeableFileElement (doc, header, records.ToArray ()));

			string xsl_filename;
			if (IsBBSHeader (header))
				xsl_filename = "manage-simplebbs.xsl";
			else
				throw new HttpException (HttpStatusCode.InternalServerError);

			return _xslTemplate.Render (server, req, res, doc, Path.Combine (DefaultTemplatePath, xsl_filename));
		}
	}
}
