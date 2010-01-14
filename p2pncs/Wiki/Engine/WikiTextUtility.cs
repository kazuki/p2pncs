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

namespace p2pncs.Wiki.Engine
{
	static class WikiTextUtility
	{
		public static int CountSameChars (string text, char c)
		{
			return CountSameChars (text, 0, c);
		}

		public static int CountSameChars (string text, int offset, char c)
		{
			for (int i = offset; i < text.Length; i ++)
				if (text[i] != c)
					return i - offset;
			return text.Length - offset;
		}

		public static int CountSameChars (string text, int offset, params char[] c)
		{
			for (int i = offset; i < text.Length; i++) {
				bool found = false;
				for (int k = 0; k < c.Length; k ++) {
					if (text[i] == c[k]) {
						found = true;
						break;
					}
				}
				if (!found)
					return i - offset;
			}
			return text.Length - offset;
		}
	}
}
