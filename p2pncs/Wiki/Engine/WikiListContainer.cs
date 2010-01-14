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
	class WikiListContainer : WikiIndentedElement
	{
		WikiListType _listType;

		public WikiListContainer (WikiListType listType, int indent, bool isRelIndent)
			: base (indent, isRelIndent)
		{
			_listType = listType;
		}

		public WikiListType ListType {
			get { return _listType; }
		}

		public override WikiElement Add (WikiNode node)
		{
			if (node is WikiListItemElement || node is WikiListContainer) {
				WikiIndentedElement other = node as WikiIndentedElement;
				if (other.Indent < this.Indent)
					return _parent.Add (node);
				if (node is WikiListItemElement) {
					if (other.Indent == this.Indent)
						return this.AppendChild (node);
					throw new Exception ();
				}
				if (node is WikiListContainer && other.Indent == this.Indent)
					return _parent.Add (node);
				if (other.Indent != this.Indent + 1)
					throw new Exception ();
			}

			if (_children.Count == 0)
				AppendChild (new WikiListItemElement (_indent, _isRel));
			return (_children[_children.Count - 1] as WikiListItemElement).Add (node);
		}

		protected override WikiElement AppendChild (WikiNode node)
		{
			if (!(node is WikiListItemElement))
				throw new Exception ();

			node.SetParent (this);
			_children.Add (node);
			return this;
		}
	}
}
