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
using p2pncs.Threading;
using TypedKey = p2pncs.Net.Overlay.DHT.ValueTypeRegister.TypedKey;

namespace p2pncs.Net.Overlay.DHT
{
	public class OnMemoryStore : ILocalHashTable
	{
		ValueTypeRegister _typeRegister;
		IntervalInterrupter _int;
		Dictionary<TypedKey, object> _dic = new Dictionary<TypedKey, object> ();

		public OnMemoryStore (ValueTypeRegister typeRegister, IntervalInterrupter expiryCheckInt)
		{
			_typeRegister = typeRegister;
			_int = expiryCheckInt;
			_int.AddInterruption (CheckExpiry);
		}

		void CheckExpiry ()
		{
			List<KeyValuePair<TypedKey, object>> list;
			List<TypedKey> removeList = new List<TypedKey> ();
			lock (_dic) {
				list = new List<KeyValuePair<TypedKey, object>> (_dic);
			}
			for (int i = 0; i < list.Count; i ++) {
				ValueTypeInfo vi = _typeRegister[list[i].Key.TypeID];
				lock (list[i].Value) {
					vi.Merger.CheckExpiration (list[i].Value);
					if (vi.Merger.GetCount (list[i].Value) == 0)
						removeList.Add (list[i].Key);
				}
			}
			lock (_dic) {
				for (int i = 0; i < removeList.Count; i ++)
					_dic.Remove (removeList[i]);
			}
		}

		#region ILocalHashTable Members

		public void Put (Key key, TimeSpan lifetime, object value)
		{
			if (value == null || key == null)
				throw new ArgumentNullException ();
			ValueTypeInfo vi = _typeRegister[value.GetType ()];
			TypedKey typedKey = new TypedKey (key, vi.ID);
			object obj;
			lock (_dic) {
				if (!_dic.TryGetValue (typedKey, out obj)) {
					_dic.Add (typedKey, vi.Merger.Merge (null, value, lifetime));
					return;
				}
			}
			lock (obj) {
				vi.Merger.Merge (obj, value, lifetime);
			}
		}

		public object[] Get (Key key, Type type, int maxCount)
		{
			if (key == null)
				throw new ArgumentNullException ();
			ValueTypeInfo vi = _typeRegister[type];
			TypedKey typedKey = new TypedKey (key, vi.ID);
			object obj;
			lock (_dic) {
				if (!_dic.TryGetValue (typedKey, out obj))
					return null;
			}
			lock (obj) {
				return vi.Merger.GetEntries (obj, maxCount);
			}
		}

		public void Close ()
		{
			lock (this) {
				if (_dic == null)
					return;
				_dic.Clear ();
				_dic = null;
			}
			_int.RemoveInterruption (CheckExpiry);
		}

		#endregion
	}
}
