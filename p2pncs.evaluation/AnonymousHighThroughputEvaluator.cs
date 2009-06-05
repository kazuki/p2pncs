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
using p2pncs.Net;
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.Anonymous;
using p2pncs.Simulation;

namespace p2pncs.Evaluation
{
	class AnonymousHighThroughputEvaluator : IEvaluator
	{
		public void Evaluate (EvalOptionSet opt)
		{
			using (EvalEnvironment env = new EvalEnvironment (opt)) {
				env.AddNodes (opt.NumberOfNodes, true);
				env.StartChurn ();

				ECKeyPair privateKey1 = ECKeyPair.Create (VirtualNode.DefaultECDomain);
				Key recipientKey1 = Key.Create (privateKey1);
				ECKeyPair privateKey2 = ECKeyPair.Create (VirtualNode.DefaultECDomain);
				Key recipientKey2 = Key.Create (privateKey2);

				env.Nodes[0].AnonymousRouter.SubscribeRecipient (recipientKey1, privateKey1);
				env.Nodes[0].SubscribeKey = recipientKey1.ToString ();
				ISubscribeInfo subscribeInfo1 = env.Nodes[0].AnonymousRouter.GetSubscribeInfo (recipientKey1);
				env.Nodes[1].AnonymousRouter.SubscribeRecipient (recipientKey2, privateKey2);
				env.Nodes[1].SubscribeKey = recipientKey2.ToString ();
				ISubscribeInfo subscribeInfo2 = env.Nodes[1].AnonymousRouter.GetSubscribeInfo (recipientKey2);

				while (true) {
					if (subscribeInfo1.Status == SubscribeRouteStatus.Stable && subscribeInfo2.Status == SubscribeRouteStatus.Stable)
						break;
					Thread.Sleep (10);
				}

				bool routeEstablished = false;
				IAnonymousSocket sock1 = null, sock2 = null;
				do {
					IMessagingSocket msock1 = null, msock2 = null;
					int windowSize = 1024, sequence = 0, packets = 10000;
					HashSet<int> ackWaiting = new HashSet<int> ();
					ManualResetEvent done = new ManualResetEvent (true);
					StandardDeviation sd = new StandardDeviation (false);
					try {
						IAsyncResult ar = env.Nodes[0].AnonymousRouter.BeginConnect (recipientKey1, recipientKey2, AnonymousConnectionType.HighThroughput, null, null, null);
						sock1 = env.Nodes[0].AnonymousRouter.EndConnect (ar);
						if (env.Nodes[1].AnonymousSocketInfoList.Count == 0)
							throw new System.Net.Sockets.SocketException ();
						routeEstablished = true;
						sock2 = env.Nodes[1].AnonymousSocketInfoList[0].BaseSocket;
						msock1 = env.CreateMessagingSocket (sock1, TimeSpan.FromSeconds (1), 16, windowSize * 2, 0);
						msock2 = env.CreateMessagingSocket (sock2, TimeSpan.FromSeconds (1), 16, windowSize * 2, 0);
						msock2.InquiredUnknownMessage += delegate (object sender, InquiredEventArgs e) {
							msock2.StartResponse (e, e.InquireMessage);
						};
						sock1.InitializedEventHandlers ();
						sock2.InitializedEventHandlers ();
						Stopwatch sw = Stopwatch.StartNew ();
						for (int i = 0; i < packets; i ++) {
							done.WaitOne ();
							int current = sequence ++;
							lock (ackWaiting) {
								ackWaiting.Add (current);
								if (ackWaiting.Count >= windowSize)
									done.Reset ();
							}
							msock1.BeginInquire (current, AnonymousRouter.DummyEndPoint, delegate (IAsyncResult ar2) {
								Stopwatch sw2 = ar2.AsyncState as Stopwatch;
								object ret = msock1.EndInquire (ar2);
								sw2.Stop ();
								if (ret == null) {
									Console.WriteLine ("Timeout...");
									throw new System.Net.Sockets.SocketException ();
								}
								int value = (int)ret;
								lock (ackWaiting) {
									sd.AddSample ((float)sw2.Elapsed.TotalMilliseconds);
									ackWaiting.Remove (value);
									if (ackWaiting.Count < windowSize)
										done.Set ();
								}
							}, Stopwatch.StartNew ());
						}
						while (true) {
							done.WaitOne ();
							lock (ackWaiting) {
								if (ackWaiting.Count == 0)
									break;
								done.Reset ();
							}
						}
						sw.Stop ();
						int minJitter, maxJitter;
						double avgJitter, sdJitter, avgRtt, sdRtt;
						env.Network.GetAndResetJitterHistory (out minJitter, out avgJitter, out sdJitter, out maxJitter);
						avgRtt = sd.Average;
						sdRtt = sd.ComputeStandardDeviation ();
						Logger.Log (LogLevel.Info, this, "{0:f1}sec, {1:f2}Mbps", sw.Elapsed.TotalSeconds, 800 * packets * 8 / sw.Elapsed.TotalSeconds / 1000.0 / 1000.0);
						Logger.Log (LogLevel.Info, this, "Jitter={0}/{1:f1}({2:f1})/{3}, RTT={4:f1}({5:f1})", minJitter, avgJitter, sdJitter, maxJitter, avgRtt, sdRtt);
					} catch {
					} finally {
						if (sock1 != null) sock1.Dispose ();
						if (sock2 != null) sock2.Dispose ();
					}
				} while (!routeEstablished);
			}
		}
	}
}
