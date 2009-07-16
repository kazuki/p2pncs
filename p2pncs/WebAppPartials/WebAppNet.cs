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
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;
using Kazuki.Net.HttpServer;
using p2pncs.Net;
using Dns = System.Net.Dns;
using EndPoint = System.Net.EndPoint;
using IPAddress = System.Net.IPAddress;
using IPEndPoint = System.Net.IPEndPoint;

namespace p2pncs
{
	partial class WebApp
	{
		object ProcessNetInitPage (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			XmlDocument doc = XmlHelper.CreateEmptyDocument ();
			if (req.HttpMethod == HttpMethod.POST && req.HasContentBody ()) {
				Dictionary<string, string> dic = HttpUtility.ParseUrlEncodedStringToDictionary (Encoding.ASCII.GetString (req.GetContentBody (MaxRequestBodySize)), Encoding.UTF8);
				if (dic.ContainsKey ("nodes")) {
					string[] lines = dic["nodes"].Replace ("\r\n", "\n").Replace ('\r', '\n').Split ('\n');
					List<EndPoint> list = new List<EndPoint> ();
					List<string> raw_list = new List<string> ();
					for (int i = 0; i < lines.Length; i++) {
						EndPoint ep;
						lines[i] = lines[i].Trim ();
						if (lines[i].StartsWith ("%")) {
							ep = EndPointObfuscator.Decode (lines[i]);
						} else {
							ep = Helpers.Parse (lines[i]);
						}
						if (ep != null) {
							list.Add (ep);
							raw_list.Add (lines[i]);
						}
					}
					if (list.Count > 0) {
						new Thread (delegate () {
							for (int i = 0; i < list.Count; i++) {
								if (list[i] is IPEndPoint) {
									if ((list[i] as IPEndPoint).Address.Equals (_node.GetCurrentPublicIPAddress ()))
										continue;
									_node.PortOpenChecker.Join (list[i]);
								}
							}
							for (int i = 0; i < list.Count; i++) {
								if (list[i] is DnsEndPoint) {
									IPAddress[] adrs_list = Dns.GetHostAddresses ((list[i] as DnsEndPoint).DNS);
									for (int k = 0; k < adrs_list.Length; k++) {
										if (adrs_list[k].AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !adrs_list[k].Equals (_node.GetCurrentPublicIPAddress ())) {
											_node.PortOpenChecker.Join (new IPEndPoint (adrs_list[k], (list[i] as DnsEndPoint).Port));
										}
									}
								}
							}
						}).Start ();
						XmlNode root = doc.DocumentElement.AppendChild (doc.CreateElement ("connected"));
						for (int i = 0; i < list.Count; i++) {
							XmlElement element = doc.CreateElement ("endpoint");
							element.AppendChild (doc.CreateTextNode (raw_list[i].ToString ()));
							root.AppendChild (element);
						}
					}
				} else if (dic.ContainsKey ("ip") && dic.ContainsKey ("port")) {
					string ip_dns = dic["ip"].Trim ();
					string port = dic["port"].Trim ();
					try {
						EndPoint ep = Helpers.Parse (ip_dns + ":" + port);
						if (ep == null)
							throw new FormatException ();
						if (ep is IPEndPoint && (IPAddressUtility.IsPrivate ((ep as IPEndPoint).Address) || (ep as IPEndPoint).Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork))
							throw new FormatException ();
						string encoded = EndPointObfuscator.Encode (ep);
						doc.DocumentElement.AppendChild (doc.CreateElement ("encoded", null, new XmlNode[] {
							doc.CreateElement ("source", null, new[] {
								doc.CreateTextNode (ip_dns + ":" + port)
							}),
							doc.CreateTextNode (encoded)
						}));
					} catch {
						doc.DocumentElement.AppendChild (doc.CreateElement ("encoded", null, new[] {
							doc.CreateElement ("source", null, new[] {
								doc.CreateTextNode (ip_dns + ":" + port)
							}),
							doc.CreateElement ("error")
						}));
					}
				}
			}

			if (!IPAddressUtility.IsPrivate (_node.GetCurrentPublicIPAddress ())) {
				doc.DocumentElement.AppendChild (doc.CreateElement ("ipendpoint", new string[][] {
					new [] {"ip", _node.GetCurrentPublicIPAddress().ToString ()},
					new [] {"port", _node.BindUdpPort.ToString ()}
				}, new[] {
					doc.CreateTextNode (EndPointObfuscator.Encode (new IPEndPoint (_node.GetCurrentPublicIPAddress (), _node.BindUdpPort)))
				}));
			}

			return _xslTemplate.Render (server, req, res, doc, Path.Combine (DefaultTemplatePath, "net_init.xsl"));
		}

		object ProcessNetExitPage (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			XmlDocument doc = XmlHelper.CreateEmptyDocument ();
			if (req.HttpMethod == HttpMethod.POST) {
				doc.DocumentElement.SetAttribute ("exit", "exit");
				ThreadPool.QueueUserWorkItem (delegate (object o) {
					Thread.Sleep (500);
					_exitWaitHandle.Set ();
				});
			}
			return _xslTemplate.Render (server, req, res, doc, Path.Combine (DefaultTemplatePath, "net_exit.xsl"));
		}
	}
}
