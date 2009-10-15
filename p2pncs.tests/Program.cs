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
using p2pncs.Net;
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.Anonymous;
using p2pncs.Net.Overlay.DHT;
using p2pncs.Simulation;
using p2pncs.Simulation.VirtualNet;
using p2pncs.Threading;
using Key = p2pncs.Net.Overlay.Key;
using openCrypto.EllipticCurve;

namespace p2pncs
{
	class Program
	{
		const ECDomainNames DefaultECDomain = ECDomainNames.secp192r1;
		static int BucketSize;

		static void Main (string[] args)
		{
			int numOfNodes = 1 << 14;
			BucketSize = int.Parse (args[0]);

			OSTimerPrecision.SetCurrentThreadToHighPrecision ();
			VirtualNetwork network = new VirtualNetwork (LatencyTypes.Constant (5), 5, PacketLossType.Constant (0.0), Environment.ProcessorCount);
			using (IntervalInterrupter messagingInt = new IntervalInterrupter (TimeSpan.FromMilliseconds (100), "Messaging"))
			using (IntervalInterrupter timeoutCheckInt = new IntervalInterrupter (TimeSpan.FromSeconds (1), "TimeoutCheck"))
			using (IntervalInterrupter stabilizeInt = new IntervalInterrupter (TimeSpan.FromMinutes (5), "DHT Stabilizing")) {
				messagingInt.Start ();
				timeoutCheckInt.Start ();
				stabilizeInt.LoadEqualizing = true;
				List<VirtualNode> allNodes = new List<VirtualNode> ();
				try {
					Run (numOfNodes, network, messagingInt, timeoutCheckInt, stabilizeInt, allNodes);
				} finally {
					for (int i = 0; i < allNodes.Count; i++)
						allNodes[i].Close ();
				}
			}
			network.Close ();
		}

		static void Run (int numOfNodes, VirtualNetwork network, IntervalInterrupter messagingInt, IntervalInterrupter timeoutCheckInt, IntervalInterrupter stabilizeInt, List<VirtualNode> allNodes)
		{
			RandomIPAddressGenerator rndIP = new RandomIPAddressGenerator ();
			Key[] appIDs = new Key[] {new Key (new byte[]{0})};
			DoWithProgress (0, numOfNodes, "Insert Nodes", delegate (int i) {
				VirtualNode node = new VirtualNode (network, rndIP.Next (), appIDs, messagingInt, timeoutCheckInt);
				stabilizeInt.AddInterruption (node.Stabilize);
				allNodes.Add (node);
				if (i > 0)
					node.KeyBasedRouter.Join (appIDs[0], new EndPoint[] { allNodes[0].PublicEndPoint });
				System.Threading.Thread.Sleep (25);
			});
			System.Threading.Thread.Sleep (TimeSpan.FromSeconds (10));

			Random rnd = new Random ();
			p2pncs.Utility.StandardDeviation sdHops = new p2pncs.Utility.StandardDeviation (false);
			int success = 0;
			int lookupTests = 1000;
			string msg = "";
			DoWithProgress (0, lookupTests, "Lookuping", delegate (int i) {
				VirtualNode node1 = allNodes[rnd.Next (0, allNodes.Count)];
				VirtualNode node2;
				while (true) {
					node2 = allNodes[rnd.Next (0, allNodes.Count)];
					if (node1 != node2)
						break;
				}
				IAsyncResult ar = node1.KeyBasedRouter.BeginRoute (appIDs[0], node2.KeyBasedRoutingAlgorithm.SelfNodeHandle.NodeID, 1, null, null, null);
				RoutingResult rr = node1.KeyBasedRouter.EndRoute (ar);
				if (rr.RootCandidates == null) {
					msg += "Root Candidates is NULL\r\n";
				} else if (rr.RootCandidates.Length == 0) {
					msg += "Root Candidates is Empty\r\n";
				} else if (rr.RootCandidates.Length > 1) {
					msg += string.Format ("Size of Root Candidates is {0}\r\n", rr.RootCandidates.Length);
				} else {
					if (rr.RootCandidates[0].NodeID.Equals (node2.KeyBasedRoutingAlgorithm.SelfNodeHandle.NodeID)) {
						sdHops.AddSample (rr.Hops);
						success++;
					} else {
						msg += string.Format ("Expected={0} but actual={1}\r\n",
							node2.KeyBasedRoutingAlgorithm.SelfNodeHandle.NodeID.ToString (2).Substring (0, 10),
							rr.RootCandidates[0].NodeID.ToString (2).Substring (0, 10));
					}
				}
			});
			Logger.Log (LogLevel.Info, null, "Nodes = {0}, BucketSize = {1}", numOfNodes, BucketSize);
			Logger.Log (LogLevel.Info, null, "  Success = {0:p}", success / (double)lookupTests);
			Logger.Log (LogLevel.Info, null, "  Hops = Min:{0:f2}/Avg:{1:f2}(SD:{2:f2})/Max:{3:f2}",
				sdHops.Minimum, sdHops.Average, sdHops.ComputeStandardDeviation (), sdHops.Maximum);
			/*if (msg.Length > 0) {
				Logger.Log (LogLevel.Info, null, msg);
			}*/
			//Console.ReadLine ();

			/*Key testKey = Key.CreateRandom (allNodes[0].KeyBasedRoutingAlgorithm.SelfNodeHandle.NodeID.KeyBytes);
			IAsyncResult ar;
			ar = allNodes[0].DistributedHashTable.BeginPut (appIDs[0], testKey, TimeSpan.FromSeconds (10), "hoge", null, null);
			allNodes[0].DistributedHashTable.EndPut (ar);

			ar = allNodes[1].DistributedHashTable.BeginPut (appIDs[0], testKey, TimeSpan.FromSeconds (10), "foo", null, null);
			allNodes[1].DistributedHashTable.EndPut (ar);

			ar = allNodes[2].DistributedHashTable.BeginPut (appIDs[0], testKey, TimeSpan.FromSeconds (10), "bar", null, null);
			allNodes[2].DistributedHashTable.EndPut (ar);

			ar = allNodes[3].DistributedHashTable.BeginPut (appIDs[0], testKey, TimeSpan.FromSeconds (10), "bar", null, null);
			allNodes[3].DistributedHashTable.EndPut (ar);

			for (int k = 0; k < 2; k ++) {
				ar = allNodes[123].DistributedHashTable.BeginGet (appIDs[0], testKey, typeof (string), new GetOptions (), null, null);
				GetResult result = allNodes[123].DistributedHashTable.EndGet (ar);
				for (int i = 0; i < result.Values.Length; i++)
					Console.WriteLine ("{0}: {1}", i, result.Values[i]);
				if (k == 0) {
					Console.WriteLine ("Waiting...10+α sec");
					System.Threading.Thread.Sleep (TimeSpan.FromSeconds (15));
				} else {
					Console.WriteLine ("Completed");
				}
			}
			Console.ReadLine ();*/

			/*int endPointsPerNode = 1;
			IAnonymousEndPoint[] endPoints = new IAnonymousEndPoint[allNodes.Count * endPointsPerNode];
			DoWithProgress (0, allNodes.Count * endPointsPerNode, "Create Anonymous EndPoints", delegate (int i) {
				endPoints[i] = allNodes[i % allNodes.Count].AnonymousRouter.CreateEndPoint (ECKeyPair.Create (DefaultECDomain),
					new AnonymousRouter.AnonymousEndPointOptions { AppId = appIDs[0], NumberOfRoutes = 2, NumberOfRelays = 3 });
				System.Threading.Thread.Sleep (20);
			});
			Console.ReadLine ();
			DoWithProgress (0, endPoints.Length, "Dispose Anonymous EndPoints", delegate (int i) {
				endPoints[i].Close ();
			});
			Console.ReadLine ();*/
		}

		delegate void LoopDelegate (int i);
		static void DoWithProgress (int start, int max, string msg, LoopDelegate proc)
		{
			int cx = Console.CursorLeft, cy = Console.CursorTop;
			for (int i = start; i < max; i ++) {
				Console.CursorLeft = cx; Console.CursorTop = cy;
				proc (i);
				Console.Write ("{0}...{1:p2}", msg, (i - start) / (double)(max - start));
			}
			Console.CursorLeft = cx; Console.CursorTop = cy;
			Console.WriteLine ("{0}...[OK]  ", msg);
		}

		class VirtualNode
		{
			Key _self;
			ECKeyPair _nodePrivate;
			Key[] _appIDs;
			IPEndPoint _pubEp;
			VirtualDatagramEventSocket _dgramSock;
			IMessagingSocket _msock;
			IKeyBasedRouter _kbr;
			IKeyBasedRoutingAlgorithm _kbrAlgo;
			ILocalHashTable _localStore;
			IDistributedHashTable _dht;
			IAnonymousRouter _anonRouter;

			public VirtualNode (VirtualNetwork net, IPAddress adrs, Key[] appIDs, IntervalInterrupter messagingRetryInt, IntervalInterrupter timeoutCheckInt)
			{
				_pubEp = new IPEndPoint (adrs, 8080);
				_dgramSock = new VirtualDatagramEventSocket (net, adrs);
				_dgramSock.Bind (new IPEndPoint (IPAddress.Any, _pubEp.Port));
				_msock = new VirtualMessagingSocket (_dgramSock, true, messagingRetryInt,
					new RFC2988BasedRTOCalculator (TimeSpan.FromSeconds (1), TimeSpan.FromMilliseconds (500), 50, false), 5, 64, 8);
				/*_msock = new MessagingSocket (_dgramSock, true, null, Serializer.Instance, null, messagingRetryInt,
					new RFC2988BasedRTOCalculator (TimeSpan.FromSeconds (1), TimeSpan.FromMilliseconds (500), 50, false), 5, 64, 8);*/

				_nodePrivate = ECKeyPair.Create (DefaultECDomain);
				_self = Key.Create (_nodePrivate);
				_appIDs = appIDs;
				_kbrAlgo = new SimpleRoutingAlgorithm (_self, _msock, BucketSize, TimeSpan.FromMinutes (5));
				for (int i = 0; i < appIDs.Length; i++)
					_kbrAlgo.NewApp (appIDs[i]);
				_kbr = new SimpleIterativeRouter (_kbrAlgo, _msock);
				_localStore = new OnMemoryStore (timeoutCheckInt);
				_dht = new SimpleDHT (_msock, _kbr, _localStore);
				_dht.RegisterType (typeof (string), 0);
				_anonRouter = new AnonymousRouter (_kbr, _kbrAlgo, _msock, _nodePrivate, timeoutCheckInt);
			}

			public void Close ()
			{
				try {
					_anonRouter.Close ();
				} catch {}
				try {
					_dht.Dispose ();
				} catch {}
				try {
					_localStore.Close ();
				} catch {}
				try {
					_kbrAlgo.Close ();
				} catch {}
				try {
					_kbr.Close ();
				} catch {}
				try {
					_msock.Close ();
				} catch {}
			}

			public void Stabilize ()
			{
				foreach (Key appId in _appIDs)
					_kbrAlgo.Stabilize (appId);
			}

			public EndPoint PublicEndPoint {
				get { return _pubEp; }
			}

			public IKeyBasedRouter KeyBasedRouter {
				get { return _kbr; }
			}

			public IKeyBasedRoutingAlgorithm KeyBasedRoutingAlgorithm {
				get { return _kbrAlgo; }
			}

			public IDistributedHashTable DistributedHashTable {
				get { return _dht; }
			}

			public IAnonymousRouter AnonymousRouter {
				get { return _anonRouter; }
			}
		}
	}
}
