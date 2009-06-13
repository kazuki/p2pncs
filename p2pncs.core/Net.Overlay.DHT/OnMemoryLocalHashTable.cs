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
using p2pncs.Utility;

namespace p2pncs.Net.Overlay.DHT
{
	public class OnMemoryLocalHashTable : ILocalHashTable, IMassKeyDelivererLocalStore
	{
		IntervalInterrupter _int;
		ReaderWriterLockWrapper _lock = new ReaderWriterLockWrapper ();
		Dictionary<PKey, List<Entry>> _dic = new Dictionary<PKey, List<Entry>> ();
		Key _self;
		IKeyBasedRoutingAlgorithm _algo;

		public OnMemoryLocalHashTable (IKeyBasedRouter router, IntervalInterrupter interrupter)
		{
			_self = router.SelftNodeId;
			_algo = router.RoutingAlgorithm;
			_int = interrupter;
			_int.AddInterruption (CheckExpiry);
		}

		void CheckExpiry ()
		{
			List<PKey> removeKeys = null;
			using (_lock.EnterReadLock ()) {
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
				using (_lock.EnterWriteLock ()) {
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
			PKey pk = new PKey (key, typeId, _algo.ComputeRoutingLevel (_self, key));
			Entry entry = new Entry (pk, expires, value);
			while (true) {
				using (_lock.EnterReadLock ()) {
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
				using (_lock.EnterWriteLock ()) {
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
			PKey pk = new PKey (key, typeId, -1);
			using (_lock.EnterReadLock ()) {
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
			PKey pk = new PKey (key, typeId, -1);
			bool removeKey = false;
			
			if (value != null) {
				using (_lock.EnterReadLock ()) {
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
				using (_lock.EnterWriteLock ()) {
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
			using (_lock.EnterWriteLock ()) {
				_dic.Clear ();
			}
		}

		#endregion

		#region IMassKeyDelivererLocalStore Members

		/// TODO: optimization
		public void GetEachRoutingLevelValues (IList<DHTEntry>[] values)
		{
			const int MAX_LIST_SIZE = 10;
			List<Entry>[] unsentList = new List<Entry>[values.Length];
			List<Entry>[] sentList = new List<Entry>[values.Length];
			for (int i = 0; i < sentList.Length; i ++) {
				unsentList[i] = new List<Entry> ();
				sentList[i] = new List<Entry> ();
			}

			using (_lock.EnterReadLock ()) {
				foreach (KeyValuePair<PKey,List<Entry>> pair in _dic) {
					List<Entry> list = pair.Value;
					lock (list) {
						for (int i = 0; i < list.Count; i++) {
							if (list[i].Expiry <= DateTime.Now) {
								list.RemoveAt (i --);
								continue;
							}
							if (list[i].SendFlag) {
								sentList[pair.Key.RoutingLevel].Add (list[i]);
							} else {
								unsentList[pair.Key.RoutingLevel].Add (list[i]);
							}
						}
					}
				}
			}

			for (int i = 0; i < values.Length; i ++) {
				if (unsentList[i].Count > 0) {
					Entry[] entries = unsentList[i].ToArray().RandomSelection(MAX_LIST_SIZE);
					for (int k = 0; k < entries.Length; k ++) {
						values[i].Add (entries[k].ToDHTEntry ());
						entries[k].SendFlag = true;
					}
				}

				/*int size = MAX_LIST_SIZE - values[i].Count;
				if (size > 0 && sentList[i].Count > 0) {
					Entry[] entries = sentList[i].ToArray ().RandomSelection (size);
					for (int k = 0; k < entries.Length; k++)
						values[i].Add (entries[k].ToDHTEntry ());
				}*/
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
		class PKey : IEquatable<PKey>
		{
			Key _key;
			int _typeId;
			int _level;

			public PKey (Key key, int typeId, int level)
			{
				_key = key;
				_typeId = typeId;
				_level = level;
			}

			public Key Key {
				get { return _key; }
			}

			public int TypeID {
				get { return _typeId; }
			}

			public int RoutingLevel {
				get { return _level; }
			}

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
			PKey _key;
			DateTime _expiry;
			object _value;
			bool _send = false;

			public Entry (PKey key, DateTime expiry, object value)
			{
				_key = key;
				_expiry = expiry;
				_value = value;
			}

			public Key Key {
				get { return _key.Key; }
			}

			public int TypeId {
				get { return _key.TypeID; }
			}

			public DateTime Expiry {
				get { return _expiry; }
			}

			public object Value {
				get { return _value; }
			}

			public bool SendFlag {
				get { return _send; }
				set { _send = value; }
			}

			public DHTEntry ToDHTEntry ()
			{
				return new DHTEntry (_key.Key, _key.TypeID, _value, _expiry);
			}

			public void Extend (DateTime newExpiry)
			{
				if (_expiry < newExpiry) {
					_expiry = newExpiry;
					_send = false;
				}
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
