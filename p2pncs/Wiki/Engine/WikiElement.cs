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

namespace p2pncs.Wiki.Engine
{
	class WikiElement : WikiNode
	{
		protected List<WikiNode> _children = new List<WikiNode> ();

		/// <summary>
		/// 引数に指定されたノードを子供に含められるかどうか判定します
		/// </summary>
		public virtual bool CanContain (WikiNode node)
		{
			return true;
		}

		public virtual WikiElement Add (WikiNode node)
		{
			if (CanContain (node)) {
				return AppendChild (node);
			} else {
				return _parent.Add (node);
			}
		}

		protected virtual WikiElement AppendChild (WikiNode node)
		{
			node.SetParent (this);
			_children.Add (node);
			if (node as WikiElement == null)
				return this;
			return node as WikiElement;
		}

		public IList<WikiNode> Children {
			get { return _children.AsReadOnly (); }
		}
	}
}
