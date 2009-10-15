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
using System.IO;
using System.Net;
using NUnit.Framework;
using openCrypto;
using p2pncs.Net;
using p2pncs.Security.Cryptography;

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

		protected override EndPoint GetNoRouteEndPoint ()
		{
			return new IPEndPoint (IPAddress.Loopback, ushort.MaxValue);
		}

		protected override void CreateMessagingSocket (int idx, SymmetricKey key, out IMessagingSocket socket, out EndPoint endPoint)
		{
			UdpSocket udpSocket = UdpSocket.CreateIPv4 ();
			endPoint = new IPEndPoint (IPAddress.Loopback, 10000 + idx);
			udpSocket.Bind (endPoint);
			socket = new MessagingSocket (udpSocket, true, key, _formatter, null, _interrupter, DefaultRTO, DefaultRetryCount, 1024, 1024);
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

		[Test]
		public void EncryptionTest ()
		{
			SymmetricKey key = new SymmetricKey (SymmetricAlgorithmType.Camellia, RNG.GetRNGBytes (16), RNG.GetRNGBytes (16));
			IMessagingSocket[] msockets;
			EndPoint[] endPoints;
			EndPoint noRouteEP;
			CreateMessagingSockets (2, key, out msockets, out endPoints, out noRouteEP);
			msockets[1].InquiredHandlers.Add (typeof (byte[]), delegate (object sender, InquiredEventArgs e) {
				msockets[1].StartResponse (e, e.InquireMessage);
			});

			try {
				byte[] data = new byte[msockets[0].MaxMessageSize];
				while (true) {
					byte[] serialized;
					using (MemoryStream ms = new MemoryStream ()) {
						_formatter.Serialize (ms, data);
						ms.Close ();
						serialized = ms.ToArray ();
					}
					if (serialized.Length == msockets[0].MaxMessageSize)
						break;
					data = new byte[data.Length - 1];
				}
				IAsyncResult ar = msockets[0].BeginInquire (data, endPoints[1], null, null);
				byte[] ret = msockets[0].EndInquire (ar) as byte[];
				Assert.AreEqual (ret, data);
			} finally {
				DisposeAll (msockets);
			}
		}
	}
}
