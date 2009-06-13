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

namespace p2pncs.Net.Overlay.DHT
{
	[SerializableTypeId (0x306)]
	public struct DHTEntry
	{
		[SerializableFieldId (0)]
		Key _key;

		[SerializableFieldId (1)]
		int _typeId;

		[SerializableFieldId (2)]
		object _value;

		[SerializableFieldId (3)]
		DateTime _expiry;

		public DHTEntry (Key key, int typeId, object value, DateTime expiry)
		{
			_key = key;
			_typeId = typeId;
			_value = value;
			_expiry = expiry;
		}

		public Key Key {
			get { return _key; }
		}

		public int TypeId {
			get { return _typeId; }
		}

		public object Value {
			get { return _value; }
		}

		public DateTime ExpirationDate {
			get { return _expiry; }
		}
	}
}
