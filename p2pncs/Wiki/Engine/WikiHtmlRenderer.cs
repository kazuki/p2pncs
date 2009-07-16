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
using System.Text.RegularExpressions;

namespace p2pncs.Wiki.Engine
{
	class WikiHtmlRenderer
	{
		static WikiHtmlRenderer _instance = new WikiHtmlRenderer ();
		public static WikiHtmlRenderer Instance {
			get { return _instance; }
		}
		WikiHtmlRenderer () {}

		public string Render (WikiRootElement root, IWikiParser parser)
		{
			StringBuilder sb = new StringBuilder ();
			Render (root, sb, parser);
			return sb.ToString ();
		}

		void Render (WikiElement parent, StringBuilder sb, IWikiParser parser)
		{
			WikiNode prev = null;

			foreach (WikiNode cur in parent.Children) {
				try {
					if (cur is WikiTextNode) {
						sb.Append (ReplaceInline ((cur as WikiTextNode).Text, parser));
						continue;
					}

					WikiElement element = (WikiElement)cur;
					string element_name = null;
					if (element is WikiHeadingElement) {
						element_name = "h" + (element as WikiHeadingElement).Indent.ToString ();
					} else if (element is WikiParagraphElement) {
						element_name = "p";
					} else if (element is WikiListContainer) {
						switch ((element as WikiListContainer).ListType) {
							case WikiListType.Itemizing: element_name = "ul"; break;
							case WikiListType.Enumerating: element_name = "ol"; break;
							case WikiListType.Description: element_name = "dl"; break;
						}
					} else if (element is WikiListItemElement) {
						element_name = "li";
					}
					if (element_name == null)
						throw new Exception ();

					sb.Append ('<');
					sb.Append (element_name);
					sb.Append ('>');
					Render (element, sb, parser);
					sb.Append ("</");
					sb.Append (element_name);
					sb.Append ('>');
				} finally {
					prev = cur;
				}
			}
		}

		static Regex _escapeRegex = new Regex ("[&\"<>]", RegexOptions.Compiled);
		string ReplaceInline (string text, IWikiParser parser)
		{
			// Escape
			text = _escapeRegex.Replace (text, EscapeRegexMatchEvaluator);

			WikiInlineMarkupInfo[] markups = parser.InlineMarkups;
			for (int i = 0; i < markups.Length; i++) {
				Regex r = markups[i].Regex;
				switch (markups[i].Type) {
					case WikiInlineMarkupType.Bold:
						text = r.Replace (text, delegate (Match m) {
							return "<span class=\"bold\">" + m.Groups["text"].Value + "</span>";
						});
						break;
					case WikiInlineMarkupType.Italic:
						text = r.Replace (text, delegate (Match m) {
							return "<span class=\"italic\">" + m.Groups["text"].Value + "</span>";
						});
						break;
					case WikiInlineMarkupType.BoldItalic:
						text = r.Replace (text, delegate (Match m) {
							return "<span class=\"italic bold\">" + m.Groups["text"].Value + "</span>";
						});
						break;
					case WikiInlineMarkupType.WikiName:
						text = r.Replace (text, delegate (Match m) {
							return "<a href=\"" + WebApp.WikiTitleToUrl (m.Groups["name"].Value) + "\">" + m.Groups["name"].Value + "</a>";
						});
						break;
				}
			}
			return text;
		}

		static string EscapeRegexMatchEvaluator (Match match)
		{
			switch (match.Value) {
				case "&": return "&amp;";
				case "\"": return "&quot;";
				case "<": return "&lt;";
				case ">": return "&gt;";
			}
			return match.Value;
		}
	}
}
