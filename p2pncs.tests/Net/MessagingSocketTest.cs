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
using NUnit.Framework;
using p2pncs.Net;

namespace p2pncs.tests.Net
{
	[TestFixture]
	public class MessagingSocketTest : IMessagingSocketTest
	{
		[TestFixtureSetUp]
		public override void Init ()
		{
			base.Init ();
		}

		[TestFixtureTearDown]
		public override void Dispose ()
		{
			base.Dispose ();
		}

		protected override void CreateMessagingSockets (int count, out IMessagingSocket[] sockets, out EndPoint[] endPoints, out EndPoint noRouteEP)
		{
			UdpSocket[] udpSockets = new UdpSocket[count];
			sockets = new MessagingSocket[count];
			endPoints = new IPEndPoint[count];
			noRouteEP = new IPEndPoint (IPAddress.Loopback, ushort.MaxValue);
			for (int i = 0; i < sockets.Length; i++) {
				udpSockets[i] = UdpSocket.CreateIPv4 ();
				endPoints[i] = new IPEndPoint (IPAddress.Loopback, 10000 + i);
				udpSockets[i].Bind (endPoints[i]);
				sockets[i] = new MessagingSocket (udpSockets[i], true, null, _formatter, null, _interrupter, DefaultTimeout, DefaultRetryCount, 1024, 1024);
			}
		}

		[Test]
		public override void InquireTest ()
		{
			base.InquireTest ();
		}

		[Test]
		public override void TimeoutTest ()
		{
			base.TimeoutTest ();
		}

		[Test]
		public override void SendTest ()
		{
			base.SendTest ();
		}

		[Test]
		public override void NullMsgTest ()
		{
			base.NullMsgTest ();
		}
	}
}
