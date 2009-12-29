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
using openCrypto.EllipticCurve;
using p2pncs.Net;
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.Anonymous;
using p2pncs.Net.Overlay.DHT;
using p2pncs.Simulation;
using p2pncs.Simulation.VirtualNet;
using p2pncs.Threading;
using p2pncs.Utility;

namespace p2pncs.debug
{
	class Program : IDisposable
	{
		const int NODES = 1024;

		const int InquiryRetries = 2;
		const int InquiryRetryBufferSize = 64;
		const int KeyBytes = 8;
		const int KBRBucketSize = 16;
		static TimeSpan MinimumPingInterval = TimeSpan.FromMinutes (5);
		static Key AppID0 = new Key (new byte[] {0});
		static RandomIPAddressGenerator RndIpGen = new RandomIPAddressGenerator ();

		VirtualNetwork _network;
		IntervalInterrupter _churnInt;
		List<DebugNode> _list = new List<DebugNode> ();
		List<Key> _kbrKeys = new List<Key> ();
		List<IPEndPoint> _eps = new List<IPEndPoint> ();
		HashSet<DebugNode> _selectedNodes = new HashSet<DebugNode> ();
		IPEndPoint[] _init_nodes;
		int _nodeIdx = -1;

		static void Main ()
		{
			using (Program prog = new Program ()) {
				prog.Run ();
			}
		}

		public Program ()
		{
			Interrupters.Start ();
			_churnInt = new IntervalInterrupter (TimeSpan.FromSeconds (500.0 / NODES), "Churn Timer");

			ILatency latency = LatencyTypes.Constant (25);
			IPacketLossRate lossrate = PacketLossType.Constant (0.05);
			_network = new VirtualNetwork (latency, 5, lossrate, Environment.ProcessorCount);
		}

		public void Run ()
		{
			int step = Math.Max (1, NODES / 10);
			for (int i = 0; i < NODES; i++) {
				AddNode ();
				Thread.Sleep (10);
			}
			Console.WriteLine ("{0} Nodes Inserted", NODES);
			lock (_list) {
				_selectedNodes.Add (_list[0]);
				_selectedNodes.Add (_list[1]);
			}
			MCRAggregator mcrAg0 = new MCRAggregator (_list[0].MCRManager, 3, 3, 2, Interrupters.ForMCR, Interrupters.ForMCR, _list[0].SelectRelayNodes);
			MCRAggregator mcrAg1 = new MCRAggregator (_list[1].MCRManager, 3, 3, 2, Interrupters.ForMCR, Interrupters.ForMCR, _list[1].SelectRelayNodes);
			mcrAg0.Received.AddUnknownKeyHandler (delegate (object sender, ReceivedEventArgs e) {
				MCRReceivedEventArgs e2 = (MCRReceivedEventArgs)e;
				string tmp = "";
				for (int i = 0; e2.SrcEndPoints != null && i < e2.SrcEndPoints.Length; i++)
					tmp += e2.SrcEndPoints[i].ToString () + "\r\n";
				Console.WriteLine ("AG0: {0} received from {1} (id={2})", e.Message, tmp, e2.ID);
			});
			mcrAg1.Received.AddUnknownKeyHandler (delegate (object sender, ReceivedEventArgs e) {
				MCRReceivedEventArgs e2 = (MCRReceivedEventArgs)e;
				string tmp = "";
				for (int i = 0; e2.SrcEndPoints != null && i < e2.SrcEndPoints.Length; i++)
					tmp += e2.SrcEndPoints[i].ToString () + "\r\n";
				Console.WriteLine ("AG1: {0} received from {1} (id={2})", e.Message, tmp, e2.ID);
			});

			_churnInt.AddInterruption (delegate () {
				lock (_list) {
					if (_list.Count <= 10)
						return;
					int idx;
					DebugNode removed;
					while (true) {
						idx = ThreadSafeRandom.Next (0, _list.Count);
						removed = _list[idx];
						if (!_selectedNodes.Contains (removed))
							break;
					}
					try {
						removed.Dispose ();
					} catch {}
					_list.RemoveAt (idx);
					_eps.RemoveAt (idx);
					AddNode ();
					GC.Collect ();
				}
			});
			_churnInt.Start ();

			for (int i = 0;; i ++) {
				string line = Console.ReadLine ();
				if (line.Length == 0)
					break;
				switch (i % 4) {
					case 0:
						mcrAg0.SendTo (line, mcrAg1.LocalEndPoint);
						break;
					case 1:
						mcrAg1.SendTo (line, mcrAg0.LocalEndPoint);
						break;
					case 2:
						mcrAg0.SendTo (line, null);
						break;
					case 3:
						mcrAg1.SendTo (line, null);
						break;
				}
			}
		}

		DebugNode AddNode ()
		{
			int nodeIdx = Interlocked.Increment (ref _nodeIdx);
			DebugNode node = new DebugNode (_network);
			if (_init_nodes == null || _init_nodes.Length < 2)
				_init_nodes = _eps.ToArray ();
			if (_init_nodes.Length > 0)
				node.KeyBasedRouter.Join (AppID0, _init_nodes);
			lock (_list) {
				_list.Add (node);
				_eps.Add (node.PublicEndPoint);
				_kbrKeys.Add (node.KeyBasedRouter.RoutingAlgorithm.SelfNodeHandle.NodeID);
			}
			return node;
		}

		public void Dispose ()
		{
			_churnInt.Dispose ();
			Interrupters.Close ();
			_network.Close ();
			lock (_list) {
				for (int i = 0; i < _list.Count; i++) {
					try {
						_list[i].Dispose ();
					} catch {}
				}
			}
		}

		sealed class DebugNode : IDisposable
		{
			IPEndPoint _pubEP;
			VirtualUdpSocket _udpSock;
			InquirySocket _sock;
			SimpleRoutingAlgorithm _algo;
			SimpleIterativeRouter _router;
			OnMemoryStore _localStore;
			SimpleDHT _dht;
			ECKeyPair _keyPair;
			MCRManager _mcrMgr;
			bool _disposed = false;

			public DebugNode (VirtualNetwork network)
			{
				_pubEP = new IPEndPoint (RndIpGen.Next (), ThreadSafeRandom.Next (1, ushort.MaxValue));
				_keyPair = ECKeyPair.Create (ConstantParameters.ECDomainName);
				NodeHandle nodeHandle = new NodeHandle (Key.Create (_keyPair), _pubEP);
				_udpSock = new VirtualUdpSocket (network, _pubEP.Address, true);
				IRTOAlgorithm rto = new RFC2988BasedRTOCalculator (TimeSpan.FromSeconds (1), TimeSpan.FromMilliseconds (200), 50);
				_sock = new InquirySocket (_udpSock, true, Interrupters.ForMessaging, rto, InquiryRetries, InquiryRetryBufferSize);
				_algo = new SimpleRoutingAlgorithm (nodeHandle.NodeID, _sock, KBRBucketSize, MinimumPingInterval);
				_router = new SimpleIterativeRouter (_algo, _sock);
				ValueTypeRegister typeReg = new ValueTypeRegister ();
				_localStore = new OnMemoryStore (typeReg, Interrupters.ForDHT);
				_dht = new SimpleDHT (_sock, _router, _localStore, typeReg);
				_mcrMgr = new MCRManager (_sock, _keyPair, Interrupters.ForMCR);
				typeReg.Register (typeof (string), 0, new EqualityValueMerger<string> ());
				_algo.NewApp (AppID0);
				Interrupters.ForKBR.AddInterruption (Stabilize);
				_udpSock.Bind (new IPEndPoint (IPAddress.Any, _pubEP.Port));
				_mcrMgr.Received.Add (typeof (string), delegate (object sender, MCRTerminalNodeReceivedEventArgs e) {
					Console.WriteLine ("T:{0} received {1}", nodeHandle.NodeID.ToShortString (), e.Message);
					e.Send (e.Message.ToString () + "#RESPONSE", true);
				});
				_mcrMgr.InquiryFailed += delegate (object sender, MCRManager.FailedEventArgs e) {
					_algo.Fail (e.EndPoint);
				};
			}

			void Stabilize ()
			{
				_algo.Stabilize (AppID0);
			}

			public NodeHandle[] SelectRelayNodes (int maxNum)
			{
				return _algo.GetRandomNodes (AppID0, maxNum);
			}

			public IPEndPoint PublicEndPoint {
				get { return _pubEP; }
			}

			public IKeyBasedRouter KeyBasedRouter {
				get { return _router; }
			}

			public MCRManager MCRManager {
				get { return _mcrMgr; }
			}

			public bool IsDisposed {
				get { return _disposed; }
			}

			public void Dispose ()
			{
				_disposed = true;
				Interrupters.ForKBR.RemoveInterruption (Stabilize);
				_mcrMgr.Dispose ();
				_dht.Dispose ();
				_localStore.Close ();
				_router.Close ();
				_algo.Close ();
				_sock.Dispose ();
				_udpSock.Dispose ();
			}
		}

		static class Interrupters
		{
			static IntervalInterrupter _messaging = new IntervalInterrupter (TimeSpan.FromMilliseconds (100), "Messaging Retry Timer");
			static IntervalInterrupter _kbr = new IntervalInterrupter (TimeSpan.FromSeconds (30), "KBR Stabilize Timer");
			static IntervalInterrupter _dht = new IntervalInterrupter (TimeSpan.FromSeconds (5), "DHT Expire Check Timer");
			static IntervalInterrupter _mcr = new IntervalInterrupter (TimeSpan.FromSeconds (1), "MCR Timeout Timer");

			public static void Start ()
			{
				_messaging.Start ();
				_dht.Start ();
				_mcr.Start ();
			}

			public static void Close ()
			{
				_messaging.Dispose ();
				_dht.Dispose ();
				_mcr.Dispose ();
			}

			public static IntervalInterrupter ForMessaging {
				get { return _messaging; }
			}

			public static IntervalInterrupter ForKBR {
				get { return _kbr; }
			}

			public static IntervalInterrupter ForDHT {
				get { return _dht; }
			}

			public static IntervalInterrupter ForMCR {
				get { return _mcr; }
			}
		}
	}
}
