﻿/*
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
using p2pncs.Net;
using p2pncs.Net.Overlay.Anonymous;
using p2pncs.Utility;

namespace p2pncs.Evaluation
{
	class AnonymousRouterEvaluation : IEvaluator
	{
		public void Evaluate (EvalOptionSet opt)
		{
			using (EvalEnvironment env = new EvalEnvironment (opt)) {
				env.AddNodes (opt.NumberOfNodes, true);

				ISubscribeInfo subscribeInfo1 = env.Nodes[0].Subscribe ();
				ISubscribeInfo subscribeInfo2 = env.Nodes[1].Subscribe ();
				string strKey1 = subscribeInfo1.Key.ToString ();
				string strKey2 = subscribeInfo2.Key.ToString ();

				IMessagingSocket msock1 = null, msock2 = null;

				while (true) {
					if (subscribeInfo1.Status == SubscribeRouteStatus.Stable && subscribeInfo2.Status == SubscribeRouteStatus.Stable)
						break;
					Thread.Sleep (10);
				}

				env.StartChurn ();
				bool routeEstablished = false;
				do {
					try {
						IAsyncResult ar = env.Nodes[0].AnonymousRouter.BeginConnect (subscribeInfo1.Key, subscribeInfo2.Key, AnonymousConnectionType.LowLatency, null, null, null);
						Stopwatch sw = Stopwatch.StartNew ();
						IAnonymousSocket sock1 = env.Nodes[0].AnonymousRouter.EndConnect (ar);
						sw.Stop ();
						if (env.Nodes[1].AnonymousSocketInfoList.Count == 0) {
							throw new System.Net.Sockets.SocketException ();
						}
						Logger.Log (LogLevel.Info, this, "Connected: {0}ms", sw.ElapsedMilliseconds);

						msock1 = env.Nodes[0].CreateAnonymousSocket (sock1);
						msock2 = env.Nodes[1].AnonymousSocketInfoList[env.Nodes[1].AnonymousSocketInfoList.Count - 1].MessagingSocket;
						routeEstablished = true;

						DateTime dt = DateTime.Now;
						StandardDeviation rtt_sd = new StandardDeviation (false);
						int tests = 0, success = 0;
						for (int i = 0; i < opt.Tests; i ++) {
							string ret;

							tests ++;
							string msg = "Hello-" + strKey1 + "-" + i.ToString ();
							sw.Reset (); sw.Start ();
							ar = msock1.BeginInquire (msg, AnonymousRouter.DummyEndPoint, null, null);
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
							ar = msock2.BeginInquire (msg, AnonymousRouter.DummyEndPoint, null, null);
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
						Logger.Log (LogLevel.Info, this, "Establish Failed. Retry...");
						return;
					} finally {
						if (msock1 != null) msock1.Dispose ();
						if (msock2 != null) msock2.Dispose ();
					}
				} while (!routeEstablished);
			}
		}
	}
}