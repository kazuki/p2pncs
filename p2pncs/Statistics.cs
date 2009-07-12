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

using System.Collections.Generic;
using System.Net;
using System.Threading;
using p2pncs.Net;
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.Anonymous;
using p2pncs.Net.Overlay.DFS.MMLC;
using p2pncs.Simulation;

namespace p2pncs
{
	class Statistics
	{
		IMessagingSocket _sock;
		Dictionary<IPAddress, EndPointInfo> _messagingStatistics = new Dictionary<IPAddress, EndPointInfo> ();

		// KBR
		long _kbrSuccess = 0, _kbrFailures = 0;
		StandardDeviation _kbrRTT = new StandardDeviation (false);
		StandardDeviation _kbrHops = new StandardDeviation (false);

		// MCR
		StandardDeviation _mcrLifeTime = new StandardDeviation (false);
		long _mcrSuccess = 0, _mcrFailures = 0;

		// AC
		long _acSuccess = 0, _acFailures = 0;

		// MMLC
		long _mmlcSuccess = 0, _mmlcFailures = 0;

		public Statistics (AnonymousRouter anonRouter, MMLC mmlc)
		{
			_sock = anonRouter.KeyBasedRouter.MessagingSocket;
			Setup (_sock);
			Setup (anonRouter.KeyBasedRouter);
			Setup (anonRouter);
			Setup (mmlc);
		}

		void Setup (IMessagingSocket sock)
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

		void Setup (IKeyBasedRouter router)
		{
			router.StatisticsNotice += delegate (object sender, StatisticsNoticeEventArgs e) {
				switch (e.Type) {
					case StatisticsNoticeType.Success:
						Interlocked.Increment (ref _kbrSuccess);
						break;
					case StatisticsNoticeType.Failure:
						Interlocked.Increment (ref _kbrFailures);
						break;
					case StatisticsNoticeType.Hops:
						_kbrHops.AddSample (e.IntValue);
						break;
					case StatisticsNoticeType.RTT:
						_kbrRTT.AddSample ((float)e.TimeSpan.TotalMilliseconds);
						break;
				}
			};
		}

		void Setup (AnonymousRouter router)
		{
			router.MCRStatisticsNotice += delegate (object sender, StatisticsNoticeEventArgs args) {
				switch (args.Type) {
					case StatisticsNoticeType.Success:
						Interlocked.Increment (ref _mcrSuccess);
						break;
					case StatisticsNoticeType.Failure:
						Interlocked.Increment (ref _mcrFailures);
						break;
					case StatisticsNoticeType.LifeTime:
						_mcrLifeTime.AddSample ((float)args.TimeSpan.TotalSeconds);
						break;
				}
			};

			router.ACStatisticsNotice += delegate (object sender, StatisticsNoticeEventArgs args) {
				switch (args.Type) {
					case StatisticsNoticeType.Success:
						Interlocked.Increment (ref _acSuccess);
						break;
					case StatisticsNoticeType.Failure:
						Interlocked.Increment (ref _acFailures);
						break;
				}
			};
		}

		void Setup (MMLC mmlc)
		{
			mmlc.MergeStatisticsNotice += delegate (object sender, StatisticsNoticeEventArgs args) {
				switch (args.Type) {
					case StatisticsNoticeType.Success:
						Interlocked.Increment (ref _mmlcSuccess);
						break;
					case StatisticsNoticeType.Failure:
						Interlocked.Increment (ref _mmlcFailures);
						break;
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
			info.KBR_Success = _kbrSuccess;
			info.KBR_Failures = _kbrFailures;
			info.KBR_RTT = _kbrRTT;
			info.KBR_Hops = _kbrHops;
			info.MCR_Success = _mcrSuccess;
			info.MCR_Failures = _mcrFailures;
			info.MCR_LifeTime = _mcrLifeTime;
			info.AC_Success = _acSuccess;
			info.AC_Failures = _acFailures;
			info.MMLC_Success = _mmlcSuccess;
			info.MMLC_Failures = _mmlcFailures;
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
			public long KBR_Success;
			public long KBR_Failures;
			public StandardDeviation KBR_Hops;
			public StandardDeviation KBR_RTT;
			public long MCR_Success;
			public long MCR_Failures;
			public StandardDeviation MCR_LifeTime;
			public long AC_Success;
			public long AC_Failures;
			public long MMLC_Success;
			public long MMLC_Failures;
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
