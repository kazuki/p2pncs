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
using System.Net;

namespace p2pncs.Net.Overlay.Anonymous
{
	[SerializableTypeId (0x410)]
	public sealed class MCREndPoint : EndPoint, IEquatable<MCREndPoint>
	{
		[SerializableFieldId (0)]
		EndPoint _ep;

		[SerializableFieldId (1)]
		uint _label;

		public MCREndPoint (EndPoint ep, uint label)
		{
			_ep = ep;
			_label = label;
		}

		public EndPoint EndPoint {
			get { return _ep; }
		}

		public uint Label {
			get { return _label; }
		}

		#region IEquatable<MCREndPoint> Members

		public bool Equals (MCREndPoint other)
		{
			return _ep.Equals (other._ep) && _label == other._label;
		}

		#endregion

		#region Override
		public override int GetHashCode ()
		{
			return _ep.GetHashCode () ^ (int)_label;
		}

		public override bool Equals (object obj)
		{
			return Equals ((MCREndPoint)obj);
		}

		public override string ToString ()
		{
			return string.Format ("{0}@{1:x8}", _ep, _label);
		}
		#endregion
	}
}
