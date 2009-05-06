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
using System.Threading;
using System.Text;
using NUnit.Framework;
using p2pncs.Net;
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.Anonymous;
using openCrypto.EllipticCurve;

namespace p2pncs.tests.Net.Overlay.Anonymous
{
	[TestFixture]
	public class AnonymousRouterTest
	{
		[Test]
		public void BidirectionalCommunicationTest ()
		{
			using (KBREnvironment env = new KBREnvironment (true, true)) {
				ECDomainNames domain = ECDomainNames.secp112r2;
				int nodes = 20;
				ECKeyPair[] nodePrivateKeys = new ECKeyPair[nodes];
				Key[] nodeKeys = new Key[nodes];
				for (int i = 0; i < nodePrivateKeys.Length; i ++) {
					nodePrivateKeys[i] = ECKeyPair.Create (domain);
					nodeKeys[i] = Key.Create (nodePrivateKeys[i]);
				}
				env.AddNodes (nodeKeys, nodePrivateKeys);

				ECKeyPair priv1 = ECKeyPair.Create (domain);
				ECKeyPair priv2 = ECKeyPair.Create (domain);
				Key id1 = Key.Create (priv1);
				Key id2 = Key.Create (priv2);
				env.AnonymousRouters[0].SubscribeRecipient (id1, priv1);
				env.AnonymousRouters[1].SubscribeRecipient (id2, priv2);
				Thread.Sleep (1000);

				byte[] received1 = null;
				object received1_lock = new object ();
				AutoResetEvent received1_done = new AutoResetEvent (false);
				byte[] received2 = null;
				object received2_lock = new object ();
				AutoResetEvent received2_done = new AutoResetEvent (false);
				AutoResetEvent accepted_done = new AutoResetEvent (false);
				IAnonymousSocket sock2 = null;
				env.AnonymousRouters[1].Accepting += delegate (object sender, AcceptingEventArgs args) {
					args.Accept (delegate (object sender2, DatagramReceiveEventArgs args2) {
						lock (received2_lock) {
							Assert.IsNull (received2, "2.received.#1");
							received2 = new byte[args2.Size];
							Buffer.BlockCopy (args2.Buffer, 0, received2, 0, received2.Length);
							received2_done.Set ();
						}
					}, null);
				};
				env.AnonymousRouters[1].Accepted += delegate (object sender, AcceptedEventArgs args) {
					lock (received2_lock) {
						Assert.IsNull (sock2, "2.accepted.#1");
						sock2 = args.Socket;
						Assert.IsNotNull (sock2, "2.accepted.#2");
						accepted_done.Set ();
					}
				};

				IAsyncResult ar = env.AnonymousRouters[0].BeginEstablishRoute (id1, id2, delegate (object sender, DatagramReceiveEventArgs args) {
					lock (received1_lock) {
						Assert.IsNull (received1, "1.received.#1");
						received1 = new byte[args.Size];
						Buffer.BlockCopy (args.Buffer, 0, received1, 0, received1.Length);
						received1_done.Set ();
					}
				}, null, null);
				IAnonymousSocket sock1 = env.AnonymousRouters[0].EndEstablishRoute (ar);
				Assert.IsNotNull (sock1, "1.sock");
				Assert.IsTrue (accepted_done.WaitOne (1000), "2.waiting");
				Assert.IsNotNull (sock2, "2.sock");

				for (int i = 0; i < 3; i ++) {
					string msg = "HELLO " + i.ToString ();
					sock1.Send (Encoding.UTF8.GetBytes (msg));
					Assert.IsTrue (received2_done.WaitOne (5000), "2.received.#2");
					Assert.IsNotNull (received2, "2.received.#3");
					Assert.AreEqual (msg, Encoding.UTF8.GetString (received2));
					lock (received2_lock) {
						received2 = null;
					}

					msg = "OK " + i.ToString ();
					sock2.Send (Encoding.UTF8.GetBytes (msg));
					Assert.IsTrue (received1_done.WaitOne (5000), "1.received.#2");
					Assert.IsNotNull (received1, "1.received.#3");
					Assert.AreEqual (msg, Encoding.UTF8.GetString (received1));
					lock (received1_lock) {
						received1 = null;
					}
				}
			}
		}

		[Test]
		public void EstablishFailTest ()
		{
			using (KBREnvironment env = new KBREnvironment (true, true)) {
				ECDomainNames domain = ECDomainNames.secp112r2;
				int nodes = 20;
				ECKeyPair[] nodePrivateKeys = new ECKeyPair[nodes];
				Key[] nodeKeys = new Key[nodes];
				for (int i = 0; i < nodePrivateKeys.Length; i ++) {
					nodePrivateKeys[i] = ECKeyPair.Create (domain);
					nodeKeys[i] = Key.Create (nodePrivateKeys[i]);
				}
				env.AddNodes (nodeKeys, nodePrivateKeys);

				ECKeyPair priv1 = ECKeyPair.Create (domain);
				ECKeyPair priv2 = ECKeyPair.Create (domain);
				Key id1 = Key.Create (priv1);
				Key id2 = Key.Create (priv2);
				env.AnonymousRouters[0].SubscribeRecipient (id1, priv1);
				env.AnonymousRouters[1].SubscribeRecipient (id2, priv2);
				Thread.Sleep (1000);

				AutoResetEvent accepted_done = new AutoResetEvent (false);
				IAnonymousSocket sock2 = null;
				env.AnonymousRouters[1].Accepting += delegate (object sender, AcceptingEventArgs args) {
					Thread.Sleep (4000); // AnonymousRouter.MCR_MaxRTTに依存
					args.Accept (delegate (object s, DatagramReceiveEventArgs e) {}, null);
				};
				env.AnonymousRouters[1].Accepted += delegate (object sender, AcceptedEventArgs args) {
					accepted_done.Set ();
					sock2 = args.Socket;
				};

				try {
					IAsyncResult ar = env.AnonymousRouters[0].BeginEstablishRoute (id1, id2, delegate (object sender, DatagramReceiveEventArgs args) {}, null, null);
					env.AnonymousRouters[0].EndEstablishRoute (ar);
					Assert.Fail ();
				} catch (System.Net.Sockets.SocketException) {}
			}
		}

		[Test]
		public void ConnectionFailTest ()
		{
			using (KBREnvironment env = new KBREnvironment (true, true)) {
				ECDomainNames domain = ECDomainNames.secp112r2;
				int nodes = 20;
				ECKeyPair[] nodePrivateKeys = new ECKeyPair[nodes];
				Key[] nodeKeys = new Key[nodes];
				for (int i = 0; i < nodePrivateKeys.Length; i++) {
					nodePrivateKeys[i] = ECKeyPair.Create (domain);
					nodeKeys[i] = Key.Create (nodePrivateKeys[i]);
				}
				env.AddNodes (nodeKeys, nodePrivateKeys);

				ECKeyPair priv1 = ECKeyPair.Create (domain);
				ECKeyPair priv2 = ECKeyPair.Create (domain);
				Key id1 = Key.Create (priv1);
				Key id2 = Key.Create (priv2);
				env.AnonymousRouters[0].SubscribeRecipient (id1, priv1);
				env.AnonymousRouters[1].SubscribeRecipient (id2, priv2);
				Thread.Sleep (1000);

				AutoResetEvent accepted_done = new AutoResetEvent (false);
				IAnonymousSocket sock2 = null;
				env.AnonymousRouters[1].Accepting += delegate (object sender, AcceptingEventArgs args) {
					args.Accept (delegate (object s, DatagramReceiveEventArgs e) { }, null);
				};
				env.AnonymousRouters[1].Accepted += delegate (object sender, AcceptedEventArgs args) {
					accepted_done.Set ();
					sock2 = args.Socket;
				};

				// Close Test
				IAsyncResult ar = env.AnonymousRouters[0].BeginEstablishRoute (id1, id2, delegate (object sender, DatagramReceiveEventArgs args) { }, null, null);
				IAnonymousSocket sock1 = env.AnonymousRouters[0].EndEstablishRoute (ar);
				sock1.Close ();
				try {
					sock1.Send (new byte[] { 1, 2, 3 });
					Assert.Fail ("close test");
				} catch (System.Net.Sockets.SocketException) { }

				// Timeout Test
				ar = env.AnonymousRouters[0].BeginEstablishRoute (id1, id2, delegate (object sender, DatagramReceiveEventArgs args) { }, null, null);
				sock1 = env.AnonymousRouters[0].EndEstablishRoute (ar);
				env.RemoveNode (1);
				Thread.Sleep (1000);
				sock1.Send (new byte[] { 1, 2, 3 });
				Thread.Sleep (10000);
				sock1.Send (new byte[] { 1, 2, 3 });
				Thread.Sleep (10000);
				sock1.Send (new byte[] { 1, 2, 3 });
				Thread.Sleep (10000);
				sock1.Send (new byte[] { 1, 2, 3 });
				Thread.Sleep (8000); // AnonymousRouter.MCR_TimeoutWithMargin (about 38sec) に依存
				try {
					sock1.Send (new byte[] {1, 2, 3});
					Assert.Fail ();
				} catch (System.Net.Sockets.SocketException) {}
			}
		}
	}
}
