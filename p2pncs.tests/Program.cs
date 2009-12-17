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
using p2pncs.Simulation;
using p2pncs.Simulation.VirtualNet;
using p2pncs.Threading;

namespace p2pncs
{
	class Program
	{
		static void Main (string[] args)
		{
			IntervalInterrupter interrupter = new IntervalInterrupter (TimeSpan.FromSeconds (1), "InquirySocket TimeoutCheck");
			IRTOAlgorithm rto = new RFC2988BasedRTOCalculator (TimeSpan.FromSeconds (1), TimeSpan.FromMilliseconds (200), 50);
			RandomIPAddressGenerator rndIpGen = new RandomIPAddressGenerator ();
			Random rnd = new Random ();
			bool bypassSerialize = true;
			const int Nodes = 1 << 16;
			const int Retries = 2;
			const int RetryBufferSize = 64;
			const int KeyBytes = 8;
			const int BucketSize = 1;
			TimeSpan minPingInterval = TimeSpan.FromMinutes (5);
			Key appId = new Key (new byte[] {0});

			interrupter.Start ();

			using (VirtualNetwork vnet = new VirtualNetwork (LatencyTypes.Constant (10), 5, PacketLossType.Lossless (), Environment.ProcessorCount)) {
				List<IPEndPoint> endPoints = new List<IPEndPoint> ();
				List<InquirySocket> sockets = new List<InquirySocket> ();
				List<SimpleIterativeRouter> routers = new List<SimpleIterativeRouter> ();
				for (int i = 0; i < Nodes; i ++) {
					IPEndPoint ep = new IPEndPoint (rndIpGen.Next (), rnd.Next (1, ushort.MaxValue));
					VirtualUdpSocket udpSock = new VirtualUdpSocket (vnet, ep.Address, bypassSerialize);
					InquirySocket sock = new InquirySocket (udpSock, true, interrupter, rto, Retries, RetryBufferSize);
					Key key = Key.CreateRandom (KeyBytes);
					SimpleRoutingAlgorithm algo = new SimpleRoutingAlgorithm (key, sock, BucketSize, minPingInterval);
					SimpleIterativeRouter router = new SimpleIterativeRouter (algo, sock);
					algo.NewApp (appId);
					udpSock.Bind (new IPEndPoint (IPAddress.Any, ep.Port));

					endPoints.Add (ep);
					sockets.Add (sock);
					routers.Add (router);
					if (routers.Count > 1)
						router.Join (appId, new EndPoint[] {endPoints[0]});
					Thread.Sleep (5);
					if ((i & 0xff) == 0xff) {
						Console.WriteLine ("{0:p}", (i + 1) / ((double)Nodes));
						GC.Collect ();
					}
				}
				Console.WriteLine ("OK");
				Console.ReadLine ();

				while (true) {
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
				}
			}

			interrupter.Dispose ();
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
