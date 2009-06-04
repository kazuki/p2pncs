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
using System.Diagnostics;
using System.Threading;
using openCrypto.EllipticCurve;
using p2pncs.Net;
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.Anonymous;
using p2pncs.Simulation;

namespace p2pncs.Evaluation
{
	class AnonymousRouterEvaluation : IEvaluator
	{
		public void Evaluate (EvalOptionSet opt)
		{
			using (EvalEnvironment env = new EvalEnvironment (opt)) {
				env.AddNodes (opt, opt.NumberOfNodes, true);
				env.StartChurn (opt);
				Logger.Log (LogLevel.Info, this, "Test ({0})", env.Nodes[0].AnonymousRouter is AnonymousRouter ? "AnonymousRouter" : "AnonymousRouter2");

				ECKeyPair privateKey1 = ECKeyPair.Create (VirtualNode.DefaultECDomain);
				Key recipientKey1 = Key.Create (privateKey1);
				ECKeyPair privateKey2 = ECKeyPair.Create (VirtualNode.DefaultECDomain);
				Key recipientKey2 = Key.Create (privateKey2);
				string strKey1 = recipientKey1.ToString (), strKey2 = recipientKey2.ToString ();

				env.Nodes[0].AnonymousRouter.SubscribeRecipient (recipientKey1, privateKey1);
				env.Nodes[0].SubscribeKey = recipientKey1.ToString ();
				ISubscribeInfo subscribeInfo1 = env.Nodes[0].AnonymousRouter.GetSubscribeInfo (recipientKey1);
				env.Nodes[1].AnonymousRouter.SubscribeRecipient (recipientKey2, privateKey2);
				env.Nodes[1].SubscribeKey = recipientKey2.ToString ();
				ISubscribeInfo subscribeInfo2 = env.Nodes[1].AnonymousRouter.GetSubscribeInfo (recipientKey2);

				DatagramEventSocketWrapper sock1 = new DatagramEventSocketWrapper ();
				IMessagingSocket msock1 = null, msock2 = null;

				while (true) {
					if (subscribeInfo1.Status == SubscribeRouteStatus.Stable && subscribeInfo2.Status == SubscribeRouteStatus.Stable)
						break;
					Thread.Sleep (10);
				}

				bool routeEstablished = false;
				do {
					try {
						sock1.Socket = env.Nodes[0].AnonymousRouter.EndEstablishRoute (env.Nodes[0].AnonymousRouter.BeginEstablishRoute (recipientKey1, recipientKey2, sock1.ReceivedHandler, null, null));
						if (sock1.Socket == null || env.Nodes[1].AnonymousSockets.Count == 0)
							throw new System.Net.Sockets.SocketException ();

						msock1 = env.Nodes[0].CreateAnonymousSocket (sock1);
						msock2 = env.Nodes[1].AnonymousSockets[0];
						routeEstablished = true;

						DateTime dt = DateTime.Now;
						Stopwatch sw = new Stopwatch ();
						StandardDeviation rtt_sd = new StandardDeviation (false);
						int tests = 0, success = 0;
						for (int i = 0; i < opt.Tests; i ++) {
							IAsyncResult ar; string ret;

							tests ++;
							string msg = "Hello-" + strKey1 + "-" + i.ToString ();
							sw.Reset (); sw.Start ();
							ar = msock1.BeginInquire (msg, DatagramEventSocketWrapper.DummyEP, null, null);
							ret = msock1.EndInquire (ar) as string;
							sw.Stop ();
							if (ret == null) {
								Console.Write ("?");
							} else if (ret != msg + "-" + strKey2 + "-ok") {
								Console.Write ("@");
							} else {
								rtt_sd.AddSample ((float)sw.Elapsed.TotalMilliseconds);
								Console.Write ("*");
								success ++;
							}

							tests++;
							msg = "Hello-" + strKey1 + "-" + i.ToString ();
							sw.Reset (); sw.Start ();
							ar = msock2.BeginInquire (msg, DatagramEventSocketWrapper.DummyEP, null, null);
							ret = msock2.EndInquire (ar) as string;
							sw.Stop ();
							if (ret == null) {
								Console.Write ("?");
							} else if (ret != msg + "-" + strKey1 + "-ok") {
								Console.Write ("@");
							} else {
								rtt_sd.AddSample ((float)sw.Elapsed.TotalMilliseconds);
								Console.Write ("=");
								success++;
							}
						}
						Console.WriteLine ();
						int minJitter, maxJitter;
						double avgJitter, sdJitter;
						env.Network.GetAndResetJitterHistory (out minJitter, out avgJitter, out sdJitter, out maxJitter);
						Logger.Log (LogLevel.Info, this, "Time: {0:f2}sec, Jitter: {1}/{2:f1}({3:f1})/{4}, DeliverSuccess={5:p}, RTT: Avg={6:f1}({7:f1})",
							DateTime.Now.Subtract (dt).TotalSeconds, minJitter, avgJitter, sdJitter, maxJitter, (double)success / (double)tests, rtt_sd.Average, rtt_sd.ComputeStandardDeviation ());
					} catch {
						Console.WriteLine ("Establish Failed. Retry...");
					} finally {
						if (msock1 != null) msock1.Dispose ();
						if (msock2 != null) msock2.Dispose ();
						if (sock1 != null) sock1.Dispose ();
						if (env.Nodes[1].AnonymousSockets.Count > 0) {
							lock (env.Nodes[1].AnonymousSockets) {
								while (env.Nodes[1].AnonymousSockets.Count > 0) {
									env.Nodes[1].AnonymousSockets[0].Dispose ();
									env.Nodes[1].AnonymousSockets.RemoveAt (0);
								}
							}
						}
					}
				} while (!routeEstablished);
			}
		}
	}
}
