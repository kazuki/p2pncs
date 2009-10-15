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

namespace p2pncs.Net.Overlay
{
	[SerializableTypeId (0x1f0)]
	public class MultiAppNodeHandle
	{
		[SerializableFieldId (0)]
		EndPoint _ep;

		[SerializableFieldId (1)]
		Key _id;

		[SerializableFieldId (2)]
		Key[] _appIds;

		[SerializableFieldId (3)]
		object[] _options;

		public MultiAppNodeHandle (Key id, EndPoint ep, Key[] appIds, object[] options)
		{
			_id = id;
			_ep = ep;
			_appIds = appIds;
			_options = options;
		}

		public MultiAppNodeHandle CloneWithNewEndPoint (EndPoint newEndPoint)
		{
			return new MultiAppNodeHandle (_id, newEndPoint, _appIds, _options);
		}

		public static bool IsAppIdChanged (MultiAppNodeHandle x, MultiAppNodeHandle y)
		{
			if (x.AppIDs == y.AppIDs)
				return false; // for simulator
			if (x.AppIDs.Length != y.AppIDs.Length)
				return true;
			for (int i = 0; i < x.AppIDs.Length; i ++)
				if (!x.AppIDs[i].Equals (y.AppIDs[i]))
					return true;
			return false;
		}

		public EndPoint EndPoint {
			get { return _ep; }
		}

		public Key NodeID {
			get { return _id; }
		}

		public Key[] AppIDs {
			get { return _appIds; }
		}

		public object[] Options {
			get { return _options; }
		}

		public object GetOption (Type type)
		{
			if (_options == null)
				return null;
			for (int i = 0; i < _options.Length; i ++)
				if (type.Equals (_options[i].GetType ()))
					return _options[i];
			return null;
		}

		public override string ToString ()
		{
			return (_id == null ? "null" : _id.ToString ()) + (_ep == null ? "@null" : "@" + _ep.ToString ());
		}
	}
}
