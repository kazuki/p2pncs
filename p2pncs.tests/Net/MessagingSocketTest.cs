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
using System.Net;
using System.Threading;
using NUnit.Framework;
using p2pncs.Net;
using p2pncs.Threading;

namespace p2pncs.tests.Net
{
	[TestFixture]
	public class MessagingSocketTest
	{
		System.Runtime.Serialization.IFormatter _formatter;
		IntervalInterrupter _interrupter;
		const int MAX_SOCKETS = 10;
		EndPoint[] _eps;

		[TestFixtureSetUp]
		public void Init ()
		{
			_formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter ();
			_interrupter = new IntervalInterrupter (TimeSpan.FromMilliseconds (10.0));
			_interrupter.Start ();
			_eps = new EndPoint[MAX_SOCKETS];
			for (int i = 0; i < _eps.Length; i++)
				_eps[i] = new IPEndPoint (IPAddress.Loopback, 10000 + i);
		}

		[TestFixtureTearDown]
		public void Dispose ()
		{
			_interrupter.Dispose ();
		}

		IMessagingSocket[] CreateMessagingSockets (int count)
		{
			UdpSocket[] sockets = new UdpSocket[count];
			IMessagingSocket[] msockets = new MessagingSocket[count];
			for (int i = 0; i < sockets.Length; i++) {
				sockets[i] = UdpSocket.CreateIPv4 (1000);
				sockets[i].Bind (_eps[i]);
				msockets[i] = new MessagingSocket (sockets[i], true, null, _formatter, null, _interrupter, TimeSpan.FromSeconds (0.5), 2, 1024);
				msockets[i].Inquired += new InquiredEventHandler (delegate (object sender, InquiredEventArgs e) {
					(sender as MessagingSocket).StartResponse (e, ((string)e.InquireMessage) + ":Responsed", true);
				});
			}
			return msockets;
		}

		void DisposeAll (IMessagingSocket[] sockets)
		{
			for (int i = 0; i < sockets.Length; i++)
				sockets[i].Dispose ();
		}

		[Test]
		public void InquireTest ()
		{
			using (AutoResetEvent done = new AutoResetEvent (false))
			using (AutoResetEvent done2 = new AutoResetEvent (false)) {
				IMessagingSocket[] msockets = CreateMessagingSockets (2);

				try {
					int count = 0;
					msockets[0].InquirySuccess += new InquiredEventHandler (delegate (object sender, InquiredEventArgs e) {
						Interlocked.Increment (ref count);
						done2.Set ();
					});
					msockets[0].InquiryFailure += new InquiredEventHandler (delegate (object sender, InquiredEventArgs e) {
						Assert.Fail ();
					});
					msockets[1].InquirySuccess += new InquiredEventHandler (delegate (object sender, InquiredEventArgs e) {
						Assert.Fail ();
					});
					msockets[1].InquiryFailure += new InquiredEventHandler (delegate (object sender, InquiredEventArgs e) {
						Assert.Fail ();
					});

					{
						IAsyncResult ar = msockets[0].BeginInquire ("HELLO", _eps[1], null, null);
						string ret = msockets[0].EndInquire (ar) as string;
						Assert.AreEqual ("HELLO:Responsed", ret, "Use EndInquire");
						if (!done2.WaitOne (1000))
							Assert.Fail ("Timeout #0");
						Assert.AreEqual (1, count);
					}

					{
						msockets[0].BeginInquire ("HELLO2", _eps[1], delegate (IAsyncResult ar) {
							string ret = msockets[0].EndInquire (ar) as string;
							Assert.AreEqual ("HELLO2:Responsed", ret, "Use EndInquire at callback");
							done.Set ();
						}, null);
						if (!done.WaitOne (2000))
							Assert.Fail ("Timeout #1");
						if (!done2.WaitOne (1000))
							Assert.Fail ("Timeout #2");
						Assert.AreEqual (2, count);
					}
				} finally {
					DisposeAll (msockets);
				}
			}
		}

		[Test]
		public void TimeoutTest ()
		{
			using (AutoResetEvent done = new AutoResetEvent (false))
			using (AutoResetEvent done2 = new AutoResetEvent (false)) {
				EndPoint ep = new IPEndPoint (IPAddress.Loopback, ushort.MaxValue);
				IMessagingSocket[] msockets = CreateMessagingSockets (2);

				try {
					int count = 0;
					msockets[0].InquirySuccess += new InquiredEventHandler (delegate (object sender, InquiredEventArgs e) {
						Assert.Fail ();
					});
					msockets[0].InquiryFailure += new InquiredEventHandler (delegate (object sender, InquiredEventArgs e) {
						Interlocked.Increment (ref count);
						done2.Set ();
					});
					msockets[1].InquirySuccess += new InquiredEventHandler (delegate (object sender, InquiredEventArgs e) {
						Assert.Fail ();
					});
					msockets[1].InquiryFailure += new InquiredEventHandler (delegate (object sender, InquiredEventArgs e) {
						Assert.Fail ();
					});

					{
						IAsyncResult ar = msockets[0].BeginInquire ("HELLO", ep, null, null);
						Assert.IsNull (msockets[0].EndInquire (ar), "Use EndInquire");
						if (!done2.WaitOne (1000))
							Assert.Fail ("Timeout #0");
						Assert.AreEqual (1, count);
					}

					{
						msockets[0].BeginInquire ("HELLO2", ep, delegate (IAsyncResult ar) {
							Assert.IsNull (msockets[0].EndInquire (ar), "Use EndInquire at callback");
							done.Set ();
						}, null);
						if (!done.WaitOne (2000))
							Assert.Fail ("Timeout #1");
						if (!done2.WaitOne (1000))
							Assert.Fail ("Timeout #2");
						Assert.AreEqual (2, count);
					}
				} finally {
					DisposeAll (msockets);
				}
			}
		}
	}
}
