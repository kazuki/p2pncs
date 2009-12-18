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
using System.Collections.Generic;

namespace p2pncs.Net.Overlay.DHT
{
	public class ValueTypeRegister
	{
		Dictionary<Type, ValueTypeInfo> _mapping1 = new Dictionary<Type,ValueTypeInfo> ();
		Dictionary<int, ValueTypeInfo> _mapping2 = new Dictionary<int, ValueTypeInfo> ();

		public void Register (Type type, int id, IValueMerger merger)
		{
			ValueTypeInfo info = new ValueTypeInfo (type, id, merger);
			lock (_mapping1) {
				_mapping1.Add (type, info);
				_mapping2.Add (id, info);
			}
		}

		public void Unregister (Type type)
		{
			lock (_mapping1) {
				ValueTypeInfo info;
				if (!_mapping1.TryGetValue (type, out info))
					return;
				_mapping1.Remove (info.Type);
				_mapping2.Remove (info.ID);
			}
		}

		public ValueTypeInfo this [int id] {
			get {
				lock (_mapping1) {
					return _mapping2[id];
				}
			}
		}

		public ValueTypeInfo this [Type type] {
			get {
				lock (_mapping1) {
					return _mapping1[type];
				}
			}
		}

		public class TypedKey : IEquatable<TypedKey>
		{
			Key _key;
			int _typeId;

			public TypedKey (Key key, int typeId)
			{
				_key = key;
				_typeId = typeId;
			}

			public Key Key {
				get { return _key; }
			}

			public int TypeID {
				get { return _typeId; }
			}

			#region IEquatable<TypedKey> Members

			public bool Equals (TypedKey other)
			{
				return _key.Equals (other._key) && _typeId == other._typeId;
			}

			#endregion

			#region Overrides
			public override bool Equals (object obj)
			{
				TypedKey pkey = obj as TypedKey;
				if (pkey == null)
					return false;
				return Equals (pkey);
			}

			public override int GetHashCode ()
			{
				return _key.GetHashCode () ^ _typeId.GetHashCode ();
			}

			public override string ToString ()
			{
				return _key.ToString () + ":" + _typeId.ToString ();
			}
			#endregion
		}
	}
}
