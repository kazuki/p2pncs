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

namespace p2pncs.Wiki.Engine
{
	class WikiListItemElement : WikiIndentedElement
	{
		public WikiListItemElement (int indent, bool isRelIndent)
			: base (indent, isRelIndent)
		{
		}

		public override WikiElement Add (WikiNode node)
		{
			if (node is WikiTextNode)
				return base.Add (node);
			if (node is WikiListItemElement || (node is WikiListContainer && (node as WikiListContainer).Indent <= this.Indent))
				return _parent.Add (node);
			if (node is WikiListContainer)
				return base.Add (node);
			throw new Exception ();
		}
	}
}
