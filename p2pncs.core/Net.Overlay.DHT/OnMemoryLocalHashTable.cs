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
	public class OnMemoryLocalHashTable : ILocalHashTable
	{
		IntervalInterrupter _int;
		ReaderWriterLockWrapper _lock = new ReaderWriterLockWrapper ();
		Dictionary<PKey, List<Entry>> _dic = new Dictionary<PKey, List<Entry>> ();

		public OnMemoryLocalHashTable (IntervalInterrupter interrupter)
		{
			_int = interrupter;
			_int.AddInterruption (CheckExpiry);
		}

		void CheckExpiry ()
		{
			List<PKey> removeKeys = null;
			using (IDisposable cookie = _lock.EnterReadLock ()) {
				foreach (KeyValuePair<PKey, List<Entry>> pair in _dic) {
					List<Entry> list = pair.Value;
					lock (list) {
						for (int i = 0; i < list.Count; i ++) {
							if (list[i].Expiry < DateTime.Now) {
								list.RemoveAt (i);
								i --;
							}
						}
						if (list.Count == 0) {
							if (removeKeys == null)
								removeKeys = new List<PKey> ();
							removeKeys.Add (pair.Key);
						}
					}
				}
			}
			if (removeKeys != null) {
				using (IDisposable cookie = _lock.EnterWriteLock ()) {
					foreach (PKey key in removeKeys) {
						List<Entry> list;
						if (!_dic.TryGetValue (key, out list))
							continue;
						if (list.Count == 0)
							_dic.Remove (key);
					}
				}
			}
		}

		#region ILocalHashTable Members

		public void Put (Key key, int typeId, DateTime expires, object value)
		{
			List<Entry> list;
			PKey pk = new PKey (key, typeId);
			Entry entry = new Entry (expires, value);
			while (true) {
				using (IDisposable cookie = _lock.EnterReadLock ()) {
					if (_dic.TryGetValue (pk, out list)) {
						lock (list) {
							for (int i = 0; i < list.Count; i ++) {
								if (entry.Equals (list[i])) {
									list[i].Extend (entry.Expiry);
									return;
								}
							}
							list.Add (entry);
						}
						return;
					}
				}
				using (IDisposable cookie = _lock.EnterWriteLock ()) {
					if (!_dic.TryGetValue (pk, out list)) {
						list = new List<Entry> ();
						list.Add (entry);
						_dic.Add (pk, list);
						return;
					}
				}
			}
		}

		public object[] Get (Key key, int typeId)
		{
			List<Entry> list;
			PKey pk = new PKey (key, typeId);
			using (IDisposable cookie = _lock.EnterReadLock ()) {
				if (!_dic.TryGetValue (pk, out list))
					return null;
			}

			List<Entry> temp = new List<Entry> (list.Count);
			lock (list) {
				for (int i = 0; i < list.Count; i ++) {
					if (list[i].Expiry < DateTime.Now) {
						list.RemoveAt (i);
						i --;
						continue;
					}
					temp.Add (list[i]);
				}
			}

			if (temp.Count == 0)
				return new object[0];

			if (temp.Count == 1)
				return new object[] {temp[0].Value};

			temp.Sort (delegate (Entry x, Entry y) {
				return x.Expiry.CompareTo (y.Expiry);
			});

			object[] objects = new object[temp.Count];
			for (int i = 0; i < temp.Count; i ++)
				objects[i] = temp[i].Value;
			return objects;
		}

		public void Remove (Key key, int typeId, object value)
		{
			List<Entry> list;
			PKey pk = new PKey (key, typeId);
			bool removeKey = false;
			
			if (value != null) {
				using (IDisposable cookie = _lock.EnterReadLock ()) {
					if (!_dic.TryGetValue (pk, out list))
						return;
				}
				lock (list) {
					for (int i = 0; i < list.Count; i ++) {
						if (value.Equals (list[i].Value)) {
							list.RemoveAt (i);
							break;
						}
					}
					removeKey = true;
				}
			}

			if (value == null || removeKey) {
				using (IDisposable cookie = _lock.EnterWriteLock ()) {
					if (value != null) {
						if (!_dic.TryGetValue (pk, out list))
							return;
						if (list.Count != 0)
							return;
					}
					_dic.Remove (pk);
				}
			}
		}

		public void Clear ()
		{
			using (IDisposable cookie = _lock.EnterWriteLock ()) {
				_dic.Clear ();
			}
		}

		#endregion

		#region IDisposable Members

		public void Dispose ()
		{
			_dic.Clear ();
			_int.RemoveInterruption (CheckExpiry);
			_lock.Dispose ();
		}

		#endregion

		#region Internal Class
		class PKey : IComparable, IComparable<PKey>, IEquatable<PKey>
		{
			Key _key;
			int _typeId;

			public PKey (Key key, int typeId)
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

			#region IComparable Members

			public int CompareTo (object obj)
			{
				return CompareTo ((PKey)obj);
			}

			#endregion

			#region IComparable<KeyAndTypeID> Members

			public int CompareTo (PKey other)
			{
				int ret = Math.Sign (_key.CompareTo (other)) * 2;
				if (ret == 0)
					ret = Math.Sign (_typeId.CompareTo (other._typeId));
				return ret;
			}

			#endregion

			#region IEquatable<KeyAndTypeID> Members

			public bool Equals (PKey other)
			{
				return _key.Equals (other._key) && _typeId == other._typeId;
			}

			#endregion

			#region Overrides
			public override bool Equals (object obj)
			{
				PKey pkey = obj as PKey;
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
		class Entry : IEquatable<Entry>
		{
			DateTime _expiry;
			object _value;

			public Entry (DateTime expiry, object value)
			{
				_expiry = expiry;
				_value = value;
			}

			public DateTime Expiry {
				get { return _expiry; }
			}

			public object Value {
				get { return _value; }
			}

			public void Extend (DateTime newExpiry)
			{
				if (_expiry < newExpiry)
					_expiry = newExpiry;
			}

			public bool Equals (Entry other)
			{
				return _value.Equals (other._value);
			}

			public override bool Equals (object obj)
			{
				Entry entry = obj as Entry;
				if (entry == null)
					return false;
				return Equals (entry);
			}

			public override int GetHashCode ()
			{
				return _value.GetHashCode ();
			}
		}
		#endregion
	}
}
