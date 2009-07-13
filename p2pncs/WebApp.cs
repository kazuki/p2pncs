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

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;
using Kazuki.Net.HttpServer;
using Kazuki.Net.HttpServer.TemplateEngines;
using openCrypto.EllipticCurve;
using p2pncs.Net;
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.Anonymous;
using p2pncs.Net.Overlay.DFS.MMLC;
using p2pncs.Security.Captcha;
using p2pncs.Security.Cryptography;
using EndPoint = System.Net.EndPoint;
using IPEndPoint = System.Net.IPEndPoint;
using IPAddress = System.Net.IPAddress;
using Dns = System.Net.Dns;

namespace p2pncs
{
	class WebApp : IHttpApplication, IDisposable
	{
		const string DefaultTemplatePath = "templates";
		const string DefaultStaticFilePath = "htdocs";
		const string DefaultDateFormat = "yyyy/MM/dd HH:mm:ss";

		const int MaxRetries = 2;
		static TimeSpan Timeout = TimeSpan.FromSeconds (2);
		const int RetryBufferSize = 512;
		const int DuplicationCheckBufferSize = 512;
		const int MaxStreamSocketSegmentSize = 500;
		const int MaxRequestBodySize = 33554432; // 32MB

		Node _node;
		ManualResetEvent _exitWaitHandle = new ManualResetEvent (false);
		XslTemplateEngine _xslTemplate = new XslTemplateEngine ();
		bool _fastView = true;

		public WebApp (Node node)
		{
			_node = node;
			node.MMLC.Register (SimpleBBSParser.Instance);
		}

		public void Dispose ()
		{
		}

		public object Process (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			string absPath = req.Url.AbsolutePath;
			if (absPath == "/")
				return ProcessMainPage (server, req, res);
			if (absPath == "/net/init")
				return ProcessNetInitPage (server, req, res);
			if (absPath == "/net/exit")
				return ProcessNetExitPage (server, req, res);
			if (absPath == "/bbs" || absPath == "/bbs/")
				return ProcessBbsListPage (server, req, res);
			if (absPath == "/bbs/new")
				return ProcessBbsNewPage (server, req, res);
			if (absPath == "/bbs/open")
				return ProcessBbsOpenPage (server, req, res);
			if (absPath.StartsWith ("/bbs/"))
				return ProcessBBS (server, req, res, false);
			if (absPath == "/manage" || absPath == "/manage/")
				return ProcessManageTop (server, req, res);
			if (absPath.StartsWith ("/manage/"))
				return ProcessManageFile (server, req, res);
			if (absPath == "/statistics")
				return ProcessStatistics (server, req, res);
			return ProcessStaticFile (server, req, res);
		}

		XmlDocument CreateEmptyDocument ()
		{
			XmlDocument doc = new XmlDocument ();
			doc.AppendChild (doc.CreateElement ("page"));
			return doc;
		}

		object ProcessMainPage (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			XmlDocument doc = CreateEmptyDocument ();
			doc.DocumentElement.SetAttribute ("ver", System.Reflection.Assembly.GetEntryAssembly ().GetName ().Version.ToString ());
			return _xslTemplate.Render (server, req, res, doc, Path.Combine (DefaultTemplatePath, "main.xsl"));
		}

		object ProcessNetInitPage (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			XmlDocument doc = CreateEmptyDocument ();
			if (req.HttpMethod == HttpMethod.POST && req.HasContentBody ()) {
				Dictionary<string, string> dic = HttpUtility.ParseUrlEncodedStringToDictionary (Encoding.ASCII.GetString (req.GetContentBody (MaxRequestBodySize)), Encoding.UTF8);
				if (dic.ContainsKey ("nodes")) {
					string[] lines = dic["nodes"].Replace ("\r\n", "\n").Replace ('\r', '\n').Split ('\n');
					List<EndPoint> list = new List<EndPoint> ();
					List<string> raw_list = new List<string> ();
					for (int i = 0; i < lines.Length; i ++) {
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
							for (int i = 0; i < list.Count; i ++) {
								if (list[i] is IPEndPoint) {
									if ((list[i] as IPEndPoint).Address.Equals (_node.GetCurrentPublicIPAddress ()))
										continue;
									_node.PortOpenChecker.Join (list[i]);
								}
							}
							for (int i = 0; i < list.Count; i++) {
								if (list[i] is DnsEndPoint) {
									IPAddress[] adrs_list = Dns.GetHostAddresses ((list[i] as DnsEndPoint).DNS);
									for (int k = 0; k < adrs_list.Length; k ++) {
										if (adrs_list[k].AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !adrs_list[k].Equals (_node.GetCurrentPublicIPAddress ())) {
											_node.PortOpenChecker.Join (new IPEndPoint (adrs_list[k], (list[i] as DnsEndPoint).Port));
										}
									}
								}
							}
						}).Start ();
						XmlNode root = doc.DocumentElement.AppendChild (doc.CreateElement ("connected"));
						for (int i = 0; i < list.Count; i ++) {
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
					new [] {"port", _node.BindPort.ToString ()}
				}, new [] {
					doc.CreateTextNode (EndPointObfuscator.Encode (new IPEndPoint (_node.GetCurrentPublicIPAddress (), _node.BindPort)))
				}));
			}

			return _xslTemplate.Render (server, req, res, doc, Path.Combine (DefaultTemplatePath, "net_init.xsl"));
		}

		object ProcessNetExitPage (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			XmlDocument doc = CreateEmptyDocument ();
			if (req.HttpMethod == HttpMethod.POST) {
				doc.DocumentElement.SetAttribute ("exit", "exit");
				ThreadPool.QueueUserWorkItem (delegate (object o) {
					Thread.Sleep (500);
					_exitWaitHandle.Set ();
				});
			}
			return _xslTemplate.Render (server, req, res, doc, Path.Combine (DefaultTemplatePath, "net_exit.xsl"));
		}

		object ProcessBbsNewPage (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			XmlDocument doc = CreateEmptyDocument ();
			if (req.HttpMethod == HttpMethod.POST) {
				Dictionary<string, string> dic = HttpUtility.ParseUrlEncodedStringToDictionary (Encoding.ASCII.GetString (req.GetContentBody (MaxRequestBodySize)), Encoding.UTF8);
				XmlNode validationRoot = doc.DocumentElement.AppendChild (doc.CreateElement ("validation"));
				string title = Helpers.GetValueSafe (dic, "title").Trim ();
				string auth = Helpers.GetValueSafe (dic, "auth").Trim ();
				string fpname = Helpers.GetValueSafe (dic, "fpname").Trim ();
				string fpbody = Helpers.GetValueSafe (dic, "fpbody").Trim ();
				string state = Helpers.GetValueSafe (dic, "state").Trim ();
				bool reedit = Helpers.GetValueSafe (dic, "re-edit").Length > 0;
				bool title_status = title.Length != 0 && title.Length <= 64;
				bool auth_status = true;
				bool all_status = true;
				try {
					AuthServerInfo.ParseArray (auth);
				} catch {
					auth_status = false;
				}
				if (title_status && auth_status && !reedit) {
					SimpleBBSHeader header2 = new SimpleBBSHeader (title);
					MergeableFileRecord record = null;
					if (fpbody.Length > 0)
						record = new MergeableFileRecord (new SimpleBBSRecord (fpname, fpbody), DateTime.UtcNow, DateTime.UtcNow, null, null, null, byte.MaxValue, new byte[DefaultAlgorithm.ECDomainBytes + 1]);
					AuthServerInfo[] auth_servers = AuthServerInfo.ParseArray (auth);
					if (state == "confirm") {
						try {
							MergeableFileHeader header = _node.MMLC.CreateNew (header2, auth_servers, record == null ? null : new MergeableFileRecord[] {record});
							doc.DocumentElement.AppendChild (doc.CreateElement ("created", new string[][] { new[] { "key", header.Key.ToUriSafeBase64String () } }, null));
							state = "success";
						} catch (OutOfMemoryException) {
							all_status = false;
							state = "";
						}
					} else {
						state = "confirm";
						MergeableFileHeader header = new MergeableFileHeader (ECKeyPair.Create (DefaultAlgorithm.ECDomainName), DateTime.UtcNow, DateTime.UtcNow, header2, auth_servers);
						if (Serializer.Instance.Serialize (header).Length > MMLC.MergeableFileHeaderMaxSize || (record != null && Serializer.Instance.Serialize (record).Length > MMLC.MergeableFileRecordMaxSize)) {
							all_status = false;
							state = "";
						}
					}
				} else {
					state = string.Empty;
				}

				if (!all_status) {
					validationRoot.AppendChild (doc.CreateElement ("all", null, new[]{
						doc.CreateTextNode (string.Format ("ヘッダサイズが{0}バイトを超えたか、最初の投稿が{1}バイトを超えました. タイトルや認証サーバまたは投稿文の情報量を減らしてください", MMLC.MergeableFileHeaderMaxSize, MMLC.MergeableFileRecordMaxSize))
					}));
				}
				validationRoot.AppendChild (doc.CreateElement ("data", new string[][] { new[] { "name", "title" }, new[] { "status", title_status ? "ok" : "error" } }, new[] {
					doc.CreateElement ("value", null, new[]{doc.CreateTextNode (title)}),
					title_status ? null : doc.CreateElement ("msg", null, new[]{doc.CreateTextNode ("タイトルは1文字～64文字に収まらなければいけません")})
				}));
				validationRoot.AppendChild (doc.CreateElement ("data", new string[][] { new[] { "name", "auth" }, new[] { "status", auth_status ? "ok" : "error" } }, new[] {
					doc.CreateElement ("value", null, new[]{doc.CreateTextNode (auth)}),
					auth_status ? null : doc.CreateElement ("msg", null, new[]{doc.CreateTextNode ("認識できません。入力ミスがないか確認してください")})
				}));
				validationRoot.AppendChild (doc.CreateElement ("data", new string[][] { new[] { "name", "fpname" } }, new[] {
					doc.CreateElement ("value", null, new[]{doc.CreateTextNode (fpname)})
				}));
				validationRoot.AppendChild (doc.CreateElement ("data", new string[][] { new[] { "name", "fpbody" } }, new[] {
					doc.CreateElement ("value", null, new[]{doc.CreateTextNode (fpbody)})
				}));
				validationRoot.AppendChild (doc.CreateElement ("data", new string[][] { new[] { "name", "state" } }, new[] {
					doc.CreateElement ("value", null, new[]{doc.CreateTextNode (state)})
				}));
			}
			return _xslTemplate.Render (server, req, res, doc, Path.Combine (DefaultTemplatePath, "bbs_new.xsl"));
		}

		object ProcessBbsOpenPage (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			if (req.HttpMethod == HttpMethod.POST && req.HasContentBody ()) {
				Dictionary<string, string> dic = HttpUtility.ParseUrlEncodedStringToDictionary (Encoding.ASCII.GetString (req.GetContentBody (MaxRequestBodySize)), Encoding.UTF8);
				string key;
				if (dic.TryGetValue ("bbsid", out key)) {
					res[HttpHeaderNames.Location] = "/bbs/" + key;
					throw new HttpException (req.HttpVersion == HttpVersion.Http10 ? HttpStatusCode.Found : HttpStatusCode.SeeOther);
				}
			}
			XmlDocument doc = CreateEmptyDocument ();
			return _xslTemplate.Render (server, req, res, doc, Path.Combine (DefaultTemplatePath, "bbs_open.xsl"));
		}

		object ProcessBbsListPage (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			XmlDocument doc = CreateEmptyDocument ();
			XmlElement rootNode = doc.DocumentElement;
			MergeableFileHeader[] headers = _node.MMLC.GetHeaderList ();
			foreach (MergeableFileHeader header in headers) {
				rootNode.AppendChild (CreateMergeableFileElement (doc, header));
			}
			return _xslTemplate.Render (server, req, res, doc, Path.Combine (DefaultTemplatePath, "bbs.xsl"));
		}

		object ProcessBBS (IHttpServer server, IHttpRequest req, HttpResponseHeader res, bool callByCallback)
		{
			string str_key = req.Url.AbsolutePath.Substring (5);
			Key key;
			try {
				if (str_key.Length == (DefaultAlgorithm.ECDomainBytes + 1) * 2)
					key = Key.Parse (str_key);
				else if (str_key.Length == (DefaultAlgorithm.ECDomainBytes + 1) * 4 / 3)
					key = Key.FromUriSafeBase64String (str_key);
				else
					throw new HttpException (HttpStatusCode.NotFound);
			} catch {
				throw new HttpException (HttpStatusCode.NotFound);
			}

			MergeableFileHeader header;
			if (req.HttpMethod == HttpMethod.POST) {
				// posting...
				res[HttpHeaderNames.ContentType] = "text/xml; charset=UTF-8";
				string name = Helpers.GetQueryValue (req, "name").Trim ();
				string body = Helpers.GetQueryValue (req, "body").Trim ();
				string auth = Helpers.GetQueryValue (req, "auth").Trim ();
				string token = Helpers.GetQueryValue (req, "token").Trim ();
				string answer = Helpers.GetQueryValue (req, "answer").Trim ();
				string prev = Helpers.GetQueryValue (req, "prev").Trim ();
				if (body.Length > 0) {
					header = _node.MMLC.GetMergeableFileHeader (key);
					if (header == null)
						return "<result status=\"ERROR\" />";
					MergeableFileRecord record;
					try {
						if (header.AuthServers == null || header.AuthServers.Length == 0) {
							_node.MMLC.AppendRecord (key, new MergeableFileRecord (new SimpleBBSRecord (name, body), DateTime.UtcNow, header.LastManagedTime, null, null, null, 0, null));
							return "<result status=\"OK\" />";
						} else {
							byte auth_idx = byte.Parse (auth);
							if (token.Length > 0 && answer.Length > 0 && prev.Length > 0) {
								record = (MergeableFileRecord)Serializer.Instance.Deserialize (Convert.FromBase64String (prev));
								byte[] sign = _node.MMLC.VerifyCaptchaChallenge (header.AuthServers[auth_idx], record.Hash.GetByteArray (),
									Convert.FromBase64String (token), Encoding.ASCII.GetBytes (answer));
								if (sign != null) {
									record.AuthorityIndex = auth_idx;
									record.Authentication = sign;
									if (record.Verify (header)) {
										_node.MMLC.AppendRecord (key, record);
										return "<result status=\"OK\" />";
									} else {
										return "<result status=\"ERROR\" code=\"1\" />";
									}
								}
							}

							record = new MergeableFileRecord (new SimpleBBSRecord (name, body), DateTime.UtcNow, header.LastManagedTime, null, null, null, 0, null);
							CaptchaChallengeData captchaData = _node.MMLC.GetCaptchaChallengeData (header.AuthServers[auth_idx], record.Hash.GetByteArray ());
							return string.Format ("<result status=\"CAPTCHA\"><img>{0}</img><token>{1}</token><prev>{2}</prev></result>",
								Convert.ToBase64String (captchaData.Data), Convert.ToBase64String (captchaData.Token),
								Convert.ToBase64String (Serializer.Instance.Serialize (record)));
						}
					} catch {
						return "<result status=\"ERROR\" code=\"0\" />";
					}
				}
				return "<result status=\"EMPTY\" />";
			}

			List<MergeableFileRecord> records = _node.MMLC.GetRecords (key, out header);
			if (!callByCallback) {
				if (!_fastView || header == null) {
					ManualResetEvent done = new ManualResetEvent (false);
					CometInfo info = new CometInfo (done, req, res, null, DateTime.Now + TimeSpan.FromSeconds (5), ProcessBBS_CometHandler);
					_node.MMLC.StartMerge (key, ProcessBBS_Callback, done);
					return info;
				} else {
					_node.MMLC.StartMerge (key, null, null);
				}
			}
			if (records == null)
				throw new HttpException (HttpStatusCode.NotFound);

			XmlDocument doc = CreateEmptyDocument ();
			doc.DocumentElement.AppendChild (CreateMergeableFileElement (doc, header, records.ToArray ()));
			return _xslTemplate.Render (server, req, res, doc, Path.Combine (DefaultTemplatePath, "bbs_view.xsl"));
		}

		void ProcessBBS_Callback (object sender, MergeDoneCallbackArgs args)
		{
			ManualResetEvent done = args.State as ManualResetEvent;
			if (done.SafeWaitHandle.IsClosed)
				return;
			done.Set ();
		}

		object ProcessBBS_CometHandler (CometInfo info)
		{
			info.WaitHandle.Close ();
			return ProcessBBS (null, info.Request, info.Response, true);
		}

		object ProcessManageTop (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			XmlDocument doc = CreateEmptyDocument ();
			MergeableFileHeader[] headers = _node.MMLC.GetOwnMergeableFiles ();

			foreach (MergeableFileHeader header in headers) {
				doc.DocumentElement.AppendChild (CreateMergeableFileElement (doc, header));
			}

			return _xslTemplate.Render (server, req, res, doc, Path.Combine (DefaultTemplatePath, "manage.xsl"));
		}

		object ProcessManageFile (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			string str_key = req.Url.AbsolutePath.Substring (8);
			Key key;
			try {
				if (str_key.Length == (DefaultAlgorithm.ECDomainBytes + 1) * 2)
					key = Key.Parse (str_key);
				else if (str_key.Length == (DefaultAlgorithm.ECDomainBytes + 1) * 4 / 3)
					key = Key.FromUriSafeBase64String (str_key);
				else
					throw new HttpException (HttpStatusCode.NotFound);
			} catch {
				throw new HttpException (HttpStatusCode.NotFound);
			}

			MergeableFileHeader header;
			if (req.HttpMethod == HttpMethod.POST && req.HasContentBody ()) {
				header = _node.MMLC.GetMergeableFileHeader (key);
				if (header == null)
					throw new HttpException (HttpStatusCode.NotFound);
				NameValueCollection c = HttpUtility.ParseUrlEncodedStringToNameValueCollection (Encoding.ASCII.GetString (req.GetContentBody (MaxRequestBodySize)), Encoding.UTF8);
				AuthServerInfo[] auth_servers = AuthServerInfo.ParseArray (c["auth"]);
				List<Key> list = new List<Key> ();
				string[] keep_array = c.GetValues ("record");
				if (keep_array != null) {
					for (int i = 0; i < keep_array.Length; i ++) {
						list.Add (Key.FromUriSafeBase64String (keep_array[i]));
					}
				}
				IHashComputable new_header_content;
				if (header.Content is SimpleBBSHeader) {
					new_header_content = new SimpleBBSHeader (c["title"]);
				} else {
					throw new HttpException (HttpStatusCode.InternalServerError);
				}
				_node.MMLC.Manage (key, new_header_content, auth_servers, list.ToArray (), null);

				res[HttpHeaderNames.Location] = "/bbs/" + key.ToUriSafeBase64String ();
				throw new HttpException (req.HttpVersion == HttpVersion.Http10 ? HttpStatusCode.Found : HttpStatusCode.SeeOther);
			}

			List<MergeableFileRecord> records = _node.MMLC.GetRecords (key, out header);
			if (header == null || records == null)
				throw new HttpException (HttpStatusCode.NotFound);

			XmlDocument doc = CreateEmptyDocument ();
			doc.DocumentElement.AppendChild (CreateMergeableFileElement (doc, header, records.ToArray ()));

			string xsl_filename;
			if (header.Content is SimpleBBSHeader)
				xsl_filename = "manage-simplebbs.xsl";
			else
				throw new HttpException (HttpStatusCode.InternalServerError);

			return _xslTemplate.Render (server, req, res, doc, Path.Combine (DefaultTemplatePath, xsl_filename));
		}

		object ProcessStatistics (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			return _xslTemplate.Render (server, req, res, CreateStatisticsXML (), Path.Combine (DefaultTemplatePath, "statistics.xsl"));
		}

		public XmlDocument CreateStatisticsXML ()
		{
			XmlDocument doc = CreateEmptyDocument ();
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
				}, null));
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
					}, null),
					doc.CreateElement ("average", new string[][] {
						new string[] {"recv-bytes", (info.TotalReceiveBytes / runningTime).ToString ()},
						new string[] {"recv-packets", (info.TotalReceivePackets / runningTime).ToString ()},
						new string[] {"send-bytes", (info.TotalSendBytes / runningTime).ToString ()},
						new string[] {"send-packets", (info.TotalSendPackets / runningTime).ToString ()},
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
				messaging
			}));
			return doc;
		}

		XmlElement CreateMergeableFileElement (XmlDocument doc, MergeableFileHeader header)
		{
			return CreateMergeableFileElement (doc, header, null);
		}

		XmlElement CreateMergeableFileElement (XmlDocument doc, MergeableFileHeader header, MergeableFileRecord[] records)
		{
			XmlElement root = doc.CreateElement ("file", new string[][] {
				new[] {"key", header.Key.ToUriSafeBase64String ()},
				new[] {"recordset", header.RecordsetHash.ToUriSafeBase64String ()},
				new[] {"created", header.CreatedTime.ToLocalTime().ToString (DefaultDateFormat)},
				new[] {"lastManaged", header.LastManagedTime.ToLocalTime().ToString (DefaultDateFormat)},
				new[] {"lastModified", header.LastModifiedTime.ToLocalTime().ToString (DefaultDateFormat)},
				new[] {"records", header.NumberOfRecords.ToString ()},
			}, null);
			XmlNode authServers = root.AppendChild (doc.CreateElement ("auth-servers"));
			if (header.AuthServers != null && header.AuthServers.Length > 0) {
				for (int i = 0; i < header.AuthServers.Length; i++) {
					authServers.AppendChild (doc.CreateElement ("auth-server", new string[][] {
						new[] {"index", i.ToString ()}
					}, new[]{
						doc.CreateElement ("public-key", null, new[]{doc.CreateTextNode (header.AuthServers[i].PublicKey.ToBase64String ())}),
						doc.CreateElement ("serialize", null, new[]{doc.CreateTextNode (header.AuthServers[i].ToParsableString ())})
					}));
				}
			}
			if (header.Content is SimpleBBSHeader) {
				root.SetAttribute ("type", "simple-bbs");
				SimpleBBSHeader content = header.Content as SimpleBBSHeader;
				root.AppendChild (doc.CreateElement ("bbs", null, new[] {
					doc.CreateElement ("title", null, new[] {
						doc.CreateTextNodeSafe (content.Title)
					})
				}));
			}

			if (records == null)
				return root;

			XmlElement records_element = (XmlElement)root.AppendChild (doc.CreateElement ("records"));
			foreach (MergeableFileRecord record in records) {
				XmlElement record_element = (XmlElement)records_element.AppendChild (doc.CreateElement ("record", new string[][] {
					new[] {"hash", record.Hash.ToUriSafeBase64String ()},
					new[] {"authidx", record.AuthorityIndex.ToString ()},
					new[] {"created", record.CreatedTime.ToLocalTime().ToString (DefaultDateFormat)}
				}, null));
				if (record.Content is SimpleBBSRecord) {
					record_element.SetAttribute ("type", "simple-bbs");
					SimpleBBSRecord record_content = record.Content as SimpleBBSRecord;
					record_element.AppendChild (doc.CreateElement ("bbs", new string[][] {
						new string[] {"short-id", record_content.GetShortId (record)}
					}, new[] {
						doc.CreateElement ("name", null, new[] {
							doc.CreateTextNodeSafe (record_content.Name)
						}),
						doc.CreateElement ("body", null, new[] {
							doc.CreateTextNodeSafe (record_content.Body)
						})
					}));
				}
			}

			return root;
		}

		object ProcessStaticFile (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			string path = req.Url.AbsolutePath;
			if (path.Contains ("/../"))
				throw new HttpException (HttpStatusCode.BadRequest);
			path = path.Replace ('/', Path.DirectorySeparatorChar).Substring (1);
			path = Path.Combine (DefaultStaticFilePath, path);
			if (!File.Exists (path))
				throw new HttpException (HttpStatusCode.NotFound);
			DateTime lastModified = File.GetLastWriteTimeUtc (path);
			string etag = lastModified.Ticks.ToString ("x");
			res[HttpHeaderNames.ETag] = etag;
			res[HttpHeaderNames.LastModified] = lastModified.ToString ("r");
			if (req.Headers.ContainsKey (HttpHeaderNames.IfNoneMatch) && req.Headers[HttpHeaderNames.IfNoneMatch] == etag)
				throw new HttpException (HttpStatusCode.NotModified);

			res[HttpHeaderNames.ContentType] = MIMEDatabase.GetMIMEType (Path.GetExtension (path));
			bool supportGzip = req.Headers.ContainsKey (HttpHeaderNames.AcceptEncoding) && req.Headers[HttpHeaderNames.AcceptEncoding].Contains("gzip");
			string gzip_path = path + ".gz";
			if (supportGzip && File.Exists (gzip_path) && File.GetLastWriteTimeUtc (gzip_path) >= lastModified) {
				path = gzip_path;
				res[HttpHeaderNames.ContentEncoding] = "gzip";
			}
			using (FileStream strm = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
				byte[] raw = new byte[strm.Length];
				strm.Read (raw, 0, raw.Length);
				return raw;
			}
		}

		public WaitHandle ExitWaitHandle {
			get { return _exitWaitHandle; }
		}
	}
}