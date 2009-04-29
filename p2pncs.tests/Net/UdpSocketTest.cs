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

namespace p2pncs.tests.Net
{
	[TestFixture]
	public class UdpSocketTest
	{
		[Test]
		public void SendTest_1 ()
		{
			using (UdpSocket sock1 = UdpSocket.CreateIPv4 (1000))
			using (UdpSocket sock2 = UdpSocket.CreateIPv4 (1000))
			using (UdpSocket sock3 = UdpSocket.CreateIPv4 (1000)) {
				EndPoint ep1 = new IPEndPoint (IPAddress.Loopback, 10000);
				EndPoint ep2 = new IPEndPoint (IPAddress.Loopback, 10001);
				EndPoint ep3 = new IPEndPoint (IPAddress.Loopback, 10002);
				UdpSocket[] sockets = new UdpSocket[] { sock1, sock2, sock3 };
				EndPoint[] eps = new EndPoint[] { ep1, ep2, ep3 };
				byte[] sendData = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };
				int recvIdx = -1, recvSize = -1;
				byte[] recvData = null;
				AutoResetEvent done = new AutoResetEvent (false);

				sock1.Bind (ep1);
				sock2.Bind (ep2);
				sock3.Bind (ep3);

				sock1.Received += new DatagramReceiveEventHandler (delegate (object sender, DatagramReceiveEventArgs e) {
					recvIdx = 1;
					recvSize = e.Size;
					recvData = (byte[])e.Buffer.Clone ();
					done.Set ();
				});
				sock2.Received += new DatagramReceiveEventHandler (delegate (object sender, DatagramReceiveEventArgs e) {
					recvIdx = 2;
					recvSize = e.Size;
					recvData = (byte[])e.Buffer.Clone ();
					done.Set ();
				});
				sock3.Received += new DatagramReceiveEventHandler (delegate (object sender, DatagramReceiveEventArgs e) {
					recvIdx = 3;
					recvSize = e.Size;
					recvData = (byte[])e.Buffer.Clone ();
					done.Set ();
				});

				for (int i = 0; i < sockets.Length; i++) {
					for (int k = 0; k < eps.Length; k++) {
						sockets[i].SendTo (sendData, eps[k]);
						done.WaitOne ();
						Array.Resize<byte> (ref recvData, recvSize);
						string id = "#" + (i + 1).ToString () + "." + (k + 1).ToString ();
						Assert.AreEqual (k + 1, recvIdx, id + ".1");
						Assert.AreEqual (sendData.Length, recvSize, id + ".2");
						Assert.AreEqual (sendData, recvData, id + ".3");
					}
				}
			}
		}
	}
}
