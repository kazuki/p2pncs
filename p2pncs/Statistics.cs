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

using System.Collections.Generic;
using System.Net;
using p2pncs.Net;
using p2pncs.Simulation;

namespace p2pncs
{
	class Statistics
	{
		IMessagingSocket _sock;
		Dictionary<IPAddress, EndPointInfo> _messagingStatistics = new Dictionary<IPAddress, EndPointInfo> ();

		public Statistics (IMessagingSocket sock)
		{
			SetupMessagingSocket (sock);
			_sock = sock;
		}

		void SetupMessagingSocket (IMessagingSocket sock)
		{
			sock.InquirySuccess += delegate (object sender, InquiredEventArgs e) {
				IPEndPoint ipep = e.EndPoint as IPEndPoint;
				if (ipep == null)
					return;

				lock (_messagingStatistics) {
					EndPointInfo info;
					if (!_messagingStatistics.TryGetValue (ipep.Address, out info)) {
						info = new EndPointInfo ();
						_messagingStatistics.Add (ipep.Address, info);
					}
					info.SD.AddSample ((float)e.RTT.TotalMilliseconds);
					info.Success ++;
					info.Retries += e.Retries;
				}
			};
			sock.InquiryFailure += delegate (object sender, InquiredEventArgs e) {
				IPEndPoint ipep = e.EndPoint as IPEndPoint;
				if (ipep == null)
					return;
				lock (_messagingStatistics) {
					EndPointInfo info;
					if (!_messagingStatistics.TryGetValue (ipep.Address, out info)) {
						info = new EndPointInfo ();
						_messagingStatistics.Add (ipep.Address, info);
					}
					info.Fail ++;
				}
			};
		}

		public Info GetInfo ()
		{
			Info info = new Info ();
			lock (_messagingStatistics) {
				info.MessagingStatistics = new List<EndPointInfo> (_messagingStatistics.Values).ToArray ();
			}
			info.TotalInquiries = _sock.NumberOfInquiries;
			info.TotalReceiveBytes = _sock.BaseSocket.ReceivedBytes;
			info.TotalReceivePackets = _sock.BaseSocket.ReceivedDatagrams;
			info.TotalSendBytes = _sock.BaseSocket.SentBytes;
			info.TotalSendPackets = _sock.BaseSocket.SentDatagrams;
			return info;
		}

		public class Info
		{
			public EndPointInfo[] MessagingStatistics;
			public long TotalInquiries;
			public long TotalReceiveBytes;
			public long TotalReceivePackets;
			public long TotalSendBytes;
			public long TotalSendPackets;
		}

		public class EndPointInfo
		{
			public StandardDeviation SD = new StandardDeviation (false);
			public long Success = 0;
			public long Fail = 0;
			public long Retries = 0;
		}
	}
}
