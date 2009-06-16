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
using p2pncs.Threading;

namespace p2pncs.Evaluation
{
	class AnonymousHighThroughputEvaluator : IEvaluator
	{
		public void Evaluate (EvalOptionSet opt)
		{
			using (EvalEnvironment env = new EvalEnvironment (opt)) {
				env.AddNodes (opt.NumberOfNodes, true);
				env.StartChurn ();

				ISubscribeInfo subscribeInfo1 = env.Nodes[0].Subscribe ();
				ISubscribeInfo subscribeInfo2 = env.Nodes[1].Subscribe ();

				while (true) {
					if (subscribeInfo1.Status == SubscribeRouteStatus.Stable && subscribeInfo2.Status == SubscribeRouteStatus.Stable)
						break;
					Thread.Sleep (10);
				}

				bool routeEstablished = false;
				IAnonymousSocket sock1 = null, sock2 = null;
				StreamSocket strm1 = null, strm2 = null;
				do {
					int datasize = 1000 * 1000;
					IntervalInterrupter timeoutChecker = new IntervalInterrupter (TimeSpan.FromMilliseconds (100), "StreamSocket TimeoutChecker");
					timeoutChecker.Start ();
					try {
						IAsyncResult ar = env.Nodes[0].AnonymousRouter.BeginConnect (subscribeInfo1.Key, subscribeInfo2.Key, AnonymousConnectionType.HighThroughput, null, null, null);
						sock1 = env.Nodes[0].AnonymousRouter.EndConnect (ar);
						if (env.Nodes[1].AnonymousSocketInfoList.Count == 0)
							throw new System.Net.Sockets.SocketException ();
						routeEstablished = true;
						sock2 = env.Nodes[1].AnonymousSocketInfoList[0].BaseSocket;
						strm1 = new StreamSocket (sock1, AnonymousRouter.DummyEndPoint, 500, timeoutChecker);
						strm2 = new StreamSocket (sock2, AnonymousRouter.DummyEndPoint, 500, timeoutChecker);
						sock1.InitializedEventHandlers ();
						sock2.InitializedEventHandlers ();
						Stopwatch sw = Stopwatch.StartNew ();
						byte[] data = new byte[datasize];
						strm1.Send (data, 0, data.Length);
						strm1.Shutdown ();
						strm2.Shutdown ();
						Logger.Log (LogLevel.Info, this, "{0:f1}sec, {1:f2}Mbps", sw.Elapsed.TotalSeconds, datasize * 8 / sw.Elapsed.TotalSeconds / 1000.0 / 1000.0);
					} catch {
					} finally {
						timeoutChecker.Dispose ();
						if (sock1 != null) sock1.Dispose ();
						if (sock2 != null) sock2.Dispose ();
						if (strm1 != null) strm1.Dispose ();
						if (strm2 != null) strm2.Dispose ();
					}
				} while (!routeEstablished);
			}
		}
	}
}
