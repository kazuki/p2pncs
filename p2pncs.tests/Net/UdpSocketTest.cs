/*
 * Copyright (C) 2009-2010 Kazuki Oikawa
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

using System.Net;
using NUnit.Framework;
using p2pncs.Net;

namespace p2pncs.tests.Net
{
	[TestFixture]
	public class UdpSocketTest : IDatagramEventSocketTest
	{
		[Test]
		public void Test1 ()
		{
			using (UdpSocket sock1 = UdpSocket.CreateIPv4 ())
			using (UdpSocket sock2 = UdpSocket.CreateIPv4 ())
			using (UdpSocket sock3 = UdpSocket.CreateIPv4 ()) {
				EndPoint ep1 = new IPEndPoint (IPAddress.Loopback, 10000);
				EndPoint ep2 = new IPEndPoint (IPAddress.Loopback, 10001);
				EndPoint ep3 = new IPEndPoint (IPAddress.Loopback, 10002);
				UdpSocket[] sockets = new UdpSocket[] { sock1, sock2, sock3 };
				EndPoint[] endPoints = new EndPoint[] { ep1, ep2, ep3 };
				base.Test1 (sockets, endPoints);
			}
		}
	}
}
