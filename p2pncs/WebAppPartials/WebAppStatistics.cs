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

using System;
using System.IO;
using System.Xml;
using Kazuki.Net.HttpServer;
using p2pncs.Threading;

namespace p2pncs
{
	partial class WebApp
	{
		object ProcessStatistics (IHttpRequest req, HttpResponseHeader res)
		{
			if (req.QueryData.ContainsKey ("xml")) {
				res[HttpHeaderNames.ContentType] = "text/xml";
				return CreateStatisticsXML ().OuterXml;
			}
			return _xslTemplate.Render (req, res, CreateStatisticsXML (), Path.Combine (DefaultTemplatePath, "statistics.xsl"));
		}

		void UpdateStatistics ()
		{
			_node.Statistics.UpdateDelta ();
		}

		static ThreadTraceInfo[] _threadInfoArray = null;
		static void UpdateThreadStatistics ()
		{
			_threadInfoArray = ThreadTracer.GetThreadInfo ();
		}

		public XmlDocument CreateStatisticsXML ()
		{
			XmlDocument doc = XmlHelper.CreateEmptyDocument ();
			Statistics.Info info = _node.Statistics.GetInfo ();
			double runningTime = _node.RunningTime;

			XmlElement messaging = doc.CreateElement ("messaging");
			messaging.SetAttribute ("total-inquiries", info.TotalInquiries.ToString ());
			for (int i = 0; i < info.MessagingStatistics.Length; i++) {
				messaging.AppendChild (doc.CreateElement ("entry", new string[][] {
					new string[] {"success", info.MessagingStatistics[i].Success.ToString () },
					new string[] {"fail", info.MessagingStatistics[i].Fail.ToString () },
					new string[] {"retries", info.MessagingStatistics[i].Retries.ToString () },
					new string[] {"rtt-avg", info.MessagingStatistics[i].SD.Average.ToString () },
					new string[] {"rtt-sd", info.MessagingStatistics[i].SD.ComputeStandardDeviation ().ToString () },
					new string[] {"rto", ((int)_node.RTOAlgorithm.GetRTO (info.MessagingStatistics[i].EndPoint).TotalMilliseconds).ToString () }
				}, null));
			}

			XmlElement threads = doc.CreateElement ("threads");
			if (_threadInfoArray != null) {
				ThreadTraceInfo[] tiList = _threadInfoArray;
				for (int i = 0; i < tiList.Length; i++) {
					threads.AppendChild (doc.CreateElement ("thread", new string[][] {
						new string[] {"id", tiList[i].ID.ToString ()},
						new string[] {"cpu", (tiList[i].CpuUsage * 100.0F).ToString ("f2")},
						new string[] {"total-cpu-time", tiList[i].TotalCpuUsageTime.ToString ()},
						new string[] {"state", tiList[i].State.ToString ()},
						new string[] {"name", tiList[i].Name}
					}, null));
				}
			}

			doc.DocumentElement.AppendChild (doc.CreateElement ("statistics", new string[][] {
				new string[] {"running-time", Math.Floor (runningTime).ToString ()}
			}, new[] {
				doc.CreateElement ("traffic", null, new [] {
					doc.CreateElement ("total", new string[][] {
						new string[] {"recv-bytes", info.TotalReceiveBytes.ToString ()},
						new string[] {"recv-packets", info.TotalReceivePackets.ToString ()},
						new string[] {"send-bytes", info.TotalSendBytes.ToString ()},
						new string[] {"send-packets", info.TotalSendPackets.ToString ()},
						new string[] {"tcp-recv-bytes", info.TotalTcpReceiveBytes.ToString ()},
						new string[] {"tcp-send-bytes", info.TotalTcpSendBytes.ToString ()},
					}, null),
					doc.CreateElement ("average", new string[][] {
						new string[] {"recv-bytes", info.AvgReceiveBytes.ToString ()},
						new string[] {"recv-packets", info.AvgReceivePackets.ToString ()},
						new string[] {"send-bytes", info.AvgSendBytes.ToString ()},
						new string[] {"send-packets", info.AvgSendPackets.ToString ()},
						new string[] {"tcp-recv-bytes", info.AvgTcpReceiveBytes.ToString ()},
						new string[] {"tcp-send-bytes", info.AvgTcpSendBytes.ToString ()},
					}, null)
				}),
				doc.CreateElement ("kbr", new string[][] {
					new string[] {"success", info.KBR_Success.ToString ()},
					new string[] {"fail", info.KBR_Failures.ToString ()},
					new string[] {"hops-avg", info.KBR_Hops.Average.ToString ()},
					new string[] {"hops-sd", info.KBR_Hops.ComputeStandardDeviation ().ToString ()},
					new string[] {"rtt-avg", info.KBR_RTT.Average.ToString ()},
					new string[] {"rtt-sd", info.KBR_RTT.ComputeStandardDeviation ().ToString ()}
				}, null),
				doc.CreateElement ("mcr", new string[][] {
					new string[] {"success", info.MCR_Success.ToString ()},
					new string[] {"fail", info.MCR_Failures.ToString ()},
					new string[] {"lifetime-avg", info.MCR_LifeTime.Average.ToString ()},
					new string[] {"lifetime-sd", info.MCR_LifeTime.ComputeStandardDeviation ().ToString ()}
				}, null),
				doc.CreateElement ("ac", new string[][] {
					new string[] {"success", info.AC_Success.ToString ()},
					new string[] {"fail", info.AC_Failures.ToString ()}
				}, null),
				doc.CreateElement ("mmlc", new string[][] {
					new string[] {"success", info.MMLC_Success.ToString ()},
					new string[] {"fail", info.MMLC_Failures.ToString ()}
				}, null),
				threads, messaging
			}));
			return doc;
		}
	}
}
