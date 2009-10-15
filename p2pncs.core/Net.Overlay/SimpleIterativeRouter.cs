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
using System.Threading;

namespace p2pncs.Net.Overlay
{
	public class SimpleIterativeRouter : IKeyBasedRouter
	{
		IKeyBasedRoutingAlgorithm _algo;
		IMessagingSocket _msock;

		public event EventHandler<StatisticsNoticeEventArgs> StatisticsNotice;

		public SimpleIterativeRouter (IKeyBasedRoutingAlgorithm algo, IMessagingSocket msock)
		{
			_algo = algo;
			_msock = msock;
			_algo.Setup (this);
			_msock.AddInquiredHandler (typeof (FindCloseNode), Received_FindCloseNode);
		}

		public void Join (Key appId, EndPoint[] initialNodes)
		{
			_algo.Join (appId, initialNodes);
		}

		public void Close (Key appId)
		{
			_algo.Close (appId);
			throw new NotImplementedException ();
		}

		public void Close ()
		{
			_algo.Close ();
			_msock.RemoveInquiredHandler (typeof (FindCloseNode), Received_FindCloseNode);
		}

		public IAsyncResult BeginRoute (Key appId, Key dest, int numOfCandidates, KeyBasedRoutingOptions opts, AsyncCallback callback, object state)
		{
			return new RoutingInfo (this, appId, dest, numOfCandidates, opts, callback, state);
		}

		public RoutingResult EndRoute (IAsyncResult ar)
		{
			ar.AsyncWaitHandle.WaitOne ();
			return (ar as RoutingInfo).Result;
		}

		public IKeyBasedRoutingAlgorithm RoutingAlgorithm {
			get { return _algo; }
		}

		void Received_FindCloseNode (object sender, InquiredEventArgs args)
		{
			FindCloseNode req = (FindCloseNode)args.InquireMessage;
			NodeHandle[] results = _algo.GetCloseNodes (req.AppId, req.Destination, 5);
			_msock.StartResponse (args, new CloseNodeSet (_algo.SelfNodeHandle, results));

			req.NodeHandle.EndPoint = args.EndPoint;
			_algo.Touch (req.NodeHandle);
		}

		#region Internal Classes
		class RoutingInfo : IAsyncResult
		{
			Key _appId, _dest;
			SimpleIterativeRouter _router;
			int _numOfCandidates, _numOfSimultaneous = -1, _maxMatchBits = -1;

			FindCloseNode _findMsg;

			AsyncCallback _callback;
			object _state;
			bool _completed = false;
			ManualResetEvent _done = new ManualResetEvent (false);
			RoutingResult _result = null;

			List<CandidateEntry> _candidates = new List<CandidateEntry> ();
			int _inquiring = 0;

			public RoutingInfo (SimpleIterativeRouter router, Key appId, Key dest, int numOfCandidates, KeyBasedRoutingOptions options, AsyncCallback callback, object state)
			{
				if (appId == null || dest == null)
					throw new ArgumentNullException ();
				if (numOfCandidates <= 0)
					throw new ArgumentOutOfRangeException ();

				EndPoint[] firstHops = null;
				_router = router;
				_appId = appId;
				_dest = dest;
				_numOfCandidates = numOfCandidates;
				_callback = callback;
				_state = state;
				_findMsg = new FindCloseNode (router._algo.SelfNodeHandle, appId, dest);

				if (options != null) {
					_numOfSimultaneous = options.NumberOfSimultaneous;
					firstHops = options.FirstHops;
					_maxMatchBits = options.RoutingFinishedMatchBits;
				}

				if (_numOfSimultaneous < 1)
					_numOfSimultaneous = 3;
				if (firstHops != null && firstHops.Length == 0)
					firstHops = null;
				if (_maxMatchBits >= dest.KeyBits)
					_maxMatchBits = -1;

				if (firstHops == null) {
					NodeHandle[] closeNodes = _router._algo.GetCloseNodes (appId, dest, numOfCandidates);
					for (int i = 0; i < closeNodes.Length; i ++)
						_candidates.Add (new CandidateEntry (_dest, closeNodes[i].NodeID,
							_router._algo.ComputeDistance (_dest, closeNodes[i].NodeID), closeNodes[i].EndPoint, 1));
				} else {
					Key tmpKey = ~dest;
					Key tmpDistance = _router._algo.ComputeDistance (_dest, tmpKey);
					for (int i = 0; i < firstHops.Length; i ++) {
						_candidates.Add (new CandidateEntry (_dest, tmpKey, tmpDistance, firstHops[i], 1));
					}
				}

				TryNext ();
			}

			void TryNext ()
			{
				bool completed = false;
				lock (_candidates) {
					for (int idx = 0, responsedCount = 0; responsedCount < _numOfCandidates && idx < _candidates.Count && _inquiring < _numOfSimultaneous; idx ++) {
						CandidateEntry entry = _candidates[idx];
						if (entry.IsResponsed) {
							responsedCount ++;
							if (_maxMatchBits >= 0 && entry.MatchBits > _maxMatchBits)
								continue;
						}
						if (!entry.IsNoCheck)
							continue;

						_router._msock.BeginInquire (_findMsg, entry.EndPoint, FindQuery_Callback, entry);
						entry.IsRunning = true;
						_inquiring ++;
					}

					if (_inquiring == 0)
						completed = true;
				}

				if (completed) {
					List<NodeHandle> list = new List<NodeHandle> ();
					int hops = 0;
					for (int i = 0; i < _candidates.Count && list.Count < _numOfCandidates; i ++) {
						if (!_candidates[i].IsResponsed)
							continue;
						hops = Math.Max (hops, _candidates[i].Hops);
						list.Add (new NodeHandle (_candidates[i].NodeID, _candidates[i].EndPoint));
					}
					_result = new RoutingResult (list.ToArray (), hops);
					_completed = true;
					_done.Set ();
					if (_callback != null) {
						try {
							_callback (this);
						} catch {}
					}
				}
			}

			void FindQuery_Callback (IAsyncResult ar)
			{
				CandidateEntry entry = ar.AsyncState as CandidateEntry;
				CloseNodeSet msg = _router._msock.EndInquire (ar) as CloseNodeSet;

				lock (_candidates) {
					_inquiring --;
					if (msg == null) {
						entry.IsFailed = true;
					} else {
						msg.NodeHandle.EndPoint = entry.EndPoint;
						entry.Responsed (_dest, _router._algo, msg.NodeHandle);
						if (msg.CloseNodes != null) {
							foreach (NodeHandle node in msg.CloseNodes) {
								Key distance = _router._algo.ComputeDistance (_dest, node.NodeID);
								for (int i = 0; i < _candidates.Count; i ++) {
									int comp = distance.CompareTo (_candidates[i].Distance);
									if (comp == 0)
										break;
									if (comp < 0 || i == _candidates.Count - 1) {
										CandidateEntry new_entry = new CandidateEntry (_dest, node.NodeID, distance, node.EndPoint, entry.Hops + 1);
										if (comp < 0)
											_candidates.Insert (i, new_entry);
										else
											_candidates.Add (new_entry);
										break;
									}
								}
							}
						}
					}
				}

				TryNext ();

				if (entry.IsFailed) {
					_router._algo.Fail (entry.EndPoint);
				} else {
					_router._algo.Touch (msg.NodeHandle);
				}
			}

			public RoutingResult Result {
				get { return _result; }
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
			
			class CandidateEntry
			{
				Key _nodeId, _distance;
				EndPoint _ep;
				MultiAppNodeHandle _nodeHandle = null;
				bool _failed = false, _running = false;
				int _hop, _matchBits;

				public CandidateEntry (Key target, Key nodeId, Key distance, EndPoint ep, int hop)
				{
					_nodeId = nodeId;
					_distance = distance;
					_matchBits = Key.MatchBitsFromMSB (target, nodeId);
					_ep = ep;
					_hop = hop;
				}

				public void Responsed (Key target, IKeyBasedRoutingAlgorithm algo, MultiAppNodeHandle nodeHandle)
				{
					_nodeId = nodeHandle.NodeID;
					_distance = algo.ComputeDistance (target, _nodeId);
					_matchBits = Key.MatchBitsFromMSB (target, _nodeId);
					_nodeHandle = nodeHandle;
				}

				public void Fail ()
				{
					_failed = true;
				}

				public Key NodeID {
					get { return _nodeId; }
				}

				public Key Distance {
					get { return _distance; }
				}

				public EndPoint EndPoint {
					get { return _ep; }
				}

				public MultiAppNodeHandle NodeHandle {
					get { return _nodeHandle; }
				}

				public int Hops {
					get { return _hop; }
				}

				public int MatchBits {
					get { return _matchBits; }
				}

				public bool IsRunning {
					get { return _running; }
					set { _running = value;}
				}

				public bool IsResponsed {
					get { return _nodeHandle != null; }
				}

				public bool IsFailed {
					get { return _failed; }
					set { _failed = value;}
				}

				public bool IsNoCheck {
					get { return !(IsRunning || IsResponsed || IsFailed); }
				}
			}
		}

		[SerializableTypeId (0x1f3)]
		sealed class FindCloseNode : IIterativeMessage
		{
			[SerializableFieldId (0)]
			MultiAppNodeHandle _handle;

			[SerializableFieldId (1)]
			Key _appId;

			[SerializableFieldId (2)]
			Key _dest;

			public FindCloseNode (MultiAppNodeHandle handle, Key appId, Key dest)
			{
				_handle = handle;
				_appId = appId;
				_dest = dest;
			}

			public MultiAppNodeHandle NodeHandle {
				get { return _handle; }
			}

			public Key AppId {
				get { return _appId; }
			}

			public Key Destination {
				get { return _dest; }
			}
		}

		[SerializableTypeId (0x1f4)]
		sealed class CloseNodeSet : IIterativeMessage
		{
			[SerializableFieldId (0)]
			MultiAppNodeHandle _handle;

			[SerializableFieldId (1)]
			NodeHandle[] _closeNodes;

			public CloseNodeSet (MultiAppNodeHandle handle, NodeHandle[] closeNodes)
			{
				_handle = handle;
				_closeNodes = closeNodes;
			}

			public MultiAppNodeHandle NodeHandle {
				get { return _handle; }
			}

			public NodeHandle[] CloseNodes {
				get { return _closeNodes; }
			}
		}
		#endregion
	}
}
