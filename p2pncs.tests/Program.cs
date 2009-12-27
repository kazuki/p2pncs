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

#define LOOPBACK

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using p2pncs.Net;
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.Anonymous;
using p2pncs.Net.Overlay.DHT;
using p2pncs.Simulation;
using p2pncs.Simulation.VirtualNet;
using p2pncs.Threading;
using p2pncs.Utility;
using openCrypto.EllipticCurve;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace p2pncs
{
	class Program
	{
		static void Main (string[] args)
		{
			IntervalInterrupter interrupter = new IntervalInterrupter (TimeSpan.FromMilliseconds (50), "InquirySocket TimeoutCheck");
			IntervalInterrupter dhtExpireCheckInt = new IntervalInterrupter (TimeSpan.FromSeconds (1), "DHT TimeoutCheck");
			IntervalInterrupter mcrTimeoutCheckInt = new IntervalInterrupter (TimeSpan.FromSeconds (1), "MCR TimeoutCheck");
			IRTOAlgorithm rto = new RFC2988BasedRTOCalculator (TimeSpan.FromSeconds (1), TimeSpan.FromMilliseconds (200), 50);
			RandomIPAddressGenerator rndIpGen = new RandomIPAddressGenerator ();
			bool bypassSerialize = true;
			const int Nodes = 1 << 6;
			const int Retries = 2;
			const int RetryBufferSize = 64;
			const int KeyBytes = 8;
			const int BucketSize = 16;
			TimeSpan minPingInterval = TimeSpan.FromMinutes (5);
			Key appId = new Key (new byte[] {0});

			interrupter.Start ();
			dhtExpireCheckInt.Start ();
			mcrTimeoutCheckInt.Start ();

			using (VirtualNetwork vnet = new VirtualNetwork (LatencyTypes.Constant (10), 5, PacketLossType.Lossless, Environment.ProcessorCount)) {
				List<IPEndPoint> endPoints = new List<IPEndPoint> ();
				List<IInquirySocket> sockets = new List<IInquirySocket> ();
				List<IKeyBasedRouter> routers = new List<IKeyBasedRouter> ();
				List<IDistributedHashTable> dhts = new List<IDistributedHashTable> ();
				List<NodeHandle> nodeHandles = new List<NodeHandle> ();
				List<MCRManager> mcrMgrs = new List<MCRManager> ();
				for (int i = 0; i < Nodes; i ++) {
					IPEndPoint ep = new IPEndPoint (rndIpGen.Next (), ThreadSafeRandom.Next (1, ushort.MaxValue));
					VirtualUdpSocket udpSock = new VirtualUdpSocket (vnet, ep.Address, bypassSerialize);
					InquirySocket sock = new InquirySocket (udpSock, true, interrupter, rto, Retries, RetryBufferSize);
					Key key = Key.CreateRandom (KeyBytes);
					SimpleRoutingAlgorithm algo = new SimpleRoutingAlgorithm (key, sock, BucketSize, minPingInterval);
					SimpleIterativeRouter router = new SimpleIterativeRouter (algo, sock);
					ValueTypeRegister typeReg = new ValueTypeRegister ();
					OnMemoryStore localStore = new OnMemoryStore (typeReg, dhtExpireCheckInt);
					SimpleDHT dht = new SimpleDHT (sock, router, localStore, typeReg);
					ECKeyPair keyPair = ECKeyPair.Create (ConstantParameters.ECDomainName);
					MCRManager mcrMgr = new MCRManager (sock, keyPair, mcrTimeoutCheckInt);
					NodeHandle nodeHandle = new NodeHandle (Key.Create (keyPair), ep);
					typeReg.Register (typeof (string), 0, new EqualityValueMerger<string> ());
					algo.NewApp (appId);
					udpSock.Bind (new IPEndPoint (IPAddress.Any, ep.Port));
					mcrMgr.Received.Add (typeof (string), delegate (object sender, MCRTerminalNodeReceivedEventArgs e) {
						Console.WriteLine ("T:{0} received {1}", nodeHandle.NodeID.ToShortString (), e.Message);
						e.Send (e.Message.ToString () + "#RESPONSE", true);
					});

					endPoints.Add (ep);
					sockets.Add (sock);
					routers.Add (router);
					dhts.Add (dht);
					mcrMgrs.Add (mcrMgr);
					nodeHandles.Add (nodeHandle);
					if (routers.Count > 1)
						router.Join (appId, new EndPoint[] {endPoints[0]});
					Thread.Sleep (5);
					if ((i & 0xff) == 0xff) {
						Console.WriteLine ("{0:p}", (i + 1) / ((double)Nodes));
						GC.Collect ();

						int minJitter, maxJitter;
						double avgJitter, sdJitter;
						vnet.GetAndResetJitterHistory (out minJitter, out avgJitter, out sdJitter, out maxJitter);
						Console.WriteLine ("{0}/{1:f2}({2:f2})/{3}", minJitter, avgJitter, sdJitter, maxJitter);
					}
				}
				Console.WriteLine ("OK");
				//Console.ReadLine ();

				int idx0 = 0;
				int idx1 = 1;
				NodeHandle[] relays0 = nodeHandles.ToArray ().RandomSelection (3, idx0);
				NodeHandle[] relays1 = nodeHandles.ToArray ().RandomSelection (3, idx1);
				MCRSocket mcrSock0 = new MCRSocket (mcrMgrs[idx0], true);
				MCRSocket mcrSock1 = new MCRSocket (mcrMgrs[idx1], true);
				mcrSock0.Bind (new MCRBindEndPoint (relays0));
				mcrSock1.Bind (new MCRBindEndPoint (relays1));
				mcrSock0.Binded += delegate (object sender, EventArgs e) {
					Console.WriteLine ("Binded#0 ({0})", mcrSock0.LocalEndPoint);
				};
				mcrSock1.Binded += delegate (object sender, EventArgs e) {
					Console.WriteLine ("Binded#1 ({0})", mcrSock1.LocalEndPoint);
				};
				mcrSock0.Disconnected += delegate (object sender, EventArgs e) {
					Console.WriteLine ("Disconnected#0");
				};
				mcrSock1.Disconnected += delegate (object sender, EventArgs e) {
					Console.WriteLine ("Disconnected#1");
				};
				while (!mcrSock0.IsBinded || !mcrSock1.IsBinded)
					Thread.Sleep (10);
				mcrSock0.Received.Add (typeof (string), delegate (object sender, ReceivedEventArgs e) {
					Console.WriteLine ("{0}: Received {1} from {2}",
						nodeHandles[idx0].NodeID.ToShortString (), e.Message, e.RemoteEndPoint);
				});
				mcrSock1.Received.Add (typeof (string), delegate (object sender, ReceivedEventArgs e) {
					MCRReceivedEventArgs e2 = (MCRReceivedEventArgs)e;
					Console.WriteLine ("{0}: Received {1} from {2}",
						nodeHandles[idx1].NodeID.ToShortString (), e.Message, e.RemoteEndPoint);
					if (e.RemoteEndPoint != null)
						mcrSock1.SendTo ("WORLD!" + (e2.IsReliableMode ? "" : " with Unreliable"), e.RemoteEndPoint);
				});
				mcrSock0.SendTo ("HOGE", null);
				mcrSock1.SendTo ("foo", null);
				mcrSock0.SendTo ("HELLO!", mcrSock1.LocalEndPoint);
				Thread.Sleep (100);
				mcrSock0.IsReliableMode = false;
				mcrSock0.SendTo ("HELLO! with Unreliable", mcrSock1.LocalEndPoint);
				Console.ReadLine ();

				/*Key key2 = Key.CreateRandom (KeyBytes);
				while (true) {
					int idx0 = rnd.Next (0, sockets.Count);
					int idx1 = rnd.Next (0, sockets.Count);
					dhts[idx0].EndPut (dhts[idx0].BeginPut (appId, key2, TimeSpan.FromHours (1), "Hello from " + routers[idx0].RoutingAlgorithm.SelfNodeHandle.NodeID.ToString (16), null, null));
					GetResult<string> result = dhts[idx1].EndGet<string> (dhts[idx1].BeginGet<string> (appId, key2, null, null, null));
					Console.WriteLine ("Result({0}hops):", result.Hops);
					for (int i = 0; result.Values != null && i < result.Values.Length; i ++)
						Console.WriteLine ("  {0}", result.Values[i]);
					Console.ReadLine ();
				}*/

				/*while (true) {
					int idx0 = rnd.Next (0, sockets.Count);
					int idx1 = rnd.Next (0, sockets.Count);
					Console.WriteLine ("{0} looking for {1}",
						routers[idx0].RoutingAlgorithm.SelfNodeHandle.NodeID,
						routers[idx1].RoutingAlgorithm.SelfNodeHandle.NodeID);
					IAsyncResult ar = routers[idx0].BeginRoute (appId, routers[idx1].RoutingAlgorithm.SelfNodeHandle.NodeID, 3,
						new KeyBasedRoutingOptions {RoutingFinishedMatchBits=8}, null, null);
					RoutingResult rr = routers[idx0].EndRoute (ar);
					Console.WriteLine ("Match First 8bit. ({0}hops)", rr.Hops);
					for (int i = 0; i < rr.RootCandidates.Length; i ++)
						Console.WriteLine ("{0}: {1}", i + 1, rr.RootCandidates[i].NodeID);

					ar = routers[idx0].BeginRoute (appId, routers[idx1].RoutingAlgorithm.SelfNodeHandle.NodeID, 3, null, null, null);
					rr = routers[idx0].EndRoute (ar);
					Console.WriteLine ("Nearst. ({0}hops)", rr.Hops);
					for (int i = 0; i < rr.RootCandidates.Length; i++)
						Console.WriteLine ("{0}: {1}", i + 1, rr.RootCandidates[i].NodeID);
					if (Console.ReadLine () == "exit")
						break;
				}*/
			}

			interrupter.Dispose ();
			dhtExpireCheckInt.Dispose ();
			mcrTimeoutCheckInt.Dispose ();
		}

		static void Main1 (string[] args)
		{
			IntervalInterrupter interrupter = new IntervalInterrupter (TimeSpan.FromSeconds (1), "InquirySocket TimeoutCheck");
			IRTOAlgorithm rto = new RFC2988BasedRTOCalculator (TimeSpan.FromSeconds (1), TimeSpan.FromMilliseconds (200), 50);
			interrupter.Start ();

			IPEndPoint ep1, ep2;
#if LOOPBACK
			ep1 = new IPEndPoint (IPAddress.Loopback, 8080);
			ep2 = new IPEndPoint (IPAddress.Loopback, 8081);
#else
			RandomIPAddressGenerator rndIpGen = new RandomIPAddressGenerator ();
			bool bypassSerialize = false;
			ep1 = new IPEndPoint (rndIpGen.Next (), 8080);
			ep2 = new IPEndPoint (rndIpGen.Next (), 8081);
#endif

#if LOOPBACK
			using (UdpSocket sock1 = UdpSocket.CreateIPv4 ())
			using (UdpSocket sock2 = UdpSocket.CreateIPv4 ())
#else
			using (VirtualNetwork vnet = new VirtualNetwork (LatencyTypes.Constant (100), 5, PacketLossType.Lossless (), 1))
			using (VirtualUdpSocket sock1 = new VirtualUdpSocket (vnet, ep1.Address, bypassSerialize))
			using (VirtualUdpSocket sock2 = new VirtualUdpSocket (vnet, ep2.Address, bypassSerialize))
#endif
			using (InquirySocket isock1 = new InquirySocket (sock1, true, interrupter, rto, 3, 64))
			using (InquirySocket isock2 = new InquirySocket (sock2, true, interrupter, rto, 3, 64)) {
				sock1.Bind (ep1);
				sock2.Bind (ep2);
				sock1.SendTo ("hoge1", ep2);
				sock1.SendTo ("hoge2", ep2);
				sock1.SendTo ("hoge3", ep2);
				sock1.SendTo ("hoge4", ep2);
				sock2.Received.Add (typeof (string), delegate (object sender, ReceivedEventArgs e) {
					Console.WriteLine ("{0} from {1}", e.Message, e.RemoteEndPoint);
				});
				Console.ReadLine ();

				isock2.Inquired.Add (typeof (string), delegate (object sender, InquiredEventArgs e) {
					Console.WriteLine ("Inquired {0} from {1}", e.InquireMessage, e.EndPoint);
					isock2.RespondToInquiry (e, "WORLD !");
				});
				isock1.BeginInquire ("HELLO", ep2, delegate (IAsyncResult ar) {
					Console.WriteLine ("{0} from {1}", isock1.EndInquire (ar), ep2);
				}, null);
				Console.ReadLine ();
			}

			interrupter.Dispose ();
		}
	}
}
