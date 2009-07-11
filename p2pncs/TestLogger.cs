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

#if !DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using p2pncs.Net;
using p2pncs.Simulation;
using System.Net;
using System.Threading;

namespace p2pncs
{
	static class TestLogger
	{
		static Dictionary<IPAddress, EndPointInfo> _udpRTTs = new Dictionary<IPAddress, EndPointInfo> ();
		static StandardDeviation _acRTT = new StandardDeviation (true);
		static long _acSuccess = 0, _acFail = 0;

		public static void SetupUdpMessagingSocket (IMessagingSocket sock)
		{
			sock.InquirySuccess += delegate (object sender, InquiredEventArgs e) {
				AddUdpRTT (e.EndPoint, (float)e.RTT.TotalMilliseconds);
			};
			sock.InquiryFailure += delegate (object sender, InquiredEventArgs e) {
				IPEndPoint ipep = e.EndPoint as IPEndPoint;
				if (ipep == null)
					return;
				lock (_udpRTTs) {
					EndPointInfo info;
					if (!_udpRTTs.TryGetValue (ipep.Address, out info)) {
						info = new EndPointInfo ();
						_udpRTTs.Add (ipep.Address, info);
					}
					Interlocked.Increment (ref info.Fail);
				}
			};
		}

		public static void SetupAcMessagingSocket (IMessagingSocket sock)
		{
			sock.InquirySuccess += delegate (object sender, InquiredEventArgs e) {
				AddAcRTT ((float)e.RTT.TotalMilliseconds);
			};
			sock.InquiryFailure += delegate (object sender, InquiredEventArgs e) {
				Interlocked.Increment (ref _acFail);
			};
		}

		static void AddUdpRTT (EndPoint ep, float rtt_ms)
		{
			IPEndPoint ipep = ep as IPEndPoint;
			if (ipep == null)
				return;

			lock (_udpRTTs) {
				EndPointInfo info;
				if (!_udpRTTs.TryGetValue (ipep.Address, out info)) {
					info = new EndPointInfo ();
					_udpRTTs.Add (ipep.Address, info);
				}
				info.SD.AddSample (rtt_ms);
				Interlocked.Increment (ref info.Success);
			}
		}

		static void AddAcRTT (float rtt_ms)
		{
			lock (_acRTT) {
				_acRTT.AddSample (rtt_ms);
			}
			Interlocked.Increment (ref _acSuccess);
		}

		public static void Dump ()
		{
			using (StreamWriter writer = new StreamWriter ("test.log", true, Encoding.UTF8)) {
				writer.WriteLine ("* UDP RTT");
				lock (_udpRTTs) {
					foreach (KeyValuePair<IPAddress, EndPointInfo> pair in _udpRTTs) {
						string ip = BitConverter.ToUInt32 (pair.Key.GetAddressBytes (), 0).ToString ("x");
						IList<float> list = pair.Value.SD.Samples;
						writer.WriteLine ("* {0} RTT Avg={1:f2}, SD={2:f2}, Rate={3:f2}, Samples={4}", ip, pair.Value.SD.Average,
							pair.Value.SD.ComputeStandardDeviation (), (double)pair.Value.Fail / (double)(pair.Value.Success + pair.Value.Fail),
							pair.Value.Success + pair.Value.Fail);
						for (int i = 0; i < list.Count; i ++)
							writer.WriteLine ("{0}: {1:f2}", ip, list[i]);
					}
				}
				lock (_acRTT) {
					writer.WriteLine ("* AC RTT Avg={0:f2}, SD={1:f2}", _acRTT.Average, _acRTT.ComputeStandardDeviation ());
					IList<float> list = _acRTT.Samples;
					for (int i = 0; i < list.Count; i++)
						writer.WriteLine ("{0:f2}", list[i]);
				}
			}
		}

		class EndPointInfo
		{
			public StandardDeviation SD = new StandardDeviation (true);
			public long Success = 0;
			public long Fail = 0;
		}
	}
}
#endif
