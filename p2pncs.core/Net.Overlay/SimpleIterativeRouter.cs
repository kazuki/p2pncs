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
	public class SimpleIterativeRouter : IKeyBasedRouter
	{
		IKeyBasedRoutingAlgorithm _algo;
		Key _selfId;
		IMessagingSocket _sock;

		bool _active = true;
		Random _rnd = new Random ();

		const int dgramMaxSize = 1000;
		int _maxNodeHandlesPerResponse;

		public SimpleIterativeRouter (Key self, IMessagingSocket sock, IKeyBasedRoutingAlgorithm algo, IFormatter formatter)
		{
			_selfId = self;
			_sock = sock;
			_algo = algo;

			// 各メッセージに含むことの出来るNodeHandle数を計算
			int overhead, nodeHandleBytes;
			{
				using (MemoryStream ms = new MemoryStream ()) {
					formatter.Serialize (ms, new NextHopQueryResponse (self, true, new NodeHandle[0], new NodeHandle[0]));
					overhead = (int)ms.Length;
				}
				using (MemoryStream ms = new MemoryStream ()) {
					formatter.Serialize (ms, new NodeHandle (self, new IPEndPoint (IPAddress.Loopback, 0)));
					nodeHandleBytes = (int)ms.Length;
				}
			}
			_maxNodeHandlesPerResponse = (dgramMaxSize - overhead) / nodeHandleBytes;

			algo.Setup (self, this);
			sock.AddInquiredHandler (typeof (NextHopQueryMessage), MessagingSocket_Inquired_NextHopQueryMessage);
		}

		#region MessagingSocket Inquired Handler
		void MessagingSocket_Inquired_NextHopQueryMessage (object sender, InquiredEventArgs e)
		{
			if (!_active)
				return;
			NextHopQueryMessage req = (NextHopQueryMessage)e.InquireMessage;
			NodeHandle[] nextHops = _algo.GetNextHopNodes (req.Destination, Math.Max (_maxNodeHandlesPerResponse / 3 * 2, req.NumberOfNextHops), req.Sender);
			bool isRoot = false;
			if (nextHops == null) {
				isRoot = true;
				nextHops = _algo.GetNeighbors (req.NumberOfRootCandidates - 1);
			}
			int numOfRands = _maxNodeHandlesPerResponse - nextHops.Length;
			NodeHandle[] rndNodes;
			if (numOfRands <= 0) {
				numOfRands = 0;
				rndNodes = null;
			} else {
				rndNodes = _algo.GetRandomNodes (numOfRands);
			}
			_sock.StartResponse (e, new NextHopQueryResponse (_selfId, isRoot, nextHops, rndNodes));
			_algo.Touch (new NodeHandle (req.Sender, e.EndPoint));
		}
		#endregion

		#region IKeyBasedRouter Members

		public void Join (EndPoint[] initialNodes)
		{
			_algo.Join (initialNodes);
		}

		public void Close ()
		{
			_sock.RemoveInquiredHandler (typeof (NextHopQueryMessage), MessagingSocket_Inquired_NextHopQueryMessage);
			_algo.Close ();
		}

		public IAsyncResult BeginRoute (Key dest, EndPoint[] firstHops, int numOfCandidates, int numOfSimultaneous, AsyncCallback callback, object state)
		{
			RoutingInfo info = new RoutingInfo (this, dest, numOfCandidates, numOfSimultaneous, callback, state);
			if (firstHops == null)
				info.StartFirstHop ();
			else
				info.StartFirstHop (firstHops);
			return info;
		}

		public RoutingResult EndRoute (IAsyncResult ar)
		{
			RoutingInfo info = (RoutingInfo)ar;
			info.AsyncWaitHandle.WaitOne ();
			return info.Result;
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

		#region Internal Class
		class RoutingInfo : IAsyncResult
		{
			Key _dest;
			SimpleIterativeRouter _router;
			int _numOfCandidates, _numOfSimultaneous, _inquiring = 0;
			AsyncCallback _callback;
			object _state;
			bool _completed = false;
			ManualResetEvent _done = new ManualResetEvent (false);
			HopSlot[] _hops;
			NextHopQueryMessage _query;
			List<NodeHandle> _rootCandidates = new List<NodeHandle> ();
			RoutingResult _result = null;

			public RoutingInfo (SimpleIterativeRouter router, Key dest, int numOfCandidates, int numOfSimultaneous,
				AsyncCallback callback, object state)
			{
				_router = router;
				_dest = dest;
				_numOfCandidates = numOfCandidates;
				_numOfSimultaneous = numOfSimultaneous;
				_callback = callback;
				_state = state;
				_hops = new HopSlot[router.RoutingAlgorithm.MaxRoutingLevel + 1];
				_query = new NextHopQueryMessage (router.SelftNodeId, dest, numOfSimultaneous, numOfCandidates);
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

			#region Start Firsthop
			public void StartFirstHop ()
			{
				NodeHandle[] nextHops;
				bool isRootCandidates = false;

				if (_router.SelftNodeId.Equals (_dest)) {
					nextHops = _router.RoutingAlgorithm.GetRandomNodes (_numOfSimultaneous);
				} else {
					nextHops = _router.RoutingAlgorithm.GetNextHopNodes (_dest, _numOfSimultaneous, null);
					if (nextHops == null || nextHops.Length == 0) {
						isRootCandidates = true;
						nextHops = _router.RoutingAlgorithm.GetNeighbors (_numOfCandidates);
					}
				}

				StartNextInquiry (null, nextHops, isRootCandidates);
			}

			public void StartFirstHop (EndPoint[] firstHops)
			{
				NodeHandle[] hops = new NodeHandle[firstHops.Length];
				for (int i = 0; i < hops.Length; i ++)
					hops[i] = new NodeHandle (null, firstHops[i]);
				StartNextInquiry (null, hops, false);
			}
			#endregion

			#region StartNextInquiry
			void StartNextInquiry (HopSlotEntry sender, NodeHandle[] nextHops, bool isRootCandidates)
			{
				bool failFlag = false, successFlag = false;
				NodeHandle[] candidates = null;
				List<HopSlotEntry> list = null;
				int nextHopIndex = (sender == null ? 1 : sender.Hop + 1);

				lock (_hops) {
					do {
						if (_result != null) {
							failFlag = true;
							break;
						}
						if (isRootCandidates) {
							candidates = new NodeHandle[nextHops == null ? 1 : (nextHops.Length + 1)];
							candidates[0] = (sender != null ? sender.NodeHandle : new NodeHandle (_router.SelftNodeId, null));
							if (nextHops != null) {
								for (int i = 0; i < nextHops.Length; i++)
									candidates[i + 1] = nextHops[i];
							}
							successFlag = true;
							break;
						} else {
							if (nextHops == null) {
								failFlag = true;
								break;
							} else {
								list = new List<HopSlotEntry> ();
								SetupSlots (nextHops, list, nextHopIndex);
								if (_inquiring == 1 && sender != null) {
									failFlag = true;
									break;
								} else if (_inquiring == 0 && sender == null) {
									failFlag = true;
									break;
								}
							}
						}
					} while (false);

					if (sender != null)
						_inquiring --;
				}

				if (list != null) {
					for (int i = 0; i < list.Count; i++)
						_router._sock.BeginInquire (_query, list[i].NodeHandle.EndPoint, Inquire_Callback, list[i]);
				}

				if (failFlag) {
					Fail ();
				} else if (successFlag) {
					Success (candidates);
				}
			}
			#endregion

			#region Slot Management
			void SetupSlots (NodeHandle[] nodes, List<HopSlotEntry> list, int nextHopIndex)
			{
				int min_level = GetFinishedLevel ();
				for (int i = 0; i < nodes.Length; i++) {
					int level = (nodes[i].NodeID == null ? 0 : _router.RoutingAlgorithm.ComputeRoutingLevel (_dest, nodes[i].NodeID));
					if (level <= min_level)
						continue;

					if (_hops[level] == null)
						_hops[level] = new HopSlot (_numOfSimultaneous);
					else if (_hops[level].Filled)
						continue;
					list.Add (_hops[level].AddEntry (nodes[i], nextHopIndex));
					_inquiring++;
				}
			}

			int GetFinishedLevel ()
			{
				for (int i = _hops.Length - 1; i >= 0; i --) {
					if (_hops[i] == null) continue;
					if (_hops[i].Filled)
						return i;
				}
				return -1;
			}
			#endregion

			#region Inquire_Callback
			void Inquire_Callback (IAsyncResult ar)
			{
				object rawObj = _router._sock.EndInquire (ar);
				HopSlotEntry sender = (HopSlotEntry)ar.AsyncState;
				NextHopQueryResponse res = rawObj as NextHopQueryResponse;

				lock (_hops) {
					sender.Responsed ();
				}

				if (res == null) {
					/*if (rawObj != null)
						_logger.Error ("Received Wrong Message {0}", rawObj.GetType ().ToString ());*/
					_router.RoutingAlgorithm.Fail (sender.NodeHandle);
					StartNextInquiry (sender, null, false);
					return;
				}

				/*if (res.NextHops != null) {
					for (int i = 0; i < res.NextHops.Length; i ++)
						_router.RoutingAlgorithm.Touch (res.NextHops[i]);
				}
				if (res.RandomNodes != null) {
					for (int i = 0; i < res.RandomNodes.Length; i ++)
						_router.RoutingAlgorithm.Touch (res.RandomNodes[i]);
				}*/

				sender.SetNodeID (res.Sender);
				_router.RoutingAlgorithm.Touch (sender.NodeHandle);
				StartNextInquiry (sender, res.NextHops, res.IsRootCandidate);
			}
			#endregion

			#region Success / Fail
			void Success (NodeHandle[] rootCandidates)
			{
				lock (_rootCandidates) {
					_rootCandidates.AddRange (rootCandidates);
					if (_result != null)
						return;
					/*if (!_dest.Equals (rootCandidates[0].NodeID)) {
						lock (_hops) {
							if (_inquiring > 0)
								return;
						}
					}*/
					RemoveDuplicateAndSortRootCandidates ();
					CreateResult (_rootCandidates.ToArray ());
				}

				FinishedRouting ();
			}

			void Fail ()
			{
				lock (_rootCandidates) {
					if (_result != null)
						return;
					lock (_hops) {
						if (_inquiring > 0)
							return;
					}
					if (_rootCandidates.Count == 0) {
						_result = new RoutingResult (null, 0);
					} else {
						RemoveDuplicateAndSortRootCandidates ();
						CreateResult (_rootCandidates.ToArray ());
					}
				}
				FinishedRouting ();
			}

			void CreateResult (NodeHandle[] candidates)
			{
				int hop = 0;
				lock (_hops) {
					for (int i = _hops.Length - 1; i >= 0; i --) {
						if (_hops[i] == null || _hops[i].MinHop == int.MaxValue) continue;
						hop = _hops[i].MinHop;
						break;
					}
				}
				_result = new RoutingResult (candidates, hop);
			}

			void RemoveDuplicateAndSortRootCandidates ()
			{
				// remove duplicate
				for (int i = 0; i < _rootCandidates.Count; i ++) {
					for (int j = i + 1; j < _rootCandidates.Count; j ++) {
						if (_rootCandidates[i].NodeID.Equals (_rootCandidates[j].NodeID)) {
							_rootCandidates.RemoveAt (i --);
							break;
						}
					}
				}

				_rootCandidates.Sort (delegate (NodeHandle x, NodeHandle y) {
					return _router.RoutingAlgorithm.ComputeDistance (_dest, x.NodeID).CompareTo (
						_router.RoutingAlgorithm.ComputeDistance (_dest, y.NodeID));
				});
			}

			void FinishedRouting ()
			{
				_completed = true;
				_done.Set ();
				if (_callback != null) {
					try {
						_callback (this);
					} catch {}
				}
			}
			#endregion

			#region Properties
			public RoutingResult Result {
				get { return _result; }
			}
			#endregion

			#region Internal Classes
			class HopSlot
			{
				HopSlotEntry[] _entries;
				int _nextIndex = 0, _minHop = int.MaxValue;

				public HopSlot (int numOfEntries)
				{
					_entries = new HopSlotEntry[numOfEntries];
				}

				public HopSlotEntry this [int index] {
					get { return _entries[index]; }
				}

				public HopSlotEntry AddEntry (NodeHandle node, int hop)
				{
					HopSlotEntry entry = new HopSlotEntry (node, hop);
					_minHop = Math.Min (_minHop, hop);
					_entries[_nextIndex++] = entry;
					return entry;
				}

				public bool Filled {
					get { return _nextIndex >= _entries.Length; }
				}

				public int MinHop {
					get { return _minHop; }
				}
			}
			class HopSlotEntry
			{
				NodeHandle _node;
				DateTime _start;
				TimeSpan _time;
				int _hop;

				public HopSlotEntry (NodeHandle node, int hop)
				{
					_node = node;
					_start = DateTime.Now;
					_time = TimeSpan.MaxValue;
					_hop = hop;
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

				public int Hop {
					get { return _hop; }
				}
				
				public void Responsed ()
				{
					_time = DateTime.Now.Subtract (_start);
				}

				public void SetNodeID (Key key)
				{
					if (_node.NodeID == null)
						_node = new NodeHandle (key, _node.EndPoint);
				}
			}
			#endregion
		}

		[Serializable]
		[SerializableTypeId (0x110)]
		class NextHopQueryMessage
		{
			[SerializableFieldIndex (0)]
			Key _sender;

			[SerializableFieldIndex (1)]
			Key _dest;

			[SerializableFieldIndex (2)]
			int _numOfNextHops;

			[SerializableFieldIndex (3)]
			int _numOfRootCandidates;

			public NextHopQueryMessage (Key sender, Key dest, int numOfNextHops, int numOfRootCandidates)
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

		[Serializable]
		[SerializableTypeId (0x111)]
		class NextHopQueryResponse
		{
			[SerializableFieldIndex (0)]
			Key _sender;

			[SerializableFieldIndex (1)]
			bool _isRootCandidate;

			[SerializableFieldIndex (2)]
			NodeHandle[] _nextHops;

			[SerializableFieldIndex (3)]
			NodeHandle[] _randomNodes;

			public NextHopQueryResponse (Key sender, bool isRootCandidate, NodeHandle[] nextHops, NodeHandle[] randomNodes)
			{
				_sender = sender;
				_isRootCandidate = isRootCandidate;
				_nextHops = nextHops;
				_randomNodes = randomNodes;
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

			public NodeHandle[] RandomNodes {
				get { return _randomNodes; }
			}
		}
		#endregion
	}
}
