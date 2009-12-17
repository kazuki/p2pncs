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

#define LOOPBACK

using System;
using System.Net;
using p2pncs.Net;
using p2pncs.Threading;
using p2pncs.Simulation;
using p2pncs.Simulation.VirtualNet;

namespace p2pncs
{
	class Program
	{
		static void Main (string[] args)
		{
			IntervalInterrupter interrupter = new IntervalInterrupter (TimeSpan.FromSeconds (1), "InquirySocket TimeoutCheck");
			IRTOAlgorithm rto = new RFC2988BasedRTOCalculator (TimeSpan.FromSeconds (1), TimeSpan.FromMilliseconds (200), 50);
			RandomIPAddressGenerator rndIpGen = new RandomIPAddressGenerator ();
			interrupter.Start ();

			IPEndPoint ep1, ep2;
#if LOOPBACK
			ep1 = new IPEndPoint (IPAddress.Loopback, 8080);
			ep2 = new IPEndPoint (IPAddress.Loopback, 8081);
#else
			bool bypassSerialize = false;
			ep1 = new IPEndPoint (rndIpGen.Next (), 8080);
			ep2 = new IPEndPoint (rndIpGen.Next (), 8081);
#endif

			using (VirtualNetwork vnet = new VirtualNetwork (LatencyTypes.Constant (100), 5, PacketLossType.Lossless (), 1))
#if LOOPBACK
			using (UdpSocket sock1 = UdpSocket.CreateIPv4 ())
			using (UdpSocket sock2 = UdpSocket.CreateIPv4 ())
#else
			using (VirtualUdpSocket sock1 = new VirtualUdpSocket (vnet, ep1.Address, bypassSerialize))
			using (VirtualUdpSocket sock2 = new VirtualUdpSocket (vnet, ep2.Address, bypassSerialize))
#endif
			using (InquirySocket isock1 = new InquirySocket (sock1, true, interrupter, rto, 3, 64))
			using (InquirySocket isock2 = new InquirySocket (sock2, true, interrupter, rto, 3, 64)) {
				sock1.Bind (ep1);
				sock2.Bind (ep2);
				sock1.SendTo ("hoge1", ep2);
				sock1.SendTo ("hoge2", ep2);
				sock1.SendTo ("hoge3", ep2);
				sock1.SendTo ("hoge4", ep2);
				sock2.Received.Add (typeof (string), delegate (object sender, ReceivedEventArgs e) {
					Console.WriteLine ("{0} from {1}", e.Message, e.RemoteEndPoint);
				});
				Console.ReadLine ();

				isock2.Inquired.Add (typeof (string), delegate (object sender, InquiredEventArgs e) {
					Console.WriteLine ("Inquired {0} from {1}", e.InquireMessage, e.EndPoint);
					isock2.RespondToInquiry (e, "WORLD !");
				});
				isock1.BeginInquire ("HELLO", ep2, delegate (IAsyncResult ar) {
					Console.WriteLine ("{0} from {1}", isock1.EndInquire (ar), ep2);
				}, null);
				Console.ReadLine ();
			}

			interrupter.Dispose ();
		}
	}
}
