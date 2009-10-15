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

#define ENABLE_REDUCTION
using System;
using System.Collections.Generic;
using System.Net;
using p2pncs.Threading;

namespace p2pncs.Net.Overlay
{
	public class SimpleRoutingAlgorithm : IKeyBasedRoutingAlgorithm
	{
		Dictionary<Key, RoutingTable> _multiAppRoutingTable = new Dictionary<Key, RoutingTable> ();
		ReaderWriterLockWrapper _multiAppTableLock = new ReaderWriterLockWrapper ();

		Dictionary<Key, RoutingEntry> _allEntries = new Dictionary<Key,RoutingEntry> ();
		Dictionary<EndPoint, RoutingEntry> _reverseMap = new Dictionary<EndPoint,RoutingEntry> ();
		object _routingTableLock = new object ();

		Key _self, _mask;
		NodeHandle _selfMinNodeHandle;
		MultiAppNodeHandle _selfNodeHandle;

		IMessagingSocket _msock;
		IKeyBasedRouter _router;

		int _bucketSize;
		TimeSpan _minimumPingInterval;

		public SimpleRoutingAlgorithm (Key self, IMessagingSocket msock, int bucketSize, TimeSpan minPingInterval)
		{
			_self = self;
			_msock = msock;
			_selfMinNodeHandle = new NodeHandle (self, null);
			_selfNodeHandle = new MultiAppNodeHandle (self, null, null, DateTime.MinValue, null);
			_mask = ~new Key (new byte[self.KeyBytes]);
			_bucketSize = bucketSize;
			_minimumPingInterval = minPingInterval;

			_msock.AddInquiredHandler (typeof (PingRequest), ReceivedPingRequest);
		}

		#region IKeyBasedRoutingAlgorithm Members

		public void Setup (IKeyBasedRouter router)
		{
			_router = router;
		}

		public void NewApp (Key appId)
		{
			using (_multiAppTableLock.EnterWriteLock ()) {
				if (_multiAppRoutingTable.ContainsKey (appId))
					return;
				_multiAppRoutingTable.Add (appId, new RoutingTable (_selfMinNodeHandle, _bucketSize));

				Key[] apps = new List<Key> (_multiAppRoutingTable.Keys).ToArray ();
				_selfNodeHandle = new MultiAppNodeHandle (_self, null, apps, DateTime.Now, _selfNodeHandle.Options);
			}
		}

		public void Join (Key appId, EndPoint[] initialNodes)
		{
			Stabilize (appId, initialNodes);
		}

		public void Close (Key appId)
		{
			throw new NotImplementedException ();
		}

		public void Close ()
		{
			if (_multiAppRoutingTable.Count == 0)
				return;
			using (_multiAppTableLock.EnterWriteLock ()) {
				_multiAppRoutingTable.Clear ();
				_allEntries.Clear ();
				_reverseMap.Clear ();
			}
			_msock.RemoveInquiredHandler (typeof (PingRequest), ReceivedPingRequest);
			_multiAppTableLock.Dispose ();
		}

		public void SetEndPointOption (object opt)
		{
			if (opt == null)
				throw new ArgumentNullException ();

			object[] options = _selfNodeHandle.Options;
			for (int i = 0; i < options.Length; i++) {
				if (options[i].GetType ().Equals (opt.GetType ())) {
					options[i] = opt;
					opt = null;
				}
			}
			if (opt != null) {
				Array.Resize<object> (ref options, options.Length + 1);
				options[options.Length - 1] = opt;
			}

			_selfNodeHandle = new MultiAppNodeHandle (_self, null, _selfNodeHandle.AppIDs,
				_selfNodeHandle.AppIDsLastModified, options);
		}

		public void RemoveEndPointOption (Type optType)
		{
			List<object> list = new List<object> (_selfNodeHandle.Options);
			for (int i = 0; i < list.Count; i++) {
				if (optType.Equals (list[i].GetType ())) {
					list.RemoveAt (i);
					_selfNodeHandle = new MultiAppNodeHandle (_self, null, _selfNodeHandle.AppIDs,
						_selfNodeHandle.AppIDsLastModified, list.ToArray ());
					return;
				}
			}
		}

		public Key ComputeDistance (Key x, Key y)
		{
			return x ^ y;
		}

		public NodeHandle[] GetCloseNodes (Key appId, Key target, int maxNum)
		{
			RoutingTable table;
			using (_multiAppTableLock.EnterReadLock ()) {
				if (!_multiAppRoutingTable.TryGetValue (appId, out table))
					throw new UnknownApplicationIdException ();
				lock (_routingTableLock) {
					return table.GetCloseNodes (target, maxNum);
				}
			}
		}

		public void Touch (MultiAppNodeHandle node)
		{
			if (node.AppIDs == null || node.EndPoint == null || node.NodeID == null)
				throw new ArgumentException ();

			int level = Key.MatchBitsFromMSB (_self, node.NodeID);
			if (level == _self.KeyBits)
				return; // 自分自身

			RoutingEntry entry;
			//List<object[]> pingRequests = new List<object[]> ();
			using (_multiAppTableLock.EnterReadLock ()) {
				lock (_routingTableLock) {
					// 既にルーティングテーブルに存在するノードの
					// NodeID, EndPoint, AppIDsが変更されたかチェックし、
					// 変更されている場合は、既存のエントリを削除する
					{
						RoutingEntry entry2;
						_allEntries.TryGetValue (node.NodeID, out entry);
						_reverseMap.TryGetValue (node.EndPoint, out entry2);

						bool epChanged = entry != null && !node.EndPoint.Equals (entry.NodeHandle.EndPoint);
						bool idChanged = !epChanged && entry2 != null && !node.NodeID.Equals (entry2.NodeHandle.NodeID);
						bool appChanged = !epChanged && !idChanged && entry != null && !node.AppIDsLastModified.Equals (entry.NodeHandle.AppIDsLastModified);
						if (epChanged || idChanged || appChanged || entry != entry2) {
							if (entry != null) RemoveEntry (entry);
							if (entry2 != null && entry != entry2) RemoveEntry (entry2);
							entry = entry2 = null;
						}
					}

					// ルーティングテーブルにエントリが存在しない場合は新規作成
					bool newEntry = false;
					if (entry == null) {
						entry = new RoutingEntry (node, _multiAppRoutingTable);
						newEntry = true;
					} else {
						entry.Touch ();
					}

					// 各アプリケーションに対応するルーティングテーブルに対して追加要請
					bool addedFlag = false;
					for (int i = 0; i < entry.NodeHandle.AppIDs.Length; i ++) {
						RoutingTable table;
						if (!_multiAppRoutingTable.TryGetValue (entry.NodeHandle.AppIDs[i], out table))
							continue;

						RoutingEntry firstEntry;
						if (table.TryAddToBucket (level, entry, out firstEntry)) {
							addedFlag = true;
						} else if (firstEntry.NeedPingCheck (_minimumPingInterval)) {
							addedFlag = true;
							//pingRequests.Add (new object[] {bucket, firstEntry});
						}
					}
					if (newEntry && addedFlag) {
						_allEntries.Add (node.NodeID, entry);
						_reverseMap.Add (node.EndPoint, entry);
					}
				}
			}

			// ノードが生きているかチェック。死んでいたら置き換える
			/*foreach (object[] pingReq in pingRequests) {
				_msock.BeginInquire (new PingRequest (_selfNodeHandle), (pingReq[1] as RoutingEntry).NodeHandle.EndPoint, delegate (IAsyncResult ar) {
					object[] state = (object[])ar.AsyncState;
					RoutingBucket prBucket = state[0] as RoutingBucket;
					RoutingEntry prEntry = state[1] as RoutingEntry;
					IIterativeMessage res = _msock.EndInquire (ar) as IIterativeMessage;
					if (res != null) {
						Touch (res.NodeHandle);
						using (_multiAppTableLock.EnterReadLock ()) {
							lock (_routingTableLock) {
								RemoveEntry (entry);
							}
						}
						return;
					}
					using (_multiAppTableLock.EnterReadLock ()) {
						lock (_routingTableLock) {
							RemoveEntry (prEntry);
							RoutingEntry dummy;
							if (!prBucket.TryAdd (entry, out dummy))
								RemoveEntry (entry);
						}
					}
				}, pingReq);
			}*/
		}

		public void Touch (EndPoint ep)
		{
			RoutingEntry entry;
			lock (_routingTableLock) {
				if (!_reverseMap.TryGetValue (ep, out entry))
					return;
			}
			Touch (entry.NodeHandle);
		}

		public void Fail (EndPoint ep)
		{
			lock (_routingTableLock) {
				RoutingEntry entry;
				if (!_reverseMap.TryGetValue (ep, out entry))
					return;
				RemoveEntry (entry);
			}
		}

		#region Stabilize
		public void Stabilize (Key appId)
		{
			Stabilize (appId, null);
		}

		void Stabilize (Key appId, EndPoint[] initNodes)
		{
			StabilizeInfo info = new StabilizeInfo (appId, initNodes);
			if (initNodes == null) {
				NodeHandle[] nodeHandles = GetRandomNodes (appId, 3);
				initNodes = new EndPoint[nodeHandles.Length];
				for (int i = 0; i < initNodes.Length; i++)
					initNodes[i] = nodeHandles[i].EndPoint;
			}
			_router.BeginRoute (appId, _self, 3, new KeyBasedRoutingOptions {FirstHops = initNodes}, Stabilize_Callback, info);
		}

		void Stabilize_Callback (IAsyncResult ar)
		{
			StabilizeInfo info = (StabilizeInfo)ar.AsyncState;
			RoutingResult result = _router.EndRoute (ar);
			if (info.CurrentLevel >= 0 && result.RootCandidates.Length > 0) {
				int matchBits = Key.MatchBitsFromMSB (_self, result.RootCandidates[0].NodeID);
				if (info.CurrentLevel < matchBits)
					info.CurrentLevel = matchBits;
			}

			RoutingTable table;
			using (_multiAppTableLock.EnterReadLock ()) {
				if (!_multiAppRoutingTable.TryGetValue (info.AppId, out table))
					return;
			}

			lock (_routingTableLock) {
				int filledMaxLevel = table.GetFilledBucketMaxLevel ();
				while (++info.CurrentLevel < filledMaxLevel) {
					if (table.GetNumberOfEntries (info.CurrentLevel) > 0) {
						continue;
					}
					Key target = _self ^ (_mask >> info.CurrentLevel);
					_router.BeginRoute (info.AppId, target, 3, new KeyBasedRoutingOptions {FirstHops = info.InitNodes, RoutingFinishedMatchBits = info.CurrentLevel},
					Stabilize_Callback, info);
					return;
				}
			}

			ReduceRoutingTableSize ();
		}

		class StabilizeInfo
		{
			Key _appId;
			EndPoint[] _initNodes;

			public StabilizeInfo (Key appId, EndPoint[] initNodes)
			{
				_appId = appId;
				_initNodes = initNodes;
				CurrentLevel = -1;
			}

			public Key AppId {
				get { return _appId; }
			}

			public EndPoint[] InitNodes {
				get { return _initNodes; }
			}

			public int CurrentLevel { get; set; }
		}
		#endregion

		public int GetRoutingTableSize ()
		{
			lock (_routingTableLock) {
				return _allEntries.Count;
			}
		}

		public int GetRoutingTableSize (Key appId)
		{
			using (_multiAppTableLock.EnterReadLock ()) {
				return _multiAppRoutingTable[appId].GetNumberOfEntries ();
			}
		}

		public MultiAppNodeHandle SelfNodeHandle {
			get { return _selfNodeHandle; }
		}
		#endregion

		#region Misc
		public void ReduceRoutingTableSize ()
		{
#if ENABLE_REDUCTION
			using (_multiAppTableLock.EnterReadLock ()) {
				lock (_routingTableLock) {
					ReduceRoutingTableSizeWithoutLock ();
				}
			}
#endif
		}
		void ReduceRoutingTableSizeWithoutLock ()
		{
			List<RoutingEntry>[] table = new List<RoutingEntry>[_self.KeyBits];
			foreach (RoutingEntry entry in _allEntries.Values) {
				int level = Key.MatchBitsFromMSB (_self, entry.NodeHandle.NodeID);
				if (table[level] == null)
					table[level] = new List<RoutingEntry> ();
				table[level].Add (entry);
			}

			Dictionary<Key,int> appMap = new Dictionary<Key,int> ();
			List<RoutingEntry>[] tmpList = new List<RoutingEntry>[_multiAppRoutingTable.Count];
			{
				int tmpIdx = 0;
				foreach (Key appId in _multiAppRoutingTable.Keys) {
					tmpList[tmpIdx] = new List<RoutingEntry> ();
					appMap.Add (appId, tmpIdx++);
				}
			}
			for (int level = 0; level < table.Length; level++) {
				for (int i = 0; i < tmpList.Length; i ++)
					tmpList[i].Clear ();
				List<RoutingEntry> bucket = table[level];
				if (bucket == null) continue;
				bucket.Sort (delegate (RoutingEntry x, RoutingEntry y) {
					if (x.CoveredApps != y.CoveredApps)
						return y.CoveredApps - x.CoveredApps;
					return y.LastResponsed.CompareTo (x.LastResponsed);
				});
				for (int i = 0; i < bucket.Count; i ++) {
					MultiAppNodeHandle nh = bucket[i].NodeHandle;
					bool removeFlag = true;
					for (int k = 0; k < nh.AppIDs.Length; k ++) {
						int idx;
						if (!appMap.TryGetValue (nh.AppIDs[k], out idx))
							continue;
						if (tmpList[idx].Count == _bucketSize)
							continue;
						removeFlag = false;
						tmpList[idx].Add (bucket[i]);
					}
					if (removeFlag)
						RemoveEntryWithoutMultiAppRoutingTable (bucket[i]);
				}
				foreach (KeyValuePair<Key,RoutingTable> kv in _multiAppRoutingTable) {
					List<RoutingEntry> newBucket = tmpList[appMap[kv.Key]];
					newBucket.Reverse ();
					kv.Value.ReplaceBucket (level, newBucket);
				}
			}
		}

		void RemoveEntryWithoutMultiAppRoutingTable (RoutingEntry entry)
		{
			_allEntries.Remove (entry.NodeHandle.NodeID);
			_reverseMap.Remove (entry.NodeHandle.EndPoint);
		}

		void RemoveEntry (RoutingEntry entry)
		{
			RemoveEntryWithoutMultiAppRoutingTable (entry);
			int level = Key.MatchBitsFromMSB (_self, entry.NodeHandle.NodeID);
			for (int i = 0; i < entry.NodeHandle.AppIDs.Length; i++) {
				RoutingTable table;
				if (!_multiAppRoutingTable.TryGetValue (entry.NodeHandle.AppIDs[i], out table))
					continue;
				table.RemoveEntry (entry, level);
			}
		}

		public NodeHandle[] GetRandomNodes (Key appId, int maxNum)
		{
			List<NodeHandle> list = new List<NodeHandle> ();
			using (_multiAppTableLock.EnterReadLock ()) {
				RoutingTable table;
				if (!_multiAppRoutingTable.TryGetValue (appId, out table))
					throw new UnknownApplicationIdException ();
				lock (_routingTableLock) {
					table.CopyTo (list);
				}
			}

			if (list.Count <= maxNum)
				return list.ToArray ();

			Random rnd = new Random ();
			NodeHandle[] array = new NodeHandle[maxNum];
			for (int i = 0; i < array.Length; i ++) {
				int idx = rnd.Next (0, list.Count);
				array[i] = list[idx];
				list.RemoveAt (idx);
			}
			return array;
		}
		#endregion

		#region Message Handler
		void ReceivedPingRequest (object sender, InquiredEventArgs e)
		{
			_msock.StartResponse (e, new PingResponse (_selfNodeHandle));
			MultiAppNodeHandle nodeHandle = (e.InquireMessage as IIterativeMessage).NodeHandle;
			nodeHandle.EndPoint = e.EndPoint;
			Touch (nodeHandle);
		}
		#endregion

		#region RoutingTable
		sealed class RoutingTable
		{
			NodeHandle _self;
			int _bucketSize;
			RoutingBucket[] _buckets;

			public RoutingTable (NodeHandle self, int bucketSize)
			{
				_self = self;
				_bucketSize = bucketSize;
				_buckets = new RoutingBucket[self.NodeID.KeyBits];
			}

			public bool TryAddToBucket (int level, RoutingEntry entry, out RoutingEntry firstEntry)
			{
				return GetBucket (level).TryAdd (entry, out firstEntry);
			}

			public void ReplaceBucket (int level, IEnumerable<RoutingEntry> newBucket)
			{
				GetBucket (level).Replace (newBucket);
			}

			public void CopyTo (IList<NodeHandle> list)
			{
				for (int i = 0; i < _buckets.Length; i ++) {
					if (_buckets[i] == null)
						continue;
					_buckets[i].CopyTo (list);
				}
			}

			public void CopyBucketTo (int level, IList<NodeHandle> list)
			{
				if (_buckets[level] == null)
					return;
				_buckets[level].CopyTo (list);
			}

			RoutingBucket GetBucket (int level)
			{
				if (_buckets[level] != null)
					return _buckets[level];

				if (_buckets[level] != null)
					return _buckets[level];
				_buckets[level] = new RoutingBucket (_bucketSize);
				return _buckets[level];
			}

			public int GetFilledBucketMaxLevel ()
			{
				for (int i = _buckets.Length - 1; i >= 0; i --)
					if (_buckets[i] != null && _buckets[i].HasEntry ())
						return i;
				return 0;
			}

			public NodeHandle[] GetCloseNodes (Key target, int maxNum)
			{
				List<NodeHandle> list = new List<NodeHandle> ();
				int level = Key.MatchBitsFromMSB (_self.NodeID, target);
				for (int i = level; list.Count < maxNum && i < _buckets.Length; i ++) {
					if (_buckets[i] != null)
						_buckets[i].CopyTo (list);
				}
				for (int i = level - 1; list.Count < maxNum && i >= 0; i--) {
					if (_buckets[i] != null)
						_buckets[i].CopyTo (list);
				}
				list.Sort (delegate (NodeHandle x, NodeHandle y) {
					return (target ^ x.NodeID).CompareTo (target ^ y.NodeID);
				});
				NodeHandle[] result = new NodeHandle[Math.Min (maxNum, list.Count)];
				for (int i = 0; i < result.Length; i ++)
					result[i] = list[i];
				return result;
			}

			public void RemoveEntry (RoutingEntry entry, int level)
			{
				if (_buckets[level] == null)
					return;
				_buckets[level].RemoveEntry (entry);
			}

			public int GetNumberOfEntries ()
			{
				int numOfEntries = 0;
				for (int i = 0; i < _buckets.Length; i ++) {
					if (_buckets[i] == null)
						continue;
					numOfEntries += _buckets[i].NumberOfEntries;
				}
				return numOfEntries;
			}

			public int GetNumberOfEntries (int level)
			{
				if (_buckets[level] == null)
					return 0;
				return _buckets[level].NumberOfEntries;
			}

			sealed class RoutingBucket : IEnumerable<RoutingEntry>
			{
				List<RoutingEntry> _entries;

				public RoutingBucket (int bucketSize)
				{
					_entries = new List<RoutingEntry> (bucketSize);
				}

				public bool TryAdd (RoutingEntry entry, out RoutingEntry firstEntry)
				{
					firstEntry = null;
					for (int i = 0; i < _entries.Count; i++) {
						if (entry == _entries[i]) {
							_entries.RemoveAt (i);
							_entries.Add (entry);
							return true;
						}
					}
					if (_entries.Count < _entries.Capacity) {
						_entries.Add (entry);
						return true;
					}
#if ENABLE_REDUCTION
					for (int i = 0; i < _entries.Count; i++) {
						if (_entries[i].CoveredApps < entry.CoveredApps) {
							_entries.RemoveAt (i);
							_entries.Add (entry);
							return true;
						}
					}
#endif
					firstEntry = _entries[0];
					return false;
				}

				public void CopyTo (IList<NodeHandle> list)
				{
					for (int i = 0; i < _entries.Count; i++)
						list.Add (_entries[i].MinNodeHandle);
				}

				public void CopyTo (IList<MultiAppNodeHandle> list)
				{
					for (int i = 0; i < _entries.Count; i++)
						list.Add (_entries[i].NodeHandle);
				}

				public void RemoveEntry (RoutingEntry entry)
				{
					_entries.Remove (entry);
				}

				public bool HasEntry ()
				{
					return _entries.Count > 0;
				}

				public void Replace (IEnumerable<RoutingEntry> newBucket)
				{
					_entries.Clear ();
					_entries.AddRange (newBucket);
				}

				public int NumberOfEntries {
					get { return _entries.Count; }
				}

				#region IEnumerable<RoutingEntry> Members

				public IEnumerator<RoutingEntry> GetEnumerator ()
				{
					return _entries.GetEnumerator ();
				}

				System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator ()
				{
					return GetEnumerator ();
				}

				#endregion
			}
		}

		sealed class RoutingEntry
		{
			MultiAppNodeHandle _nodeHandle;
			NodeHandle _minNodeHandle;
			DateTime _lastResponsed;
			int _appCover;

			public RoutingEntry (MultiAppNodeHandle nodeHandle, Dictionary<Key, RoutingTable> table)
			{
				_nodeHandle = nodeHandle;
				_minNodeHandle = new NodeHandle (nodeHandle.NodeID, nodeHandle.EndPoint);
				_lastResponsed = DateTime.Now;

				_appCover = 0;
				for (int i = 0; i < nodeHandle.AppIDs.Length; i ++)
					if (table.ContainsKey (nodeHandle.AppIDs[i]))
						_appCover++;
			}

			public bool NeedPingCheck (TimeSpan minPingInterval)
			{
				if (_lastResponsed < DateTime.Now.Subtract (minPingInterval)) {
					_lastResponsed = DateTime.Now;
					return true;
				}
				return false;
			}

			public void Touch ()
			{
				_lastResponsed = DateTime.Now;
			}

			public MultiAppNodeHandle NodeHandle {
				get { return _nodeHandle; }
			}

			public NodeHandle MinNodeHandle {
				get { return _minNodeHandle; }
			}

			public DateTime LastResponsed {
				get { return _lastResponsed; }
			}

			public int CoveredApps {
				get { return _appCover; }
			}
		}
		#endregion

		#region Messages
		[SerializableTypeId (0x1f1)]
		sealed class PingRequest : IIterativeMessage
		{
			[SerializableFieldId (0)]
			MultiAppNodeHandle _nodeHandle;

			public PingRequest (MultiAppNodeHandle nodeHandle)
			{
				_nodeHandle = nodeHandle;
			}

			public MultiAppNodeHandle NodeHandle {
				get { return _nodeHandle; }
			}
		}

		[SerializableTypeId (0x1f2)]
		sealed class PingResponse : IIterativeMessage
		{
			[SerializableFieldId (0)]
			MultiAppNodeHandle _nodeHandle;

			public PingResponse (MultiAppNodeHandle nodeHandle)
			{
				_nodeHandle = nodeHandle;
			}

			public MultiAppNodeHandle NodeHandle {
				get { return _nodeHandle; }
			}
		}
		#endregion
	}

	interface IIterativeMessage
	{
		MultiAppNodeHandle NodeHandle { get; }
	}

	public sealed class UnknownApplicationIdException : ApplicationException {}
}
