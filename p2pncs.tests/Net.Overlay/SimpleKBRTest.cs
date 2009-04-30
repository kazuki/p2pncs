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
using NUnit.Framework;
using p2pncs.Net;
using p2pncs.Net.Overlay;
using p2pncs.Threading;
using p2pncs.Security.Cryptography;
using p2pncs.Simulation.VirtualNet;

namespace p2pncs.tests.Net.Overlay
{
	[TestFixture]
	public class SimpleKBRTest
	{
		[Test]
		public void Test ()
		{
			VirtualNetwork network = new VirtualNetwork (20, 20, 5, 2);
			IntervalInterrupter interrupter = new IntervalInterrupter (TimeSpan.FromMilliseconds (50), "MessagingSocket Interrupter");
			interrupter.Start ();
			System.Runtime.Serialization.Formatters.Binary.BinaryFormatter formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter ();
			Key[] keys = new Key[] {
				new Key (new byte[]{0x00, 0x80}),
				new Key (new byte[]{0x00, 0x40}),
				new Key (new byte[]{0x00, 0x20}),
				new Key (new byte[]{0x00, 0x10}),
				new Key (new byte[]{0x00, 0x08}),
				new Key (new byte[]{0x00, 0x04}),
				new Key (new byte[]{0x00, 0x02}),
				new Key (new byte[]{0x00, 0x01}),
			};
			List<EndPoint> endPoints = new List<EndPoint> ();
			List<IMessagingSocket> sockets = new List<IMessagingSocket> ();
			List<IKeyBasedRouter> routers = new List<IKeyBasedRouter> ();
			try {
				for (int i = 0; i < keys.Length; i++) {
					IPAddress adrs = IPAddress.Parse ("10.0.0." + (i + 1).ToString ());
					IPEndPoint ep = new IPEndPoint (adrs, 10000);
					endPoints.Add (ep);

					VirtualDatagramEventSocket sock = new VirtualDatagramEventSocket (network, adrs);
					sock.Bind (new IPEndPoint (IPAddress.Loopback, ep.Port));
					IMessagingSocket msock = new MessagingSocket (sock, true, SymmetricKey.NoneKey, formatter, null, interrupter, TimeSpan.FromSeconds (1), 2, 1024);
					sockets.Add (msock);

					IKeyBasedRouter router = new SimpleIterativeRouter (keys[i], msock, new SimpleRoutingAlgorithm ());
					routers.Add (router);
					if (i != 0) {
						router.Join (new EndPoint[] { endPoints[0] });
						System.Threading.Thread.Sleep (50);
					}
				}

				System.Threading.Thread.Sleep (1000);
				Key reqKey = new Key (new byte[] { 0x00, 0x1F });
				IAsyncResult ar = routers[0].BeginRoute (reqKey, null, 1, 3, null, null);
				RoutingResult rr = routers[0].EndRoute (ar);
				Assert.IsNotNull (rr);
				Assert.IsNotNull (rr.RootCandidates);
				Assert.AreEqual (1, rr.RootCandidates.Length);
				Assert.AreEqual (keys[3], rr.RootCandidates[0].NodeID);
				Assert.AreEqual (endPoints[3], rr.RootCandidates[0].EndPoint);
			} finally {
				for (int i = 0; i < routers.Count; i++)
					routers[i].Close ();
				for (int i = 0; i < sockets.Count; i++)
					sockets[i].Dispose ();
				network.Close ();
				interrupter.Dispose ();
			}
		}
	}
}
