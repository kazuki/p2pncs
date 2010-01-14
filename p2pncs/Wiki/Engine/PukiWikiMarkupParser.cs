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

using System;
using System.IO;

namespace p2pncs.Wiki.Engine
{
	class PukiWikiMarkupParser : IWikiParser
	{
		static PukiWikiMarkupParser _instance = new PukiWikiMarkupParser ();
		public static PukiWikiMarkupParser Instance {
			get { return _instance; }
		}
		PukiWikiMarkupParser () {}

		#region Block
		public WikiRootElement Parse (string text)
		{
			WikiRootElement root = new WikiRootElement ();
			WikiElement cur = root;

			using (StringReader reader = new StringReader (text)) {
				string line;
				while ((line = reader.ReadLine ()) != null) {
					if (line.Length >= 2 && line[0] == '/' && line[1] == '/')
						continue;
					line = line.TrimEnd ();

					if (line.Length == 0) {
						cur = root;
						continue;
					}

					WikiElement element = null;
					int indent = -1;
					switch (line[0]) {
						case '*':
							indent = Math.Min (6, WikiTextUtility.CountSameChars (line, '*'));
							root.Add (element = new WikiHeadingElement (indent, false));
							cur = root;
							break;
						case '+':
						case '-':
							WikiListContainer list_container = cur as WikiListContainer;
							WikiListType type = line[0] == '-' ? WikiListType.Itemizing : WikiListType.Enumerating;
							indent = Math.Min (3, WikiTextUtility.CountSameChars (line, line[0]));
							if (list_container == null) {
								list_container = new WikiListContainer (type, 1, false);
								cur = cur.Add (list_container);
							}
							for (int i = list_container.Indent + 1; i < indent; i ++) {
								list_container = new WikiListContainer (type, i, false);
								cur = cur.Add (list_container);
							}
							if (list_container.Indent < indent || (list_container.Indent == indent && list_container.ListType != type)) {
								list_container = new WikiListContainer (type, indent, false);
								cur = cur.Add (list_container);
							}
							element = new WikiListItemElement (indent, false);
							cur = list_container.Add (element);
							break;
					}
					if (element != null) {
						if (indent > 0)
							line = line.Substring (indent).Trim ();
						if (line.Length > 0)
							element.Add (new WikiTextNode (line));
					} else {
						cur = cur.Add (new WikiTextNode (line.Trim ()));
					}
				}
			}

			return root;
		}
		#endregion

		#region Inline
		static WikiInlineMarkupInfo[] _inlineMarkups = new WikiInlineMarkupInfo[] {
			new WikiInlineMarkupInfo ("'{3}(?<text>[^'].+?)'{3}", WikiInlineMarkupType.Italic),
			new WikiInlineMarkupInfo ("'{2}(?<text>[^'].+?)'{2}", WikiInlineMarkupType.Bold),
			new WikiInlineMarkupInfo (@"(?=\[\[[^\[])\[\[(?<name>[^\]]+?)(?![^\]]\]\])\]\]", WikiInlineMarkupType.WikiName)
		};

		public WikiInlineMarkupInfo[] InlineMarkups {
			get { return _inlineMarkups; }
		}
		#endregion
	}
}
