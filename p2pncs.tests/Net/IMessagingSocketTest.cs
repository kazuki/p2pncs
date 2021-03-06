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
using System.Net;
using System.Threading;
using NUnit.Framework;
using p2pncs.Net;
using p2pncs.Security.Cryptography;
using p2pncs.Threading;
using p2pncs.Simulation;

namespace p2pncs.tests.Net
{
	public abstract class IMessagingSocketTest
	{
		protected System.Runtime.Serialization.IFormatter _formatter;
		protected IntervalInterrupter _interrupter;
		protected static IRTOAlgorithm DefaultRTO = new ConstantRTO (TimeSpan.FromSeconds (0.5));
		protected static int DefaultRetryCount = 2;

		public virtual void Init ()
		{
			_formatter = Serializer.Instance;
			_interrupter = new IntervalInterrupter (TimeSpan.FromMilliseconds (10.0), "Test IntervalInterrupter");
			_interrupter.Start ();
		}

		public virtual void Dispose ()
		{
			_interrupter.Dispose ();
		}

		protected void DisposeAll (IMessagingSocket[] sockets)
		{
			if (sockets == null)
				return;
			for (int i = 0; i < sockets.Length; i++) {
				if (sockets[i] == null)
					continue;
				sockets[i].Dispose ();
			}
		}

		protected abstract void CreateMessagingSocket (int idx, SymmetricKey key, out IMessagingSocket socket, out EndPoint endPoint);
		protected abstract EndPoint GetNoRouteEndPoint ();

		protected void CreateMessagingSockets (int count, SymmetricKey key, out IMessagingSocket[] sockets, out EndPoint[] endPoints, out EndPoint noRouteEP)
		{
			sockets = new IMessagingSocket[count];
			endPoints = new EndPoint[count];
			noRouteEP = GetNoRouteEndPoint ();
			for (int i = 0; i < sockets.Length; i++) {
				IMessagingSocket sock;
				EndPoint ep;
				CreateMessagingSocket (i, key, out sock, out ep);
				sockets[i] = sock;
				endPoints[i] = ep;
				sockets[i].InquiredUnknownMessage += DefaultInquiredEventHandler;
			}
		}
		static void DefaultInquiredEventHandler (object sender, InquiredEventArgs e)
		{
			(sender as IMessagingSocket).StartResponse (e, ((string)e.InquireMessage) + ":Responsed");
		}

		public virtual void InquireTest ()
		{
			using (AutoResetEvent done = new AutoResetEvent (false))
			using (AutoResetEvent done2 = new AutoResetEvent (false)) {
				IMessagingSocket[] msockets;
				EndPoint[] endPoints;
				EndPoint noRouteEP;
				CreateMessagingSockets (2, null, out msockets, out endPoints, out noRouteEP);

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
						IAsyncResult ar = msockets[0].BeginInquire ("HELLO", endPoints[1], null, null);
						string ret = msockets[0].EndInquire (ar) as string;
						Assert.AreEqual ("HELLO:Responsed", ret, "Use EndInquire");
						if (!done2.WaitOne (1000))
							Assert.Fail ("Timeout #0");
						Assert.AreEqual (1, count);
					}

					{
						msockets[0].BeginInquire ("HELLO2", endPoints[1], delegate (IAsyncResult ar) {
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

		public virtual void TimeoutTest ()
		{
			using (AutoResetEvent done = new AutoResetEvent (false))
			using (AutoResetEvent done2 = new AutoResetEvent (false)) {
				IMessagingSocket[] msockets;
				EndPoint[] endPoints;
				EndPoint noRouteEP;
				CreateMessagingSockets (2, null, out msockets, out endPoints, out noRouteEP);

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
						IAsyncResult ar = msockets[0].BeginInquire ("HELLO", noRouteEP, null, null);
						Assert.IsNull (msockets[0].EndInquire (ar), "Use EndInquire");
						if (!done2.WaitOne (1000))
							Assert.Fail ("Timeout #0");
						Assert.AreEqual (1, count);
					}

					{
						msockets[0].BeginInquire ("HELLO2", noRouteEP, delegate (IAsyncResult ar) {
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

		public virtual void SendTest ()
		{
			using (AutoResetEvent done = new AutoResetEvent (false)) {
				IMessagingSocket[] msockets;
				EndPoint[] endPoints;
				EndPoint noRouteEP;
				CreateMessagingSockets (2, null, out msockets, out endPoints, out noRouteEP);

				try {
					msockets[0].ReceivedUnknownMessage += new ReceivedEventHandler(delegate (object sender, ReceivedEventArgs e) {
						Assert.Fail ();
					});
					msockets[0].InquiredUnknownMessage += new InquiredEventHandler (delegate (object sender, InquiredEventArgs e) {
						Assert.Fail ();
					});
					msockets[0].InquirySuccess += new InquiredEventHandler (delegate (object sender, InquiredEventArgs e) {
						Assert.Fail ();
					});
					msockets[0].InquiryFailure += new InquiredEventHandler (delegate (object sender, InquiredEventArgs e) {
						Assert.Fail ();
					});
					msockets[1].ReceivedUnknownMessage += new ReceivedEventHandler (delegate (object sender, ReceivedEventArgs e) {
						Assert.AreEqual ("HELLO", e.Message as string);
						done.Set ();
					});
					msockets[1].InquiredUnknownMessage += new InquiredEventHandler (delegate (object sender, InquiredEventArgs e) {
						Assert.Fail ();
					});
					msockets[1].InquirySuccess += new InquiredEventHandler (delegate (object sender, InquiredEventArgs e) {
						Assert.Fail ();
					});
					msockets[1].InquiryFailure += new InquiredEventHandler (delegate (object sender, InquiredEventArgs e) {
						Assert.Fail ();
					});

					msockets[0].Send ("HELLO", endPoints[1]);
					Assert.IsTrue (done.WaitOne (2000));
				} finally {
					DisposeAll (msockets);
				}
			}
		}

		public virtual void NullMsgTest ()
		{
			using (AutoResetEvent done = new AutoResetEvent (false)) {
				IMessagingSocket[] msockets;
				EndPoint[] endPoints;
				EndPoint noRouteEP;
				CreateMessagingSockets (2, null, out msockets, out endPoints, out noRouteEP);

				try {
					msockets[0].ReceivedUnknownMessage += new ReceivedEventHandler (delegate (object sender, ReceivedEventArgs e) {
						Assert.Fail ();
					});
					msockets[0].InquiredUnknownMessage += new InquiredEventHandler (delegate (object sender, InquiredEventArgs e) {
						Assert.Fail ();
					});
					msockets[0].InquiryFailure += new InquiredEventHandler (delegate (object sender, InquiredEventArgs e) {
						Assert.Fail ();
					});
					msockets[1].ReceivedUnknownMessage += new ReceivedEventHandler (delegate (object sender, ReceivedEventArgs e) {
						Assert.IsNull (e.Message);
						done.Set ();
					});
					msockets[1].InquiredUnknownMessage -= DefaultInquiredEventHandler;
					msockets[1].InquiredUnknownMessage += new InquiredEventHandler (delegate (object sender, InquiredEventArgs e) {
						Assert.IsNull (e.InquireMessage);
						(sender as IMessagingSocket).StartResponse (e, null);
						done.Set ();
					});
					msockets[1].InquirySuccess += new InquiredEventHandler (delegate (object sender, InquiredEventArgs e) {
						Assert.Fail ();
					});
					msockets[1].InquiryFailure += new InquiredEventHandler (delegate (object sender, InquiredEventArgs e) {
						Assert.Fail ();
					});

					msockets[0].Send (null, endPoints[1]);
					Assert.IsTrue (done.WaitOne (2000));
					IAsyncResult ar = msockets[0].BeginInquire (null, endPoints[1], null, null);
					Assert.IsTrue (done.WaitOne (2000));
					Assert.IsTrue (ar.AsyncWaitHandle.WaitOne (2000));
					Assert.IsNull (msockets[0].EndInquire (ar));
				} finally {
					DisposeAll (msockets);
				}
			}
		}
	}
}
