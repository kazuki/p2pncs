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

using System.Net;
using NUnit.Framework;
using p2pncs.Net;
using p2pncs.Security.Cryptography;
using p2pncs.Simulation;
using p2pncs.Simulation.VirtualNet;

namespace p2pncs.tests.Simulation.VirtualNet
{
	[TestFixture]
	public class VirtualMessagingSocketTest : p2pncs.tests.Net.IMessagingSocketTest
	{
		VirtualNetwork _net;
		static RandomIPAddressGenerator _adrsGen = new RandomIPAddressGenerator ();

		[TestFixtureSetUp]
		public override void Init ()
		{
			base.Init ();
			_net = new VirtualNetwork (LatencyTypes.Constant (20), 5, PacketLossType.Lossless (), 2);
		}

		[TestFixtureTearDown]
		public override void Dispose ()
		{
			base.Dispose ();
			_net.Close ();
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

		protected override EndPoint GetNoRouteEndPoint ()
		{
			return new IPEndPoint (IPAddress.Parse ("192.168.255.254"), 10000);
		}

		protected override void CreateMessagingSocket (int idx, SymmetricKey key, out IMessagingSocket socket, out EndPoint endPoint)
		{
			endPoint = new IPEndPoint (_adrsGen.Next (), 10000);
			VirtualDatagramEventSocket sock = new VirtualDatagramEventSocket (_net, ((IPEndPoint)endPoint).Address);
			sock.Bind (endPoint);
			socket = new VirtualMessagingSocket (sock, true, _interrupter, DefaultTimeout, DefaultRetryCount, 1024, 1024);
		}
	}
}
