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
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using Kazuki.Net.HttpServer;
using openCrypto.EllipticCurve;
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.DFS.MMLC;
using p2pncs.Security.Cryptography;

namespace p2pncs
{
	partial class WebApp
	{
		/// <summary>
		/// URLからファイルのキーを取り出す。
		/// 形式としては、/wiki/FileKey/ や /bbs/FileKey/ のほかに、汎用の /FileKey/ に対応し、
		/// FileKeyの後ろにスラッシュが無くURLが終わっている場合は、最後にスラッシュを付与するようにリダイレクトさせる
		/// </summary>
		Key ParseRequestKey (IHttpRequest req, HttpResponseHeader res, out string tailurl)
		{
			tailurl = null;
			string str_key = req.Url.AbsolutePath.Substring (1);
			int pos = str_key.IndexOf ('/');
			if (pos > 0 && pos < 10) {
				// /wiki や /bbs を除去
				str_key = str_key.Substring (pos + 1);
				pos = str_key.IndexOf ('/');
			}
			if (pos < 0) {
				// キーの末尾の / が無いのでリダイレクト
				res[HttpHeaderNames.Location] = req.Url.AbsolutePath + "/";
				throw new HttpException (req.HttpVersion == HttpVersion.Http10 ? HttpStatusCode.Found : HttpStatusCode.SeeOther);
			} else {
				tailurl = str_key.Substring (pos + 1);
				str_key = str_key.Substring (0, pos);
			}
			try {
				if (str_key.Length == (DefaultAlgorithm.ECDomainBytes + 1) * 2)
					return Key.Parse (str_key);
				else if (str_key.Length == (DefaultAlgorithm.ECDomainBytes + 1) * 4 / 3)
					return Key.FromUriSafeBase64String (str_key);
				throw new HttpException (HttpStatusCode.NotFound);
			} catch {
				throw new HttpException (HttpStatusCode.NotFound);
			}
		}

		object Process_File (IHttpRequest req, HttpResponseHeader res)
		{
			string tailurl;
			Key key = ParseRequestKey (req, res, out tailurl);
			MergeableFileHeader header = _node.MMLC.GetMergeableFileHeader (key);
			return Process_ViewMergeableFilePage (req, res, key, header, tailurl);
		}

		object ProcessFileList (IHttpRequest req, HttpResponseHeader res)
		{
			XmlDocument doc = XmlHelper.CreateEmptyDocument ();
			XmlElement rootNode = doc.DocumentElement;
			MergeableFileHeader[] headers = _node.MMLC.GetHeaderList ();

			bool include_empty = req.QueryData.ContainsKey ("empty");
			foreach (MergeableFileHeader header in headers) {
				if (header.RecordsetHash.IsZero () && !include_empty)
					continue;
				rootNode.AppendChild (XmlHelper.CreateMergeableFileElement (doc, header));
			}
			return _xslTemplate.Render (req, res, doc, Path.Combine (DefaultTemplatePath, "list.xsl"));
		}

		object ProcessFileOpen (IHttpRequest req, HttpResponseHeader res)
		{
			if (req.HttpMethod == HttpMethod.POST && req.HasContentBody ()) {
				Dictionary<string, string> dic = HttpUtility.ParseUrlEncodedStringToDictionary (Encoding.ASCII.GetString (req.GetContentBody (MaxRequestBodySize)), Encoding.UTF8);
				string key;
				if (dic.TryGetValue ("id", out key)) {
					res[HttpHeaderNames.Location] = key;
					throw new HttpException (req.HttpVersion == HttpVersion.Http10 ? HttpStatusCode.Found : HttpStatusCode.SeeOther);
				}
			}
			XmlDocument doc = XmlHelper.CreateEmptyDocument ();
			return _xslTemplate.Render (req, res, doc, Path.Combine (DefaultTemplatePath, "open.xsl"));
		}
	}
}
