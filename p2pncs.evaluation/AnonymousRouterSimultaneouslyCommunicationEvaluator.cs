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
using System.Threading;
using openCrypto.EllipticCurve;
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.Anonymous;
using p2pncs.Utility;

namespace p2pncs.Evaluation
{
	class AnonymousRouterSimultaneouslyCommunicationEvaluator : IEvaluator
	{
		int _tests = 0;
		int _success_count = 0;
		int _connecting = 0;
		AutoResetEvent _connectingDone = new AutoResetEvent (true);
		StandardDeviation _rtt_sd = new StandardDeviation (false);

		public void Evaluate (EvalOptionSet opt)
		{
			// 強制的にchurn=0, packet-loss=0を設定
			opt.ChurnInterval = 0;
			opt.PacketLossRate = 0.0;

			// 総コネクション数 = EvalOptionSet.Tests
			int connections = opt.Tests;

			Random rnd = new Random ();
			using (EvalEnvironment env = new EvalEnvironment (opt)) {
				env.AddNodes (opt.NumberOfNodes, true);
				List<Info> subscribedList = new List<Info> ();
				HashSet<Key> aliveRecipients = new HashSet<Key> ();
				Dictionary<VirtualNode, Info> mapping = new Dictionary<VirtualNode,Info> ();
				int simultaneouslyProcess = 32;
				int px, py;

				Console.Write ("Subscribe ALL Nodes...");
				px = Console.CursorLeft;
				py = Console.CursorTop;
				for (int i = 0; i < opt.NumberOfNodes; i++) {
					_connectingDone.WaitOne ();
					if (Interlocked.Increment (ref _connecting) < simultaneouslyProcess)
						_connectingDone.Set ();

					Info info = new Info (env.Nodes[i]);
					aliveRecipients.Add (info.PublicKey);
					mapping.Add (env.Nodes[i], info);
					subscribedList.Add (info);
					ThreadPool.QueueUserWorkItem (SubscribeWait_Thread, info);
					Console.CursorLeft = px;
					Console.CursorTop = py;
					Console.Write ("{0}/{1}", i, opt.NumberOfNodes);
				}

				Console.CursorLeft = px;
				Console.CursorTop = py;
				Console.Write ("waiting...{0}", new string (' ', Console.WindowWidth - px - 11));
				while (true) {
					_connectingDone.WaitOne ();
					if (Interlocked.Add (ref _connecting, 0) == 0)
						break;
				}
				Console.CursorLeft = px;
				Console.CursorTop = py;
				Console.WriteLine ("ok{0}", new string (' ', Console.WindowWidth - px - 3));

				Console.Write ("Establishing ALL Connection...");
				_connectingDone.Set ();
				px = Console.CursorLeft;
				py = Console.CursorTop;
				for (int i = 0; i < connections; i++) {
					_connectingDone.WaitOne ();
					if (Interlocked.Increment (ref _connecting) < simultaneouslyProcess)
						_connectingDone.Set ();

					Info info = subscribedList[rnd.Next (subscribedList.Count)];
					Info destInfo;
					while (true) {
						int idx = rnd.Next (subscribedList.Count);
						destInfo = subscribedList[idx];
						info.TempDest = destInfo.PublicKey;
						if (destInfo != info)
							break;
					}
					ThreadPool.QueueUserWorkItem (EstablishConnect_Thread, new object[] {info, destInfo});
					Console.CursorLeft = px;
					Console.CursorTop = py;
					Console.Write ("{0}/{1}", i, connections);
				}
				Console.CursorLeft = px;
				Console.CursorTop = py;
				Console.Write ("waiting...{0}", new string (' ', Console.WindowWidth - px - 11));
				while (true) {
					_connectingDone.WaitOne ();
					if (Interlocked.Add (ref _connecting, 0) == 0)
						break;
				}
				Console.CursorLeft = px;
				Console.CursorTop = py;
				Console.WriteLine ("ok{0}", new string (' ', Console.WindowWidth - px - 3));

				Console.WriteLine ("Start");
				long lastPackets = env.Network.Packets;
				do {
					for (int i = 0; i < subscribedList.Count; i ++) {
						IList<AnonymousSocketInfo> list;
						lock (subscribedList[i].Node.AnonymousSocketInfoList) {
							list = new List<AnonymousSocketInfo> (subscribedList[i].Node.AnonymousSocketInfoList);
						}
						if (list.Count == 0) continue;
						string msg = "Hello-" + DateTime.Now.Ticks.ToString () + "-" + subscribedList[i].PublicKey.ToString ();
						for (int k = 0; k < list.Count; k ++) {
							list[k].MessagingSocket.BeginInquire (msg, AnonymousRouter.DummyEndPoint, Messaging_Callback,
								new object[] {subscribedList[i], list[k], msg, Stopwatch.StartNew ()});
						}
						Thread.Sleep (50);
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
				} while (false);
			}
		}

		void SubscribeWait_Thread (object o)
		{
			Info info = (Info)o;
			while (true) {
				Thread.Sleep (1);
				if (info.SubscribeInfo.Status == SubscribeRouteStatus.Stable)
					break;
			}
			Interlocked.Decrement (ref _connecting);
			_connectingDone.Set ();
		}

		void EstablishConnect_Thread (object o)
		{
			object[] objects = (object[])o;
			Info info = (Info)objects[0];
			Info destInfo = (Info)objects[1];
			while (true) {
				IAsyncResult ar = info.Node.AnonymousRouter.BeginConnect (info.PublicKey, info.TempDest, AnonymousConnectionType.LowLatency, null, null, null);
				try {
					IAnonymousSocket sock = info.Node.AnonymousRouter.EndConnect (ar);
					info.Node.CreateAnonymousSocket (sock);
					break;
				} catch {
					lock (destInfo.Node.AnonymousSocketInfoList) {
						for (int k = 0; k < destInfo.Node.AnonymousSocketInfoList.Count; k++) {
							if (!info.PublicKey.Equals (destInfo.Node.AnonymousSocketInfoList[k].BaseSocket.RemoteEndPoint)) continue;
							destInfo.Node.AnonymousSocketInfoList[k].MessagingSocket.Close ();
							destInfo.Node.AnonymousSocketInfoList.RemoveAt (k);
							break;
						}
					}
				}
				Debug.WriteLine (string.Format ("Retry {0} to {1}", info.PublicKey, destInfo.PublicKey));
			}
			Interlocked.Decrement (ref _connecting);
			_connectingDone.Set ();
		}

		void Messaging_Callback (IAsyncResult ar)
		{
			object[] status = (object[])ar.AsyncState;
			Info info = (Info)status[0];
			AnonymousSocketInfo ainfo = (AnonymousSocketInfo)status[1];
			string msg = (string)status[2];
			Stopwatch sw = (Stopwatch)status[3];
			msg += "-" + ainfo.BaseSocket.RemoteEndPoint.ToString () + "-ok";
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
				Node = node;
				SubscribeInfo = node.Subscribe ();
				PrivateKey = SubscribeInfo.PrivateKey;
				PublicKey = SubscribeInfo.Key;
			}

			public ECKeyPair PrivateKey { get; set; }
			public Key PublicKey { get; set; }
			public VirtualNode Node { get; set; }
			public ISubscribeInfo SubscribeInfo { get; set; }

			public Key TempDest { get; set; }
		}
	}
}
