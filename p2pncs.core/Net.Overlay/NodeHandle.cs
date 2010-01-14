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
using System.Net;

namespace p2pncs.Net.Overlay
{
	[Serializable]
	[SerializableTypeId (0x201)]
	public class NodeHandle : IEquatable<NodeHandle>
	{
		[SerializableFieldId (0)]
		EndPoint _ep;

		[SerializableFieldId (1)]
		Key _id;

		public NodeHandle (Key id, EndPoint ep)
		{
			_id = id;
			_ep = ep;
		}

		public EndPoint EndPoint {
			get { return _ep; }
		}

		public Key NodeID {
			get { return _id; }
		}

		public override string ToString ()
		{
			return (_id == null ? "null" : _id.ToString ()) + (_ep == null ? "@null" : "@" + _ep.ToString ());
		}

		public override int GetHashCode ()
		{
			return (_id == null ? 0 : _id.GetHashCode ()) ^ (_ep == null ? 0 : _ep.GetHashCode ());
		}

		public override bool Equals (object obj)
		{
			if (!(obj is NodeHandle))
				return false;
			return Equals ((NodeHandle)obj);
		}

		public bool Equals (NodeHandle other)
		{
			if (_ep == null) {
				if (other._ep != null)
					return false;
			} else {
				if (!_ep.Equals (other._ep))
					return false;
			}
			if (_id == null) {
				if (other._id != null)
					return false;
				return true;
			} else {
				return _id.Equals (other._id);
			}
		}
	}
}
