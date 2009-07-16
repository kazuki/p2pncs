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

namespace p2pncs.Wiki.Engine
{
	class WikiRootElement : WikiElement
	{
		public override WikiElement Add (WikiNode node)
		{
			if (node is WikiTextNode) {
				WikiParagraphElement p = new WikiParagraphElement ();
				p.Add (node);
				return base.Add (p);
			}
			return base.Add (node);
		}
	}
}
