/*
 * Copyright (C) 2010 Kazuki Oikawa
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
using System.Threading;
using NUnit.Framework;
using p2pncs.Net;
using p2pncs.Simulation;
using p2pncs.Simulation.VirtualNet;

namespace p2pncs.tests.Simulation.VirtualNet
{
	[TestFixture]
	public class VirtualTcpConnectionDispatcherTest
	{
		static RandomIPAddressGenerator RndIpGen = new RandomIPAddressGenerator ();

		VirtualNetwork CreateVirtualNetwork ()
		{
			return new VirtualNetwork (LatencyTypes.Constant (5), 5, PacketLossType.Constant (1), Environment.ProcessorCount);
		}

		void CreateNodes (VirtualNetwork vnet, int count, out VirtualTcpConnectionDispatcher[] dispatchers, out IPEndPoint[] remoteEPs)
		{
			Random rnd = new Random ();
			dispatchers = new VirtualTcpConnectionDispatcher[count];
			remoteEPs = new IPEndPoint[count];

			for (int i = 0; i < count; i ++) {
				remoteEPs[i] = new IPEndPoint (RndIpGen.Next (), rnd.Next (1, ushort.MaxValue));
				dispatchers[i] = new VirtualTcpConnectionDispatcher (vnet, remoteEPs[i].Address, true);
				dispatchers[i].Bind (new IPEndPoint (IPAddress.Any, remoteEPs[i].Port));
				dispatchers[i].ListenStart ();
			}
		}

		[Test]
		public void Test1 ()
		{
			const string HelloMessage = "Hello";
			const string SimpleTest0 = "SimpleTest0";
			const string SimpleTest1 = "SimpleTest1";

			using (VirtualNetwork vnet = CreateVirtualNetwork ()) {
				VirtualTcpConnectionDispatcher[] dispatchers;
				IPEndPoint[] remoteEPs;
				CreateNodes (vnet, 3, out dispatchers, out remoteEPs);
				ISocket sock0 = null, sock1 = null;
				List<string> list0 = new List<string> (), list1 = new List<string> ();

				dispatchers[0].Register (typeof (string), delegate (object sender, AcceptedEventArgs e) {
					Assert.IsNull (sock0);
					sock0 = e.Socket;
					Assert.IsNotNull (sock0);
					Assert.AreEqual (HelloMessage, e.AuxiliaryInfo as string);
				});

				for (int loop = 0; loop < 2; loop ++) {
					// Connect & Accept Test
					sock1 = dispatchers[1].EndConnect (dispatchers[1].BeginConnect (remoteEPs[0], null, null));
					Assert.IsNotNull (sock1);
					sock1.Send (HelloMessage);
					while (sock0 == null) Thread.Sleep (50);

					// Register Received Handler
					sock0.Received.Add (typeof (string), delegate (object sender, ReceivedEventArgs e) {
						Assert.AreEqual (remoteEPs[1].Address, (e.RemoteEndPoint as IPEndPoint).Address);
						Assert.AreNotEqual (remoteEPs[1], e.RemoteEndPoint);
						lock (list0) {
							list0.Add (e.Message as string);
						}
					});
					sock1.Received.Add (typeof (string), delegate (object sender, ReceivedEventArgs e) {
						Assert.AreEqual (remoteEPs[0], e.RemoteEndPoint);
						lock (list1) {
							list1.Add (e.Message as string);
						}
					});

					// Simple Message Exchange Test
					sock0.Send (SimpleTest0);
					sock1.Send (SimpleTest1);
					while (list0.Count == 0 || list1.Count == 0) Thread.Sleep (50);
					Assert.AreEqual (1, list0.Count);
					Assert.AreEqual (1, list1.Count);
					Assert.AreEqual (SimpleTest1, list0[0]);
					Assert.AreEqual (SimpleTest0, list1[0]);
					list0.Clear (); list1.Clear ();

					// Close Test
					if (loop == 0) {
						sock0.Close (); // Close accepted socket
						TestClosedSocket (sock0, sock1, "#0");
						sock0 = null; sock1.Close (); sock1 = null;
					} else {
						sock1.Close (); // Close initiator socket
						TestClosedSocket (sock1, sock0, "#1");
						sock1 = null; sock0.Close (); sock0 = null;
					}
				}
			}
		}

		void TestClosedSocket (ISocket closedSocket, ISocket aliveSocket, string msg)
		{
			closedSocket.Received.Remove (typeof (string));
			closedSocket.Received.Add (typeof (string), delegate (object sender, ReceivedEventArgs e) {
				Assert.Fail (msg + " (recv)");
			});

			aliveSocket.Send ("CLOSE TEST");
			Thread.Sleep (500);

			bool flag = true;
			try {
				aliveSocket.Send ("CLOSE TEST");
			} catch {
				flag = false;
			}
			if (flag)
				Assert.Fail (msg);
		}
	}
}
