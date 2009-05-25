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
using System.Text;
using System.Collections.Generic;
using System.Net;

namespace p2pncs.Net.Overlay
{
	public class SimpleRoutingAlgorithm : IKeyBasedRoutingAlgorithm
	{
		Random _rnd = new Random (BitConverter.ToInt32 (openCrypto.RNG.GetRNGBytes (4), 0));
		const int BucketSize = 6;
		Key _selfNodeId = null;
		IKeyBasedRouter _router = null;
		List<NodeHandle>[] _routingTable = null;
		List<NodeHandle> _flatList = new List<NodeHandle> ();
		Dictionary<EndPoint, NodeHandle> _epMapping = new Dictionary<EndPoint, NodeHandle> ();

		public SimpleRoutingAlgorithm ()
		{
		}

		#region IKeyBasedRoutingAlgorithm Members

		public void Join (EndPoint[] initialNodes)
		{
			for (int i = 0; i < initialNodes.Length; i ++)
				_router.BeginRoute (_selfNodeId, new EndPoint[] {initialNodes[i]}, 1, 3, null, null);
		}

		public void Close ()
		{
		}

		public Key ComputeDistance (Key x, Key y)
		{
			return x.Xor (y);
		}

		public int ComputeRoutingLevel (Key x, Key y)
		{
			return Key.MatchBitsFromMSB (x, y);
		}

		public int MaxRoutingLevel {
			get { return _selfNodeId.KeyBits; }
		}

		public NodeHandle[] GetNextHopNodes (Key dest, int maxNum, Key exclude)
		{
			int match = Key.MatchBitsFromMSB (_selfNodeId, dest);
			if (match >= _routingTable.Length || _routingTable[match] == null)
				return null;

			List<NodeHandle> list = new List<NodeHandle> ();
			Key minDist = _selfNodeId.Xor (dest);
			Key selfDist = minDist;
			lock (_routingTable[match]) {
				for (int i = 0; i < Math.Min (maxNum, _routingTable[match].Count); i++) {
					if (exclude != null && exclude.Equals (_routingTable[match][i].NodeID))
						continue;
					Key dist = dest.Xor (_routingTable[match][i].NodeID);
					if (minDist.CompareTo (dist) > 0) {
						minDist = dist;
					}
					list.Add (_routingTable[match][i]);
				}
			}

			if (minDist == selfDist)
				return null;
			return list.ToArray ();
		}

		public NodeHandle[] GetRandomNodes (int maxNum)
		{
			List<NodeHandle> list = new List<NodeHandle> (maxNum);
			HashSet<Key> addedSet = new HashSet<Key> ();
			int remain = maxNum;

			lock (_routingTable) {
				if (_flatList.Count == 0)
					return new NodeHandle[0];
				while (remain > 0) {
					for (int i = 0; i < 5; i ++) {
						int idx = _rnd.Next (0, _flatList.Count);
						if (addedSet.Contains (_flatList[idx].NodeID))
							continue;
						list.Add (_flatList[idx]);
						addedSet.Add (_flatList[idx].NodeID);
						break;
					}
					remain --;
				}
			}

			return list.ToArray ();
		}

		public NodeHandle[] GetNeighbors (int maxNum)
		{
			List<NodeHandle> list = new List<NodeHandle> (maxNum);
			int remain = maxNum;

			for (int i = _routingTable.Length - 1; remain > 0 && i >= 0; i--) {
				if (_routingTable[i] == null || _routingTable[i].Count == 0) continue;
				
				lock (_routingTable[i]) {
					if (remain >= _routingTable[i].Count) {
						for (int k = 0; k < _routingTable[i].Count; k++)
							list.Add (_routingTable[i][k]);
						remain -= _routingTable[i].Count;;
					} else {
						List<NodeHandle> temp = new List<NodeHandle> (_routingTable[i]);
						temp.Sort (delegate (NodeHandle x, NodeHandle y) {
							Key diffX = _selfNodeId.Xor (x.NodeID);
							Key diffY = _selfNodeId.Xor (y.NodeID);
							return diffX.CompareTo (diffY);
						});
						for (int k = 0; k < remain; k ++)
							list.Add (temp[k]);
						remain = 0;
					}
				}
			}
			return list.ToArray ();
		}

		public NodeHandle[] GetCloseNodes (Key target, int maxNum)
		{
			NodeHandle[] nodes;
			lock (_routingTable) {
				nodes = _flatList.ToArray ();
			}
			Array.Sort<NodeHandle> (nodes, delegate (NodeHandle x, NodeHandle y) {
				Key diffX = target.Xor (x.NodeID);
				Key diffY = target.Xor (y.NodeID);
				return diffX.CompareTo (diffY);
			});

			NodeHandle[] results = new NodeHandle[Math.Min (nodes.Length, maxNum)];
			Array.Copy (nodes, 0, results, 0, results.Length);
			return results;
		}

		public void Touch (NodeHandle node)
		{
			if (node.NodeID == null)
				return;
			List<NodeHandle> list = LookupNodeList (node.NodeID);
			if (list == null)
				return;

			lock (_routingTable) {
				if (list.Count >= BucketSize)
					return;
				for (int i = 0; i < list.Count; i ++) {
					if (list[i].NodeID.Equals (node.NodeID))
						return;
				}
				//Logger.Log (LogLevel.Trace, this, "{0}: add routing-table entry to {1}", _selfNodeId, node.NodeID);
				list.Add (node);
				_flatList.Add (node);
				_epMapping[node.EndPoint] = node;
			}
		}

		public void Fail (NodeHandle node)
		{
			if (node.NodeID == null) {
				Fail (node.EndPoint);
				return;
			}
			List<NodeHandle> list = LookupNodeList (node.NodeID);
			if (list == null)
				return;

			lock (_routingTable) {
				for (int i = 0; i < list.Count; i++) {
					if (list[i].NodeID.Equals (node.NodeID)) {
						_flatList.Remove (list[i]);
						list.RemoveAt (i);
						return;
					}
				}
			}
		}

		void Fail (EndPoint ep)
		{
			NodeHandle node;
			lock (_routingTable) {
				if (!_epMapping.TryGetValue (ep, out node))
					return;
				_epMapping.Remove (ep);
			}
			Fail (node);
		}

		List<NodeHandle> LookupNodeList (Key target)
		{
			List<NodeHandle> list;
			int match = Key.MatchBitsFromMSB (_selfNodeId, target);

			if (match >= _routingTable.Length)
				return null;

			lock (_routingTable) {
				list = _routingTable[match];
				if (list == null)
					list = _routingTable[match] = new List<NodeHandle> ();
			}

			return list;
		}

		public void Setup (Key selfNodeId, IKeyBasedRouter router)
		{
			_selfNodeId = selfNodeId;
			_router = router;
			_routingTable = new List<NodeHandle> [selfNodeId.KeyBits];
		}

		public void Stabilize ()
		{
			_router.BeginRoute (_selfNodeId, null, 3, 3, null, null);
			_router.BeginRoute (Key.CreateRandom (_selfNodeId.KeyBytes), null, 3, 3, null, null);
		}

		#endregion
	}
}
