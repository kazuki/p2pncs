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
	class AnonymousRouterEvaluation : IEvaluator
	{
		public void Evaluate (EvalOptionSet opt)
		{
			VirtualNetwork network = new VirtualNetwork (opt.GetLatency (), 5, opt.GetPacketLossRate (), Environment.ProcessorCount);
			List<VirtualNode> nodes = new List<VirtualNode> ();
			IntervalInterrupter messagingInt = new IntervalInterrupter (TimeSpan.FromMilliseconds (50), "MessagingSocket Interrupter");
			IntervalInterrupter messagingInt2 = new IntervalInterrupter (TimeSpan.FromMilliseconds (50), "AnonymousMessagingSocket Interrupter");
			IntervalInterrupter anonInt = new IntervalInterrupter (TimeSpan.FromMilliseconds (50), "Anonymous Interrupter");
			IntervalInterrupter kbrStabilizeInt = new IntervalInterrupter (TimeSpan.FromSeconds (10), "KBR Stabilize Interrupter");
			IntervalInterrupter dhtInt = new IntervalInterrupter (TimeSpan.FromSeconds (10), "DHT Maintenance Interrupter");
			IntervalInterrupter churn = null;
			Random rnd = new Random ();
			kbrStabilizeInt.LoadEqualizing = true;
			dhtInt.LoadEqualizing = true;
			messagingInt.Start ();
			messagingInt2.Start ();
			anonInt.Start ();
			kbrStabilizeInt.Start ();
			if (opt.ChurnInterval > 0) {
				churn = new IntervalInterrupter (TimeSpan.FromMilliseconds (opt.ChurnInterval), "Churn Interrupter");
				churn.Start ();
			}

			{
				Console.Write ("Setup {0} Nodes...", opt.NumberOfNodes);
				int initEndPoints = 2;
				List<EndPoint> endPoints = new List<EndPoint> (initEndPoints);
				EndPoint[] eps = null;
				int px = Console.CursorLeft, py = Console.CursorTop;
				for (int i = 0; i < opt.NumberOfNodes; i++) {
					if (i % 10 == 0) {
						Console.CursorLeft = px; Console.CursorTop = py;
						Console.Write ("[{0:p0}]", i / (double)opt.NumberOfNodes);
					}
					VirtualNode node = new VirtualNode (network, opt, messagingInt, kbrStabilizeInt, anonInt, dhtInt);
					lock (nodes) {
						nodes.Add (node);
					}
					if (i != 0) {
						if (eps == null || eps.Length < initEndPoints)
							eps = endPoints.ToArray ();
						node.KeyBasedRouter.Join (eps);
					}
					endPoints.Add (node.PublicEndPoint);
					Thread.Sleep (5);
				}
				Console.CursorLeft = px; Console.CursorTop = py;
				Console.Write ("[stabilizing]");
				for (int i = 0; i < opt.NumberOfNodes; i++) {
					nodes[i].KeyBasedRouter.RoutingAlgorithm.Stabilize ();
					Thread.Sleep (5);
				}
				Console.CursorLeft = px; Console.CursorTop = py;
				Console.Write ("[waiting]    ");
				Thread.Sleep (500);
				Console.CursorLeft = px; Console.CursorTop = py;
				Console.WriteLine ("[ok]         ");
			}
			
			Logger.Log (LogLevel.Info, this, "Test ({0})", nodes[0].AnonymousRouter is AnonymousRouter ? "AnonymousRouter" : "AnonymousRouter2");

			if (churn != null) {
				churn.AddInterruption (delegate () {
					int idx = rnd.Next (2, opt.NumberOfNodes - 1);
					VirtualNode node = new VirtualNode (network, opt, messagingInt, kbrStabilizeInt, anonInt, dhtInt);
					lock (nodes) {
						Logger.Log (LogLevel.Trace, this, "Drop-out Node {0}, {1}", nodes[idx].NodeID, nodes[idx].PublicEndPoint);
						nodes[idx].Dispose ();
						nodes.RemoveAt (idx);
						nodes.Add (node);
					}
					node.KeyBasedRouter.Join (new EndPoint[] { nodes[0].PublicEndPoint });
				});
			}

			ManualResetEvent acceptedDone = new ManualResetEvent (false);
			AutoResetEvent receivedDone1 = new AutoResetEvent (false), receivedDone2 = new AutoResetEvent (false);
			try {
				DatagramEventSocketWrapper sock2 = null;
				ECKeyPair privateKey1 = ECKeyPair.Create (VirtualNode.DefaultECDomain);
				Key recipientKey1 = Key.Create (privateKey1);
				ECKeyPair privateKey2 = ECKeyPair.Create (VirtualNode.DefaultECDomain);
				Key recipientKey2 = Key.Create (privateKey2);

				nodes[0].AnonymousRouter.SubscribeRecipient (recipientKey1, privateKey1);
				ISubscribeInfo subscribeInfo1 = nodes[0].AnonymousRouter.GetSubscribeInfo (recipientKey1);

				nodes[1].AnonymousRouter.SubscribeRecipient (recipientKey2, privateKey2);
				nodes[1].AnonymousRouter.Accepting += delegate (object sender, AcceptingEventArgs args) {
					DatagramEventSocketWrapper sock = new DatagramEventSocketWrapper ();
					args.Accept (sock.ReceivedHandler, sock);
				};
				nodes[1].AnonymousRouter.Accepted += delegate (object sender, AcceptedEventArgs args) {
					DatagramEventSocketWrapper sock = (DatagramEventSocketWrapper)args.State;
					sock.Socket = args.Socket;
					sock2 = sock;
					acceptedDone.Set ();
				};
				ISubscribeInfo subscribeInfo2 = nodes[1].AnonymousRouter.GetSubscribeInfo (recipientKey2);

				TimeSpan timeout = TimeSpan.FromSeconds (2);
				int retries = 3, retryBufferSize = 8192, dupCheckSize = 8192;

				DatagramEventSocketWrapper sock1 = new DatagramEventSocketWrapper ();
				IMessagingSocket msock1 = null, msock2 = null;
				sock2 = null;

				while (true) {
					if (subscribeInfo1.Status == SubscribeRouteStatus.Stable && subscribeInfo2.Status == SubscribeRouteStatus.Stable)
						break;
					Thread.Sleep (10);
				}

				do {
					try {
						sock1.Socket = nodes[0].AnonymousRouter.EndEstablishRoute (nodes[0].AnonymousRouter.BeginEstablishRoute (recipientKey1, recipientKey2, sock1.ReceivedHandler, null, null));
						acceptedDone.WaitOne ();

						msock1 = new MessagingSocket (sock1, true, SymmetricKey.NoneKey, Serializer.Instance, null, messagingInt2, timeout, retries, retryBufferSize, dupCheckSize);
						msock2 = new MessagingSocket (sock2, true, SymmetricKey.NoneKey, Serializer.Instance, null, messagingInt2, timeout, retries, retryBufferSize, dupCheckSize);
						msock1.InquiredUnknownMessage += delegate (object sender, InquiredEventArgs e) {
							msock1.StartResponse (e, "OK");
						};
						msock2.InquiredUnknownMessage += delegate (object sender, InquiredEventArgs e) {
							msock2.StartResponse (e, "OK");
						};

						DateTime dt = DateTime.Now;
						Stopwatch sw = new Stopwatch ();
						StandardDeviation rtt_sd = new StandardDeviation (false);
						int tests = 0, success = 0;
						for (int i = 0; i < opt.Tests; i ++) {
							IAsyncResult ar; object ret;

							tests ++;
							sw.Reset (); sw.Start ();
							ar = msock1.BeginInquire ("Hello", DatagramEventSocketWrapper.DummyEP, null, null);
							ret = msock1.EndInquire (ar);
							sw.Stop ();
							if (ret == null) {
								Console.Write ("?");
							} else {
								rtt_sd.AddSample ((float)sw.Elapsed.TotalMilliseconds);
								Console.Write ("*");
								success ++;
							}

							tests++;
							sw.Reset (); sw.Start ();
							ar = msock2.BeginInquire ("Hello", DatagramEventSocketWrapper.DummyEP, null, null);
							ret = msock2.EndInquire (ar);
							sw.Stop ();
							if (ret == null) {
								Console.Write ("?");
							} else {
								rtt_sd.AddSample ((float)sw.Elapsed.TotalMilliseconds);
								Console.Write ("=");
								success++;
							}
						}
						Console.WriteLine ();
						int minJitter, maxJitter;
						double avgJitter, sdJitter;
						network.GetAndResetJitterHistory (out minJitter, out avgJitter, out sdJitter, out maxJitter);
						Logger.Log (LogLevel.Info, this, "Time: {0:f2}sec, Jitter: {1}/{2:f1}({3:f1})/{4}, DeliverSuccess={5:p}, RTT: Avg={6:f1}({7:f1})",
							DateTime.Now.Subtract (dt).TotalSeconds, minJitter, avgJitter, sdJitter, maxJitter, (double)success / (double)tests, rtt_sd.Average, rtt_sd.ComputeStandardDeviation ());
					} catch {
					} finally {
						if (msock1 != null) msock1.Dispose ();
						if (msock2 != null) msock2.Dispose ();
						if (sock1 != null) sock1.Dispose ();
						if (sock2 != null) sock2.Dispose ();
					}
				} while (sock1.Socket == null);
			} catch {
			} finally {
				messagingInt.Dispose ();
				messagingInt2.Dispose ();
				kbrStabilizeInt.Dispose ();
				anonInt.Dispose ();
				if (churn != null)
					churn.Dispose ();
				dhtInt.Dispose ();
				network.Close ();
				for (int i = 0; i < nodes.Count; i++)
					nodes[i].Dispose ();
				acceptedDone.Close ();
				receivedDone1.Close ();
				receivedDone2.Close ();
			}
		}
	}
}
