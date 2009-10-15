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

namespace p2pncs.Net.Overlay.DHT
{
	public class OnMemoryStore : ILocalHashTable
	{
		IntervalInterrupter _int;
		ReaderWriterLockWrapper _lock = new ReaderWriterLockWrapper ();
		Dictionary<Key2, ValueList> _dic = new Dictionary<Key2, ValueList> ();
		const int MaxValueListSize = 32;
		static readonly object[] EmptyResult = new object[0];

		public OnMemoryStore (IntervalInterrupter expiryCheckInt)
		{
			_int = expiryCheckInt;
			_int.AddInterruption (CheckExpiry);
		}

		void CheckExpiry ()
		{
			List<Key2> removeKeys = new List<Key2> ();
			using (_lock.EnterReadLock ()) {
				foreach (KeyValuePair<Key2, ValueList> pair in _dic) {
					pair.Value.CheckExpiration ();
					if (pair.Value.Count == 0)
						removeKeys.Add (pair.Key);
				}
			}
			if (removeKeys.Count > 0) {
				using (_lock.EnterWriteLock ()) {
					foreach (Key2 key in removeKeys) {
						ValueList value;
						if (!_dic.TryGetValue (key, out value))
							continue;
						if (value.Count == 0)
							_dic.Remove (key);
					}
				}
			}
		}

		#region ILocalHashTable Members

		public void Put (Key key, int typeId, TimeSpan lifetime, object value)
		{
			Key2 key2 = new Key2 (key, typeId);
			ValueList list;
			using (_lock.EnterReadLock ()) {
				_dic.TryGetValue (key2, out list);
			}
			if (list == null) {
				using (_lock.EnterWriteLock ()) {
					if (!_dic.TryGetValue (key2, out list)) {
						list = new ValueList (MaxValueListSize);
						_dic.Add (key2, list);
					}
				}
			}
			list.Add (lifetime, value);
		}

		public object[] Get (Key key, int typeId, int maxCount)
		{
			Key2 key2 = new Key2 (key, typeId);
			ValueList list;
			using (_lock.EnterReadLock ()) {
				_dic.TryGetValue (key2, out list);
			}
			if (list == null)
				return EmptyResult;
			return list.GetValues (maxCount);
		}

		public void Close ()
		{
			lock (_lock) {
				if (_dic == null)
					return;
				_int.RemoveInterruption (CheckExpiry);
				_dic.Clear ();
				_dic = null;
				_lock.Dispose ();
			}
		}

		#endregion

		#region Internal Class
		class Key2 : IEquatable<Key2>
		{
			Key _key;
			int _typeId;

			public Key2 (Key key, int typeId)
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

			#region IEquatable<Key2> Members

			public bool Equals (Key2 other)
			{
				return _key.Equals (other._key) && _typeId == other._typeId;
			}

			#endregion

			#region Overrides
			public override bool Equals (object obj)
			{
				Key2 pkey = obj as Key2;
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
		class ValueList
		{
			List<ValueEntry> _list;
			int _maxListSize;

			public ValueList (int maxListSize)
			{
				_list = new List<ValueEntry> ();
				_maxListSize = maxListSize;
			}

			public void Add (TimeSpan lifeTime, object value)
			{
				lock (_list) {
					for (int i = 0; i < _list.Count; i ++) {
						if (value.Equals (_list[i].Value)) {
							_list[i].LifeTime = lifeTime;
							_list[i].Expiration = DateTime.Now + lifeTime;
							return;
						}
					}
					if (_list.Count >= _maxListSize) {
						CheckExpiration ();
						if (_list.Count >= _maxListSize)
							return;
					}
					_list.Add (new ValueEntry (value, lifeTime, DateTime.Now + lifeTime));
				}
			}

			public void CheckExpiration ()
			{
				lock (_list) {
					for (int i = 0; i < _list.Count; i ++) {
						if (_list[i].Expiration <= DateTime.Now) {
							_list.RemoveAt (i);
							i --;
						}
					}
				}
			}

			public object[] GetValues (int maxSize)
			{
				lock (_list) {
					object[] result = new object[Math.Min (_list.Count, maxSize)];
					for (int i = 0; i < result.Length; i ++)
						result[i] = _list[i].Value;
					return result;
				}
			}

			public int Count {
				get {
					lock (_list) {
						return _list.Count;
					}
				}
			}
		}
		class ValueEntry
		{
			public ValueEntry (object value, TimeSpan lifeTime, DateTime expiration)
			{
				Value = value;
				LifeTime = lifeTime;
				Expiration = expiration;
			}

			public object Value { get; set; }
			public TimeSpan LifeTime { get; set; }
			public DateTime Expiration { get; set; }
		}
		#endregion
	}
}
