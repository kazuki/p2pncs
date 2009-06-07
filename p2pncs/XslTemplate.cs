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
using System.Xml;
using System.Xml.Xsl;
using Kazuki.Net.HttpServer;

namespace p2pncs
{
	class XslTemplate
	{
		Dictionary<string, XslCache> _cache = new Dictionary<string,XslCache> ();
		const string MIME_HTML = "text/html";
		const string MIME_XHTML = "application/xhtml+xml";

		public XslTemplate ()
		{
		}

		public object Process (IHttpRequest req, HttpResponseHeader res, XmlDocument doc, string xsl_path)
		{
			XslCache cache;
			lock (_cache) {
				_cache.TryGetValue (xsl_path, out cache);
			}
			if (cache == null) {
				cache = new XslCache (xsl_path);
				lock (_cache) {
					_cache[xsl_path] = cache;
				}
			}

			bool enable_xhtml = (req.Headers.ContainsKey (HttpHeaderNames.Accept) && req.Headers[HttpHeaderNames.Accept].Contains (MIME_XHTML));
			if (enable_xhtml) {
				res[HttpHeaderNames.ContentType] = MIME_XHTML + "; charset=utf-8";
			} else {
				res[HttpHeaderNames.ContentType] = MIME_HTML + "; charset=utf-8";
			}
			return cache.Transform (doc, !enable_xhtml);
		}

		class XslCache
		{
			object _lock = new object ();
			XslCompiledTransform _xsl_html4;
			XslCompiledTransform _xsl_xhtml;
			DateTime _lastModified = DateTime.MinValue;
			string _path;
			const string NS_XSL = "http://www.w3.org/1999/XSL/Transform";
			const string NS_XHTML = "http://www.w3.org/1999/xhtml";

			public XslCache (string path)
			{
				_path = path;
				_xsl_html4 = new XslCompiledTransform ();
				_xsl_xhtml = new XslCompiledTransform ();
				Check ();
			}

			void Check ()
			{
				DateTime lastModified = File.GetLastWriteTime (_path);
				lock (_lock) {
					if (lastModified == _lastModified)
						return;
					_lastModified = lastModified;

					XmlDocument doc = new XmlDocument ();
					doc.Load (_path);

					// remove xsl:output element
					XmlNodeList list = doc.DocumentElement.GetElementsByTagName ("output", NS_XSL);
					for (int i = 0; i < list.Count; i ++)
						doc.DocumentElement.RemoveChild (list[i]);

					_xsl_html4.Load (SetupHTML4 (doc));
					_xsl_xhtml.Load (SetupXHTML (doc));
				}
			}

			XmlDocument SetupXHTML (XmlDocument doc)
			{
				SetupXSLOutputElement (doc, "xml", "utf-8", "-//W3C//DTD XHTML 1.1//EN", "http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd", false);
				return doc;
			}

			XmlDocument SetupHTML4 (XmlDocument base_doc)
			{
				XmlDocument doc = (XmlDocument)base_doc.CloneNode (true);
				XmlElement new_root = ConvertXHTMLtoHTML (doc, doc.DocumentElement);
				doc.ReplaceChild (new_root, doc.DocumentElement);
				SetupXSLOutputElement (doc, "html", "utf-8", "-//W3C//DTD HTML 4.01//EN", "http://www.w3.org/TR/html4/strict.dtd", false);
				return doc;
			}

			XmlElement ConvertXHTMLtoHTML (XmlDocument doc, XmlElement element)
			{
				XmlElement new_element;
				if (element.NamespaceURI == NS_XHTML) {
					new_element = doc.CreateElement (string.Empty, element.LocalName, string.Empty);
				} else {
					new_element = doc.CreateElement (element.Name, element.NamespaceURI);
				}

				foreach (XmlAttribute att in element.Attributes) {
					if ((att.Name == "xmlns" || att.Name.StartsWith ("xmlns:")) && att.Value == NS_XHTML)
						continue;
					if (att.NamespaceURI == NS_XHTML)
						new_element.SetAttribute (att.LocalName, string.Empty, att.Value);
					else
						new_element.SetAttributeNode ((XmlAttribute)att.Clone ());
				}

				foreach (XmlNode child in element.ChildNodes) {
					XmlElement child_element = child as XmlElement;
					if (child_element != null) {
						new_element.AppendChild (ConvertXHTMLtoHTML (doc, child_element));
					} else {
						new_element.AppendChild (child.Clone ());
					}
				}

				return new_element;
			}

			void SetupXSLOutputElement (XmlDocument doc, string method, string encoding, string docpublic, string docsystem, bool indent)
			{
				// Setup xsl:output
				XmlElement outputSetting = doc.CreateElement ("xsl", "output", NS_XSL);
				outputSetting.SetAttribute ("method", method);
				outputSetting.SetAttribute ("encoding", encoding);
				outputSetting.SetAttribute ("doctype-public", docpublic);
				outputSetting.SetAttribute ("doctype-system", docsystem);
				outputSetting.SetAttribute ("indent", indent ? "yes" : "no");
				if (doc.DocumentElement.FirstChild != null)
					doc.DocumentElement.InsertBefore (outputSetting, doc.DocumentElement.FirstChild);
				else
					doc.DocumentElement.AppendChild (outputSetting);
			}

			public byte[] Transform (XmlDocument doc, bool is_html4)
			{
				Check ();
				XslCompiledTransform xsl = (is_html4 ? _xsl_html4 : _xsl_xhtml);

				byte[] raw;
				using (MemoryStream ms = new MemoryStream ()) {
					xsl.Transform (doc, null, ms);
					ms.Close ();
					raw = ms.ToArray ();
				}
				return raw;
			}
		}
	}
}
