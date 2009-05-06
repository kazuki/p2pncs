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
using System.Threading;
using NUnit.Framework;
using p2pncs.Net;
using p2pncs.Simulation.VirtualNet;

namespace p2pncs.tests.Simulation.VirtualNet
{
	[TestFixture]
	public class VirtualDatagramEventSocketTest : p2pncs.tests.Net.IDatagramEventSocketTest
	{
		[Test]
		public void Test1 ()
		{
			IPEndPoint ep1 = new IPEndPoint (IPAddress.Parse ("10.0.0.1"), 10000);
			IPEndPoint ep2 = new IPEndPoint (IPAddress.Parse ("10.0.0.2"), 10000);
			IPEndPoint ep3 = new IPEndPoint (IPAddress.Parse ("10.0.0.3"), 10000);

			VirtualNetwork network = new VirtualNetwork (LatencyTypes.Constant (20), 5, PacketLossType.Lossless (), 2);
			try {
				using (VirtualDatagramEventSocket sock1 = new VirtualDatagramEventSocket (network, ep1.Address))
				using (VirtualDatagramEventSocket sock2 = new VirtualDatagramEventSocket (network, ep2.Address))
				using (VirtualDatagramEventSocket sock3 = new VirtualDatagramEventSocket (network, ep3.Address)) {
					IDatagramEventSocket[] sockets = new IDatagramEventSocket[] { sock1, sock2, sock3 };
					EndPoint[] endPoints = new EndPoint[] { ep1, ep2, ep3 };
					base.Test1 (sockets, endPoints);
				}
			} finally {
				network.Close ();
			}
		}

		[Test]
		public void Test_SendToWrongPort ()
		{
			IPEndPoint ep1 = new IPEndPoint (IPAddress.Parse ("10.0.0.1"), 10000);
			IPEndPoint ep2 = new IPEndPoint (IPAddress.Parse ("10.0.0.2"), 10000);

			VirtualNetwork network = new VirtualNetwork (LatencyTypes.Constant (20), 5, PacketLossType.Lossless (), 2);
			byte[] msg = new byte[]{0, 1, 2, 3};
			try {
				using (AutoResetEvent done = new AutoResetEvent (false))
				using (VirtualDatagramEventSocket sock1 = new VirtualDatagramEventSocket (network, ep1.Address))
				using (VirtualDatagramEventSocket sock2 = new VirtualDatagramEventSocket (network, ep2.Address)) {
					sock1.Bind (new IPEndPoint (IPAddress.Any, ep1.Port));
					sock2.Bind (new IPEndPoint (IPAddress.Any, ep2.Port));
					sock1.Received += new DatagramReceiveEventHandler (delegate (object sender, DatagramReceiveEventArgs e) {
						done.Set ();
					});
					sock2.Received += new DatagramReceiveEventHandler (delegate (object sender, DatagramReceiveEventArgs e) {
						done.Set ();
					});
					sock1.SendTo (msg, new IPEndPoint (ep2.Address, ep2.Port + 1));
					Assert.IsFalse (done.WaitOne (500));
					sock2.SendTo (msg, ep1);
					Assert.IsTrue (done.WaitOne ());
				}
			} finally {
				network.Close ();
			}
		}
	}
}
