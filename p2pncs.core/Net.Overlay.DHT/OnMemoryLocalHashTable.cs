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
		Dictionary<PKey, object> _dic = new Dictionary<PKey, object> ();
		HashSet<PKey>[] _dicEachRoutingLevel;
		Key _self;
		IKeyBasedRoutingAlgorithm _algo;

		public OnMemoryLocalHashTable (IKeyBasedRouter router, IntervalInterrupter interrupter)
		{
			_self = router.SelftNodeId;
			_algo = router.RoutingAlgorithm;
			_int = interrupter;
			_int.AddInterruption (CheckExpiry);
			_dicEachRoutingLevel = new HashSet<PKey> [_algo.MaxRoutingLevel];
			for (int i = 0; i < _dicEachRoutingLevel.Length; i ++)
				_dicEachRoutingLevel[i] = new HashSet<PKey> ();
		}

		void CheckExpiry ()
		{
			List<PKey> removeKeys = new List<PKey> ();
			using (_lock.EnterReadLock ()) {
				foreach (KeyValuePair<PKey, object> pair in _dic) {
					lock (pair.Value) {
						pair.Key.Merger.ExpirationCheck (pair.Value);
						if (pair.Key.Merger.GetCount (pair.Value) == 0)
							removeKeys.Add (pair.Key);
					}
				}
			}
			if (removeKeys.Count > 0) {
				using (_lock.EnterWriteLock ()) {
					foreach (PKey key in removeKeys) {
						object value;
						if (!_dic.TryGetValue (key, out value))
							continue;
						if (key.Merger.GetCount (value) == 0) {
							_dic.Remove (key);
							_dicEachRoutingLevel[key.RoutingLevel].Remove (key);
						}
					}
				}
			}
		}

		#region ILocalHashTable Members

		public void Put (Key key, int typeId, TimeSpan lifeTime, object value, ILocalHashTableValueMerger merger)
		{
			PKey pk = new PKey (key, typeId, _algo.ComputeRoutingLevel (_self, key), merger);
			object cur_value;
			using (_lock.EnterReadLock ()) {
				if (!_dic.TryGetValue (pk, out cur_value))
					cur_value = null;
			}
			if (cur_value == null) {
				using (_lock.EnterWriteLock ()) {
					if (!_dic.TryGetValue (pk, out cur_value)) {
						cur_value = merger.Merge (null, value, lifeTime);
						_dic.Add (pk, cur_value);
						_dicEachRoutingLevel[pk.RoutingLevel].Add (pk);
						return;
					}
				}
			}

			lock (cur_value) {
				merger.Merge (cur_value, value, lifeTime);
			}
		}

		public object[] Get (Key key, int typeId, ILocalHashTableValueMerger merger)
		{
			object value;
			PKey pk = new PKey (key, typeId, -1, merger);
			using (_lock.EnterReadLock ()) {
				if (!_dic.TryGetValue (pk, out value))
					return null;
			}

			lock (value) {
				return merger.GetEntries (value, 5);
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
			const int MAX_LIST_SIZE = 5;
			for (int i = 0; i < values.Length; i ++) {
				using (_lock.EnterReadLock ()) {
					if (_dicEachRoutingLevel[i].Count == 0)
						continue;
					List<KeyValuePair<PKey, DHTEntry>> list = new List<KeyValuePair<PKey, DHTEntry>> ();
					int num = MAX_LIST_SIZE / _dicEachRoutingLevel[i].Count;
					if (num == 0) num = 1;
					while (list.Count < MAX_LIST_SIZE) {
						int tmp = list.Count;
						foreach (PKey pk in _dicEachRoutingLevel[i]) {
							if (pk.MKDGetter == null) continue;
							object value = _dic[pk];
							DHTEntry[] entries;
							lock (value) {
								entries = pk.MKDGetter.GetSendEntries (pk.Key, pk.TypeID, value, num);
							}
							for (int k = 0; k < entries.Length; k ++)
								list.Add (new KeyValuePair<PKey, DHTEntry> (pk, entries[k]));
						}
						if (list.Count == tmp)
							break;
						num = 1;
					}
					if (list.Count > MAX_LIST_SIZE) {
						Random rnd = new Random ();
						while (list.Count > MAX_LIST_SIZE) {
							int idx = rnd.Next (list.Count);
							object value = _dic[list[idx].Key];
							lock (value) {
								list[idx].Key.MKDGetter.UnmarkSendFlag (value, list[idx].Value.Value);
							}
							list.RemoveAt (idx);
						}
					}
					HashSet<object> set = new HashSet<object> ();
					for (int k = 0; k < list.Count; k ++) {
						values[i].Add (list[k].Value);
						set.Add (list[k].Value.Value);
					}
				}
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
			ILocalHashTableValueMerger _merger;

			public PKey (Key key, int typeId, int level, ILocalHashTableValueMerger merger)
			{
				_key = key;
				_typeId = typeId;
				_level = level;
				_merger = merger;
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

			public ILocalHashTableValueMerger Merger {
				get { return _merger; }
			}

			public IMassKeyDelivererValueGetter MKDGetter {
				get { return _merger as IMassKeyDelivererValueGetter; }
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
		#endregion
	}
}
