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
using System.Net;
using System.IO;
using System.Runtime.Serialization;
using System.Threading;

namespace p2pncs.Net.Overlay
{
	public class SimpleIterativeRouter2 : IKeyBasedRouter
	{
		IKeyBasedRoutingAlgorithm _algo;
		Key _selfId;
		IMessagingSocket _sock;

		bool _active = true, _strict_mode;
		Random _rnd = new Random ();

		const int dgramMaxSize = 1000;
		int _maxNodeHandlesPerResponse;

		public event EventHandler<StatisticsNoticeEventArgs> StatisticsNotice;

		public SimpleIterativeRouter2 (Key self, IMessagingSocket sock, IKeyBasedRoutingAlgorithm algo, IFormatter formatter)
			: this (self, sock, algo, formatter, false)
		{
		}

		public SimpleIterativeRouter2 (Key self, IMessagingSocket sock, IKeyBasedRoutingAlgorithm algo, IFormatter formatter, bool isStrictMode)
		{
			_selfId = self;
			_sock = sock;
			_algo = algo;
			_strict_mode = isStrictMode;

			// メッセージに含むことの出来る大体の最大NodeHandle数を計算
			int overhead, nodeHandleBytes;
			{
				using (MemoryStream ms = new MemoryStream ()) {
					formatter.Serialize (ms, new NextHopResponse (self, true, new NodeHandle[0]));
					overhead = (int)ms.Length;
				}
				using (MemoryStream ms = new MemoryStream ()) {
					formatter.Serialize (ms, new NodeHandle (self, new IPEndPoint (IPAddress.Loopback, 0)));
					nodeHandleBytes = (int)ms.Length;
				}
			}
			_maxNodeHandlesPerResponse = (dgramMaxSize - overhead) / nodeHandleBytes;

			algo.Setup (self, this);
			sock.AddInquiredHandler (typeof (NextHopQuery), MessagingSocket_Inquired_NextHopQuery);
			sock.AddInquiredHandler (typeof (CloseNodeQuery), MessagingSocket_Inquired_CloseNodeQuery);
		}

		#region IKeyBasedRouter Members

		public void Join (EndPoint[] initialNodes)
		{
			_algo.Join (initialNodes);
		}

		public void Close ()
		{
			_algo.Close ();
			_sock.RemoveInquiredHandler (typeof (NextHopQuery), MessagingSocket_Inquired_NextHopQuery);
			_sock.RemoveInquiredHandler (typeof (CloseNodeQuery), MessagingSocket_Inquired_CloseNodeQuery);
		}

		public IAsyncResult BeginRoute (Key dest, EndPoint[] firstHops, int numOfCandidates, int numOfSimultaneous, AsyncCallback callback, object state)
		{
			RoutingInfo ri = new RoutingInfo (this, dest, numOfCandidates, numOfSimultaneous, _strict_mode, callback, state);
			ri.Start (firstHops);
			return ri;
		}

		public RoutingResult EndRoute (IAsyncResult ar)
		{
			ar.AsyncWaitHandle.WaitOne ();
			return (ar as RoutingInfo).Result;
		}

		public IKeyBasedRoutingAlgorithm RoutingAlgorithm {
			get { return _algo; }
		}

		public IMessagingSocket MessagingSocket {
			get { return _sock; }
		}

		public Key SelftNodeId {
			get { return _selfId; }
		}

		#endregion

		#region Misc
		void InvokeStatisticsNotice (StatisticsNoticeEventArgs e)
		{
			if (StatisticsNotice == null)
				return;
			try {
				StatisticsNotice (this, e);
			} catch {}
		}
		#endregion

		#region MessagingSocket Inquired Event Handlers
		void MessagingSocket_Inquired_NextHopQuery (object sender, InquiredEventArgs e)
		{
			if (!_active)
				return;

			NextHopQuery req = (NextHopQuery)e.InquireMessage;
			NodeHandle[] nextHops = _algo.GetNextHopNodes (req.Destination, Math.Max (_maxNodeHandlesPerResponse, req.NumberOfNextHops), req.Sender);
			bool isRoot = false;
			if (nextHops == null) {
				isRoot = true;
				nextHops = _algo.GetCloseNodes (req.Destination, req.NumberOfRootCandidates, req.Sender);
			}
			_sock.StartResponse (e, new NextHopResponse (_selfId, isRoot, nextHops));
			_algo.Touch (new NodeHandle (req.Sender, e.EndPoint));
		}

		void MessagingSocket_Inquired_CloseNodeQuery (object sender, InquiredEventArgs e)
		{
			if (!_active)
				return;

			CloseNodeQuery req = (CloseNodeQuery)e.InquireMessage;
			NodeHandle[] closeNodes = _algo.GetCloseNodes (req.Destination, req.NumberOfCloseNodes, req.Sender);
			_sock.StartResponse (e, new CloseNodeResponse (_selfId, closeNodes));
			_algo.Touch (new NodeHandle (req.Sender, e.EndPoint));
		}
		#endregion

		#region Internal Classes
		class RoutingInfo : IAsyncResult
		{
			Key _dest;
			SimpleIterativeRouter2 _router;
			int _numOfCandidates, _numOfSimultaneous;
			AsyncCallback _callback;
			object _state;
			bool _completed = false, _strictLookup;
			ManualResetEvent _done = new ManualResetEvent (false);
			DateTime _startTime, _timeout;
			NextHopQuery _query1;
			CloseNodeQuery _query2;

			object _fillLock = new object (); // 以下の変数はこのオブジェクトをロックしない限りアクセスしてはいけない
			int _inquiring = 0;
			HopSlot[] _slots;
			List<HopSlotEntry> _rootCandidates = new List<HopSlotEntry> ();
			RoutingResult _rr;
			Dictionary<Key, HopSlotEntry> _keyMap = new Dictionary<Key,HopSlotEntry> ();

			public RoutingInfo (SimpleIterativeRouter2 router, Key dest, int numOfCandidates, int numOfSimultaneous, bool strict_mode, AsyncCallback callback, object state)
			{
				_router = router;
				_dest = dest;
				_numOfCandidates = numOfCandidates;
				_numOfSimultaneous = numOfSimultaneous;
				_callback = callback;
				_state = state;
				_strictLookup = strict_mode;
				_slots = new HopSlot[router.RoutingAlgorithm.MaxRoutingLevel + 1];
				_startTime = DateTime.Now;
				_timeout = DateTime.Now + TimeSpan.FromSeconds (10);
				_query1 = new NextHopQuery (router.SelftNodeId, dest, numOfSimultaneous * 2, numOfCandidates);
				_query2 = new CloseNodeQuery (router.SelftNodeId, dest, numOfCandidates);
			}

			#region IAsyncResult Members

			public object AsyncState {
				get { return _state; }
			}

			public WaitHandle AsyncWaitHandle {
				get { return _done; }
			}

			public bool CompletedSynchronously {
				get { return false; }
			}

			public bool IsCompleted {
				get { return _completed; }
			}

			#endregion

			#region Start
			public void Start (EndPoint[] firstHops)
			{
				NodeHandle[] nodes;
				bool isRoot = false;
				if (firstHops == null || firstHops.Length == 0) {
					if (_router.SelftNodeId.Equals (_dest)) {
						nodes = _router.RoutingAlgorithm.GetRandomNodes (_numOfSimultaneous);
					} else {
						nodes = _router.RoutingAlgorithm.GetNextHopNodes (_dest, _numOfSimultaneous, null);
						if (nodes == null || nodes.Length == 0) {
							isRoot = true;
							List<NodeHandle> list = new List<NodeHandle> (_numOfCandidates + 1);
							list.Add (new NodeHandle (_router.SelftNodeId, null));
							list.AddRange (_router.RoutingAlgorithm.GetCloseNodes (_dest, _numOfCandidates, null));
							nodes = list.ToArray ();
						}
					}
				} else {
					nodes = new NodeHandle [firstHops.Length];
					for (int i = 0; i < firstHops.Length; i ++)
						nodes[i] = new NodeHandle (null, firstHops[i]);
				}
				lock (_fillLock) {
					FillSlot (null, nodes, isRoot, 0);
				}
			}
			#endregion

			#region NextHopQuery
			void FillSlot (HopSlotEntry sender, NodeHandle[] nextHops, bool isRootCandidates, int current_inquiring)
			{
				int nextHopIndex = (sender == null ? 1 : sender.Hop + 1);

				if (isRootCandidates) {
					if (_strictLookup) {
						FillRootCandidateSlot (sender, true, nextHops, current_inquiring);
						return;
					} else {
						lock (_rootCandidates) {
							_rootCandidates.Add (sender);
						}
						NodeHandle[] result;
						if (sender != null) {
							result = new NodeHandle[nextHops == null ? 1 : nextHops.Length + 1];
							result[0] = sender.NodeHandle;
							for (int i = 0; i < nextHops.Length; i ++)
								result[i + 1] = nextHops[i];
						} else {
							result = nextHops;
						}
						Success (result, nextHopIndex - 1);
						return;
					}
				}
				if (_rootCandidates.Count > 0) {
					if (_strictLookup) {
						// 厳密なルックアップの場合、すでにルート候補が選出されているので
						// ルート候補となったノードと同じスロットに入っているノードへの問い合わせのみを行う #1
					} else {
						// ルート候補の選出は終了している
						return;
					}
				}

				if (_timeout < DateTime.Now) {
					Fail (FailReason.Timeout, nextHopIndex - 1);
					return;
				}

				// Fill slots
				if (nextHops != null) {
					for (int i = 0; i < nextHops.Length; i++) {
						int level = (nextHops[i].NodeID == null ? 0 : _router.RoutingAlgorithm.ComputeRoutingLevel (_dest, nextHops[i].NodeID));
						if (_slots[level] == null)
							_slots[level] = new HopSlot (_numOfSimultaneous);
						_slots[level].AddEntry (nextHops[i], nextHopIndex, _keyMap);
					}
				}

				// Start NextHop Inquire
				int free_inq = Math.Max (0, _numOfSimultaneous - current_inquiring);
				int inquiried = 0, inquiry_failed = 0;
				for (int i = _slots.Length - 1; free_inq > 0 && i >= 0; i--) {
					if (_slots[i] == null) continue;
					for (int q = 0; free_inq > 0 && q < _slots[i].Count; q++) {
						if (_slots[i][q].IsInquiryStarted) {
							inquiried ++;
							if (_slots[i][q].IsFailed)
								inquiry_failed ++;
							continue;
						}
						_slots[i][q].StartInquiry ();
						_router.MessagingSocket.BeginInquire (_query1, _slots[i][q].NodeHandle.EndPoint, NextHopQuery_Callback, _slots[i][q]);
						current_inquiring = Interlocked.Increment (ref _inquiring);
						free_inq--;
					}

					// 問い合わせ中のものがあれば、同時問い合わせ数未満だったとしても
					// 最長一致スロットのみをチェックして、ループから抜ける
					if (inquiried != inquiry_failed)
						break;
					inquiried = int.MinValue;
				}

				if (current_inquiring == 0) {
					// 問い合わせ中のものがない場合、
					// 厳密なルックアップを行っていて、かつルート候補リストが1つ以上埋まっていれば
					// ルート候補選定に入り、そうでない場合は失敗とする
					if (_strictLookup && _rootCandidates.Count > 0) {
						FillRootCandidateSlot (sender, false, null, current_inquiring);
					} else {
						Fail (FailReason.NoRoot, nextHopIndex - 1);
					}
				}
			}

			void NextHopQuery_Callback (IAsyncResult ar)
			{
				HopSlotEntry entry = (HopSlotEntry)ar.AsyncState;
				object rawRes = _router.MessagingSocket.EndInquire (ar);

				NextHopResponse res = rawRes as NextHopResponse;
				if (res == null) {
					entry.Fail ();
					_router.RoutingAlgorithm.Fail (entry.NodeHandle);
					lock (_fillLock) {
						FillSlot (entry, null, false, Interlocked.Decrement (ref _inquiring));
					}
					return;
				}

				entry.Responsed (res.Sender, _keyMap);
				_router.RoutingAlgorithm.Touch (entry.NodeHandle);
				lock (_fillLock) {
					FillSlot (entry, res.NextHops, res.IsRootCandidate, Interlocked.Decrement (ref _inquiring));
				}
			}
			#endregion

			#region CloseNodeQuery
			void FillRootCandidateSlot (HopSlotEntry sender, bool senderIsRoot, NodeHandle[] closeNodes, int current_inquiring)
			{
				NodeHandle[] resultNodes = null;
				int resultHops = 0;

				if (_rootCandidates.Count == 0 && _dest.Equals (_router.SelftNodeId)) {
					// 自分自身が宛先と同じなので候補に追加する
					_rootCandidates.Add (new HopSlotEntry (new NodeHandle (_dest, null), 0));
				}
				if (senderIsRoot && sender != null) {
					// senderがルート候補なので追加する
					if (!_rootCandidates.Exists (delegate (HopSlotEntry entry) {
						return Key.Equals (sender.NodeHandle.NodeID, entry.NodeHandle.NodeID);
					})) {
						_rootCandidates.Add (sender);
					}
					if (closeNodes == null)
						closeNodes = new NodeHandle[0]; // ソートのためにダミーの空配列を代入
				}
				if (closeNodes != null) {
					// closeNodesを候補一覧に追加し、ソート
					int nextHopIndex = (sender == null ? 1 : sender.Hop + 1);
					for (int i = 0; i < closeNodes.Length; i ++) {
						if (_rootCandidates.Exists (delegate (HopSlotEntry entry) {
							return Key.Equals (closeNodes[i].NodeID, entry.NodeHandle.NodeID);
						})) continue;
						HopSlotEntry hopSlotEntry;
						if (!_keyMap.TryGetValue (closeNodes[i].NodeID, out hopSlotEntry))
							hopSlotEntry = new HopSlotEntry (closeNodes[i], nextHopIndex);
						_rootCandidates.Add (hopSlotEntry);
					}
					_rootCandidates.Sort (delegate (HopSlotEntry x, HopSlotEntry y) {
						Key diffX = _router.RoutingAlgorithm.ComputeDistance (_dest, x.NodeHandle.NodeID);
						Key diffY = _router.RoutingAlgorithm.ComputeDistance (_dest, y.NodeHandle.NodeID);
						return diffX.CompareTo (diffY);
					});
				}

				// ルート候補が確定しているか確認
				int decided_candidates = 0;
				for (int i = 0; i < _rootCandidates.Count; i ++) {
					if (_rootCandidates[i].IsFailed) continue;
					if (_rootCandidates[i].IsResponsed) {
						decided_candidates ++;
						if (decided_candidates == _numOfCandidates)
							break;
					} else {
						break;
					}
				}
				if (decided_candidates >= _numOfCandidates) {
					// 問い合わせ完了！
					resultNodes = new NodeHandle[decided_candidates];
					for (int i = 0, q = 0; i < _rootCandidates.Count; i ++) {
						if (_rootCandidates[i].IsResponsed) {
							resultNodes[q ++] = _rootCandidates[i].NodeHandle;
							resultHops = Math.Max (resultHops, _rootCandidates[i].Hop);
							if (q == decided_candidates)
								break;
						}
					}
					Success (resultNodes, resultHops);
					return;
				}

				if (_timeout <= DateTime.Now) {
					Fail (FailReason.Timeout, sender == null ? 0 : sender.Hop);
					return;
				}

				// Start CloseNode Inquire
				int free_inq = Math.Max (0, _numOfSimultaneous - current_inquiring);
				for (int i = 0; i < _rootCandidates.Count && free_inq > 0; i ++, free_inq --) {
					if (_rootCandidates[i].IsInquiryStarted) continue;
					_rootCandidates[i].StartInquiry ();
					_router.MessagingSocket.BeginInquire (_query2, _rootCandidates[i].NodeHandle.EndPoint, CloseNodeQuery_Callback, _rootCandidates[i]);
					current_inquiring = Interlocked.Increment (ref _inquiring);
				}

				if (current_inquiring == 0) {
					// 指定された数だけルート候補がそろわなかったが、
					// Failよりはましなので、Successとする
					List<NodeHandle> temp = new List<NodeHandle> ();
					for (int i = 0; i < _rootCandidates.Count; i++) {
						if (_rootCandidates[i].IsResponsed) {
							temp.Add (_rootCandidates[i].NodeHandle);
							resultHops = Math.Max (resultHops, _rootCandidates[i].Hop);
							if (temp.Count == _numOfCandidates)
								break;
						}
					}
					Success (temp.ToArray (), resultHops);
					return;
				}
			}

			void CloseNodeQuery_Callback (IAsyncResult ar)
			{
				HopSlotEntry entry = (HopSlotEntry)ar.AsyncState;
				object rawRes = _router.MessagingSocket.EndInquire (ar);

				CloseNodeResponse res = rawRes as CloseNodeResponse;
				if (res == null) {
					entry.Fail ();
					_router.RoutingAlgorithm.Fail (entry.NodeHandle);
					lock (_fillLock) {
						FillRootCandidateSlot (entry, false, null, Interlocked.Decrement (ref _inquiring));
					}
					return;
				}

				entry.Responsed (res.Sender, _keyMap);
				_router.RoutingAlgorithm.Touch (entry.NodeHandle);
				lock (_fillLock) {
					FillRootCandidateSlot (entry, false, res.CloseNodes, Interlocked.Decrement (ref _inquiring));
				}
			}
			#endregion

			#region Success / Fail / Result
			void Success (NodeHandle[] resultNodes, int hops)
			{
				Done (new RoutingResult (resultNodes, hops));
				_router.InvokeStatisticsNotice (StatisticsNoticeEventArgs.CreateSuccess ());
				_router.InvokeStatisticsNotice (StatisticsNoticeEventArgs.CreateRTT (DateTime.Now.Subtract (_startTime)));
				_router.InvokeStatisticsNotice (StatisticsNoticeEventArgs.CreateHops (hops));
			}

			void Fail (FailReason reason, int hops)
			{
				Done (new RoutingResult (reason, hops));
				_router.InvokeStatisticsNotice (StatisticsNoticeEventArgs.CreateFailure ());
			}

			void Done (RoutingResult result)
			{
				if (_rr != null)
					return;
				_rr = result;
				_completed = true;
				_done.Set ();
				if (_callback != null)
					_callback (this);
			}

			public RoutingResult Result {
				get { return _rr; }
			}
			#endregion

			#region Internal Classes
			class HopSlot
			{
				List<HopSlotEntry> _entries;
				int _minHop = int.MaxValue;

				public HopSlot (int numOfSim)
				{
					_entries = new List<HopSlotEntry> (numOfSim);
				}

				public HopSlotEntry this [int index] {
					get { return _entries[index]; }
				}

				public void AddEntry (NodeHandle node, int hop, Dictionary<Key, HopSlotEntry> map)
				{
					if (node.NodeID != null && map.ContainsKey (node.NodeID))
						return;

					HopSlotEntry entry = new HopSlotEntry (node, hop);
					_minHop = Math.Min (_minHop, hop);
					_entries.Add (entry);
					if (node.NodeID != null)
						map.Add (node.NodeID, entry);
				}

				public int MinHop {
					get { return _minHop; }
				}

				public int Count {
					get { return _entries.Count; }
				}
			}

			class HopSlotEntry
			{
				NodeHandle _node;
				DateTime _start;
				TimeSpan _time = TimeSpan.MaxValue;
				int _hop;
				bool _isInquiryStarted = false, _fail = false;

				public HopSlotEntry (NodeHandle node, int hop)
				{
					_node = node;
					_start = DateTime.Now;
					_hop = hop;
					if (node.EndPoint == null) {
						// self
						_isInquiryStarted = true;
						_fail = false;
						_hop = 0;
						_time = TimeSpan.Zero;
					}
				}

				public NodeHandle NodeHandle {
					get { return _node; }
				}

				public TimeSpan Time {
					get { return _time; }
				}

				public bool IsResponsed {
					get { return !TimeSpan.MaxValue.Equals (_time); }
				}

				public bool IsInquiryStarted {
					get { return _isInquiryStarted; }
				}

				public bool IsFailed {
					get { return _fail; }
				}

				public int Hop {
					get { return _hop; }
				}

				public void StartInquiry ()
				{
					_isInquiryStarted = true;
				}
				
				public void Responsed (Key key, Dictionary<Key, HopSlotEntry> map)
				{
					_time = DateTime.Now.Subtract (_start);
					if (_node.NodeID == null) {
						_node = new NodeHandle (key, _node.EndPoint);
						map.Add (key, this);
					}
				}

				public void Fail ()
				{
					_fail = true;
				}
			}
			#endregion
		}

		[SerializableTypeId (0x112)]
		class NextHopQuery
		{
			[SerializableFieldId (0)]
			Key _sender;

			[SerializableFieldId (1)]
			Key _dest;

			[SerializableFieldId (2)]
			int _numOfNextHops;

			[SerializableFieldId (3)]
			int _numOfRootCandidates;

			public NextHopQuery (Key sender, Key dest, int numOfNextHops, int numOfRootCandidates)
			{
				_sender = sender;
				_dest = dest;
				_numOfNextHops = numOfNextHops;
				_numOfRootCandidates = numOfRootCandidates;
			}

			public Key Sender {
				get { return _sender; }
			}

			public Key Destination {
				get { return _dest; }
			}

			public int NumberOfNextHops {
				get { return _numOfNextHops; }
			}

			public int NumberOfRootCandidates {
				get { return _numOfRootCandidates; }
			}
		}

		[SerializableTypeId (0x113)]
		class NextHopResponse
		{
			[SerializableFieldId (0)]
			Key _sender;

			[SerializableFieldId (1)]
			bool _isRootCandidate;

			[SerializableFieldId (2)]
			NodeHandle[] _nextHops;

			public NextHopResponse (Key sender, bool isRootCandidate, NodeHandle[] nextHops)
			{
				_sender = sender;
				_isRootCandidate = isRootCandidate;
				_nextHops = nextHops;
			}

			public Key Sender {
				get { return _sender; }
			}

			public bool IsRootCandidate {
				get { return _isRootCandidate; }
			}

			public NodeHandle[] NextHops {
				get { return _nextHops; }
			}
		}

		[SerializableTypeId (0x114)]
		class CloseNodeQuery
		{
			[SerializableFieldId (0)]
			Key _sender;

			[SerializableFieldId (1)]
			Key _dest;

			[SerializableFieldId (2)]
			int _numOfCloseNodes;

			public CloseNodeQuery (Key sender, Key dest, int numOfCloseNodes)
			{
				_sender = sender;
				_dest = dest;
				_numOfCloseNodes = numOfCloseNodes;
			}

			public Key Sender {
				get { return _sender; }
			}

			public Key Destination {
				get { return _dest; }
			}

			public int NumberOfCloseNodes {
				get { return _numOfCloseNodes; }
			}
		}

		[SerializableTypeId (0x115)]
		class CloseNodeResponse
		{
			[SerializableFieldId (0)]
			Key _sender;

			[SerializableFieldId (1)]
			NodeHandle[] _closeNodes;

			public CloseNodeResponse (Key sender, NodeHandle[] closeNodes)
			{
				_sender = sender;
				_closeNodes = closeNodes;
			}

			public Key Sender {
				get { return _sender; }
			}

			public NodeHandle[] CloseNodes {
				get { return _closeNodes; }
			}
		}
		#endregion
	}
}
