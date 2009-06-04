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
using System.Diagnostics;
using System.Net;
using System.Threading;
using openCrypto.EllipticCurve;
using p2pncs.Net;
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.Anonymous;
using p2pncs.Security.Cryptography;
using p2pncs.Simulation;
using p2pncs.Simulation.VirtualNet;
using p2pncs.Threading;

namespace p2pncs.Evaluation
{
	class AnonymousRouterSimultaneouslyCommunicationEvaluator : IEvaluator
	{
		int _tests = 0;
		int _success_count = 0;
		StandardDeviation _rtt_sd = new StandardDeviation (false);

		public void Evaluate (EvalOptionSet opt)
		{
			// 強制的にchurn=0, packet-loss=0を設定
			opt.ChurnInterval = 0;
			opt.PacketLossRate = 0.0;

			Random rnd = new Random ();
			using (EvalEnvironment env = new EvalEnvironment (opt)) {
				env.AddNodes (opt, opt.NumberOfNodes, true);
				List<Info> keys = new List<Info> ();
				List<Info> subscribedList = new List<Info> ();
				List<Info> waitingList = new List<Info> ();
				HashSet<Key> aliveRecipients = new HashSet<Key> ();
				Dictionary<VirtualNode, Info> mapping = new Dictionary<VirtualNode,Info> ();

				Console.Write ("Subscribe ALL Nodes...");
				for (int i = 0; i < opt.NumberOfNodes; i++) {
					Info info = new Info (env.Nodes[i]);
					keys.Add (info);
					aliveRecipients.Add (info.PublicKey);
					env.Nodes[i].AnonymousRouter.SubscribeRecipient (info.PublicKey, info.PrivateKey);
					env.Nodes[i].SubscribeKey = info.PublicKey.ToString ();
					info.SubscribeInfo = env.Nodes[i].AnonymousRouter.GetSubscribeInfo (info.PublicKey);
					mapping.Add (env.Nodes[i], info);
					waitingList.Add (info);
				}

				Console.Write ("waiting...");
				while (waitingList.Count > 0) {
					for (int i = 0; i < waitingList.Count; i ++) {
						Info info = waitingList[i];
						if (info.SubscribeInfo.Status != SubscribeRouteStatus.Stable)
							continue;
						waitingList.RemoveAt (i --);
						subscribedList.Add (info);
						if (subscribedList.Count == 1)
							continue;
					}
					Thread.Sleep (0);
				}
				Console.WriteLine ("ok");

				Console.Write ("Establishing ALL Connection...");
				for (int i = 0; i < subscribedList.Count; i ++) {
					Info info = subscribedList[i];
					info.TempSocket = new DatagramEventSocketWrapper ();
					while (true) {
						int idx = rnd.Next (subscribedList.Count);
						info.TempDest = subscribedList[idx].PublicKey;
						if (subscribedList[idx] != info)
							break;
					}
					info.Node.AnonymousRouter.BeginEstablishRoute (info.PublicKey, info.TempDest,
						info.TempSocket.ReceivedHandler, EstablishRoute_Callback, info);
					Thread.Sleep (5);
				}
				Console.Write ("waiting...");
				while (true) {
					bool hasIncompleted = false;
					for (int i = 0; i < subscribedList.Count; i ++) {
						Info info = subscribedList[i];
						if (info.TempSocket.Socket == null) {
							hasIncompleted = true;
							break;
						}
					}
					if (!hasIncompleted)
						break;
				}
				Console.WriteLine ("ok");

				Console.WriteLine ("Start");
				long lastPackets = env.Network.Packets;
				while (true) {
					for (int i = 0; i < subscribedList.Count; i ++) {
						IList<AnonymousSocketInfo> list;
						lock (subscribedList[i].Node.AnonymousSocketInfoList) {
							list = new List<AnonymousSocketInfo> (subscribedList[i].Node.AnonymousSocketInfoList);
						}
						if (list.Count == 0) continue;
						string msg = "Hello-" + DateTime.Now.Ticks.ToString () + "-" + subscribedList[i].PublicKey.ToString ();
						for (int k = 0; k < list.Count; k ++) {
							list[k].MessagingSocket.BeginInquire (msg, DatagramEventSocketWrapper.DummyEP, Messaging_Callback,
								new object[] {subscribedList[i], list[k], msg, Stopwatch.StartNew ()});
						}
					}

					Thread.Sleep (1000);
					long packets = env.Network.Packets;
					lock (Console.Out) {
						Console.WriteLine ();
						int minJitter, maxJitter;
						double avgJitter, sdJitter, avgRtt, sdRtt;
						env.Network.GetAndResetJitterHistory (out minJitter, out avgJitter, out sdJitter, out maxJitter);
						lock (_rtt_sd) {
							avgRtt = _rtt_sd.Average;
							sdRtt = _rtt_sd.ComputeStandardDeviation ();
						}
						Logger.Log (LogLevel.Info, this, "Jitter={0}/{1:f1}({2:f1})/{3}, DeliverSuccess={4:p}, RTT={5:f1}({6:f1}), Packets={7}",
							minJitter, avgJitter, sdJitter, maxJitter, (double)_success_count / (double)_tests, avgRtt, sdRtt, packets - lastPackets);
					}
					lastPackets = packets;
				}
			}
		}

		static void EstablishRoute_Callback (IAsyncResult ar)
		{
			Info info = (Info)ar.AsyncState;
			try {
				info.TempSocket.Socket = info.Node.AnonymousRouter.EndEstablishRoute (ar);
				info.Node.CreateAnonymousSocket (info.TempDest, info.TempSocket);
			} catch {}
		}

		void Messaging_Callback (IAsyncResult ar)
		{
			object[] status = (object[])ar.AsyncState;
			Info info = (Info)status[0];
			AnonymousSocketInfo ainfo = (AnonymousSocketInfo)status[1];
			string msg = (string)status[2];
			Stopwatch sw = (Stopwatch)status[3];
			msg += "-" + ainfo.Destination.ToString () + "-ok";
			string ret = ainfo.MessagingSocket.EndInquire (ar) as string;
			sw.Stop ();
			lock (Console.Out) {
				Interlocked.Increment (ref _tests);
				if (ret == null) {
					Console.Write ("?");
				} else if (ret == msg) {
					Interlocked.Increment (ref _success_count);
					Console.Write ("*");
					Debug.WriteLine (sw.Elapsed);
					lock (_rtt_sd) {
						_rtt_sd.AddSample ((float)sw.Elapsed.TotalMilliseconds);
					}
				} else {
					Console.Write ("@");
				}
			}
		}

		class Info
		{
			public Info (VirtualNode node)
			{
				PrivateKey = ECKeyPair.Create (VirtualNode.DefaultECDomain);
				PublicKey = Key.Create (PrivateKey);
				Node = node;
			}

			public ECKeyPair PrivateKey { get; set; }
			public Key PublicKey { get; set; }
			public VirtualNode Node { get; set; }
			public ISubscribeInfo SubscribeInfo { get; set; }

			public DatagramEventSocketWrapper TempSocket { get; set; }
			public Key TempDest { get; set; }
		}
	}
}
