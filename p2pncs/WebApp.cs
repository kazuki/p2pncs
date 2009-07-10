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

namespace p2pncs
{
	class WebApp : IHttpApplication, IDisposable
	{
		const string DefaultTemplatePath = "templates";
		const string DefaultStaticFilePath = "htdocs";

		const int MaxRetries = 2;
		static TimeSpan Timeout = TimeSpan.FromSeconds (2);
		const int RetryBufferSize = 512;
		const int DuplicationCheckBufferSize = 512;
		const int MaxStreamSocketSegmentSize = 500;
		const int MaxRequestBodySize = 33554432; // 32MB

		Node _node;
		ManualResetEvent _exitWaitHandle = new ManualResetEvent (false);
		XslTemplateEngine _xslTemplate = new XslTemplateEngine ();
		int _roomId = 1;
		Dictionary<int, ChatRoomInfo> _rooms = new Dictionary<int, ChatRoomInfo> ();
		Dictionary<Key, int> _joinRooms = new Dictionary<Key, int> ();
		long _rev = 1;
		List<ManualResetEvent> _cometWaits = new List<ManualResetEvent> ();
		string _name;
		Key _imPubKey;
		ISubscribeInfo _imSubscribe;
		SubscribeRouteStatus _imLastStatus = SubscribeRouteStatus.Establishing;
		Interrupters _ints;
		bool _fastView = true;

		public WebApp (Node node, Key imPubKey, ECKeyPair imPrivateKey, string name, Interrupters ints)
		{
			_node = node;
			_imPubKey = imPubKey;
			_name = name;
			_ints = ints;
			node.MMLC.Register (SimpleBBSParser.Instance);
			_imSubscribe = node.AnonymousRouter.SubscribeRecipient (imPubKey, imPrivateKey);
			ints.WebAppInt.AddInterruption (CheckUpdate);
			_imSubscribe.Accepting += delegate (object sender, AcceptingEventArgs e) {
				if (e.Payload is string && ((string)e.Payload) == "ThroughputTest") {
					e.Accept (null, null);
				} else {
					e.Reject ();
				}
			};
			_imSubscribe.Accepted += delegate (object sender, AcceptedEventArgs e) {
				StreamSocket sock = new StreamSocket (e.Socket, AnonymousRouter.DummyEndPoint, MaxStreamSocketSegmentSize, _ints.StreamSocketTimeoutInt);
				e.Socket.InitializedEventHandlers ();
				ThreadPool.QueueUserWorkItem (ThroughputTest_ReceiverSide, sock);
				Console.WriteLine ("Accepted ThroughputTest");
			};
		}

		public void Dispose ()
		{
			_ints.WebAppInt.RemoveInterruption (CheckUpdate);
			lock (_rooms) {
				foreach (ChatRoomInfo room in _rooms.Values)
					room.Close ();
			}
			_node.AnonymousRouter.UnsubscribeRecipient (_imPubKey);
		}

		void CheckUpdate ()
		{
			bool updated = false;
			lock (_rooms) {
				foreach (ChatRoomInfo room in _rooms.Values)
					updated |= room.UpdateCheck ();
			}
			if (_imSubscribe.Status != _imLastStatus) {
				_imLastStatus = _imSubscribe.Status;
				updated = true;
			}

			if (updated)
				IncrementRevisionAndUpdate ();
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
			XmlElement rootNode = doc.DocumentElement;
			XmlElement node = doc.CreateElement ("name");
			node.AppendChild (doc.CreateTextNode (_name));
			rootNode.AppendChild (node);
			node = doc.CreateElement ("key");
			node.AppendChild (doc.CreateTextNode (Convert.ToBase64String (_imPubKey.GetByteArray())));
			rootNode.AppendChild (node);
			rootNode.SetAttribute ("ver", System.Reflection.Assembly.GetCallingAssembly ().GetName ().Version.ToString ());
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
						EndPoint ep = Helpers.Parse (lines[i]);
						if (ep != null) {
							list.Add (ep);
							raw_list.Add (lines[i]);
						}
					}
					if (list.Count > 0) {
						_node.KeyBasedRouter.Join (list.ToArray ());
						XmlNode root = doc.DocumentElement.AppendChild (doc.CreateElement ("connected"));
						for (int i = 0; i < list.Count; i ++) {
							XmlElement element = doc.CreateElement ("endpoint");
							element.AppendChild (doc.CreateTextNode (raw_list[i].ToString ()));
							root.AppendChild (element);
						}
					}
				}
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
						record = new MergeableFileRecord (new SimpleBBSRecord (fpname, fpbody, DateTime.UtcNow), DateTime.UtcNow, null, null, byte.MaxValue, new byte[DefaultAlgorithm.ECDomainBytes + 1]);
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
						MergeableFileHeader header = new MergeableFileHeader (ECKeyPair.Create (DefaultAlgorithm.ECDomainName), DateTime.UtcNow, header2, auth_servers);
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
				SimpleBBSHeader simpleBBS = header.Content as SimpleBBSHeader;
				XmlElement e1 = doc.CreateElement ("bbs");
				e1.SetAttribute ("key", header.Key.ToUriSafeBase64String ());
				e1.SetAttribute ("recordset", header.RecordsetHash.ToString ());
				XmlElement title = doc.CreateElement ("title");
				title.AppendChild (doc.CreateTextNode (simpleBBS.Title));
				e1.AppendChild (title);
				rootNode.AppendChild (e1);
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
							_node.MMLC.AppendRecord (key, new MergeableFileRecord (new SimpleBBSRecord (name, body, DateTime.UtcNow), header.LastManagedTime, null, null, 0, null));
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
									_node.MMLC.AppendRecord (key, record);
									return "<result status=\"OK\" />";
								}
							}

							record = new MergeableFileRecord (new SimpleBBSRecord (name, body, DateTime.UtcNow), header.LastManagedTime, null, null, 0, null);
							CaptchaChallengeData captchaData = _node.MMLC.GetCaptchaChallengeData (header.AuthServers[auth_idx], record.Hash.GetByteArray ());
							return string.Format ("<result status=\"CAPTCHA\"><img>{0}</img><token>{1}</token><prev>{2}</prev></result>",
								Convert.ToBase64String (captchaData.Data), Convert.ToBase64String (captchaData.Token),
								Convert.ToBase64String (Serializer.Instance.Serialize (record)));
						}
					} catch {
						return "<result status=\"ERROR\" />";
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

		XmlElement CreateMergeableFileElement (XmlDocument doc, MergeableFileHeader header)
		{
			return CreateMergeableFileElement (doc, header, null);
		}

		XmlElement CreateMergeableFileElement (XmlDocument doc, MergeableFileHeader header, MergeableFileRecord[] records)
		{
			XmlElement root = doc.CreateElement ("file", new string[][] {
				new[] {"key", header.Key.ToUriSafeBase64String ()},
				new[] {"recordset", header.RecordsetHash.ToUriSafeBase64String ()}
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
						doc.CreateTextNode (content.Title)
					})
				}));
			}

			if (records == null)
				return root;

			XmlElement records_element = (XmlElement)root.AppendChild (doc.CreateElement ("records"));
			foreach (MergeableFileRecord record in records) {
				XmlElement record_element = (XmlElement)records_element.AppendChild (doc.CreateElement ("record", new string[][] {
					new[] {"hash", record.Hash.ToUriSafeBase64String ()},
					new[] {"authidx", record.AuthorityIndex.ToString ()}
				}, null));
				if (record.Content is SimpleBBSRecord) {
					record_element.SetAttribute ("type", "simple-bbs");
					SimpleBBSRecord record_content = record.Content as SimpleBBSRecord;
					record_element.AppendChild (doc.CreateElement ("bbs", null, new[] {
						doc.CreateElement ("name", null, new[] {
							doc.CreateTextNode (record_content.Name)
						}),
						doc.CreateElement ("body", null, new[] {
							doc.CreateTextNode (record_content.Body)
						}),
						doc.CreateElement ("posted", null, new[] {
							doc.CreateTextNode (record_content.PostedTime.ToString ("yyyy/MM/dd HH:mm:ss"))
						})
					}));
				}
			}

			return root;
		}

		object ProcessAPI_GET (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			switch (Helpers.GetQueryValue (req, "method")) {
				case "log":
					long rev;
					if (!long.TryParse (Helpers.GetQueryValue (req, "rev"), out rev) || rev != Interlocked.Read (ref _rev))
						return LogCometHandler (new CometInfo (null, req, res, new CometState (0), DateTime.Now, null));

					ManualResetEvent done = new ManualResetEvent (false);
					lock (_cometWaits) {
						_cometWaits.Add (done);
					}
					return new CometInfo (done, req, res, new CometState (rev), DateTime.Now + TimeSpan.FromSeconds (10), LogCometHandler);
			}
			return null;
		}
		
		object ReturnXML (XmlDocument doc, HttpResponseHeader res, bool noCache)
		{
			StringBuilder sb = new StringBuilder ();
			using (StringWriter base_writer = new StringWriter (sb))
			using (XmlTextWriter writer = new XmlTextWriter (base_writer)) {
				doc.WriteTo (writer);
			}
			res[HttpHeaderNames.ContentType] = "text/xml; charset=UTF-8";
			if (noCache)
				res[HttpHeaderNames.CacheControl] = "no-cache";
			return sb.ToString ();
		}

		object LogCometHandler (CometInfo info)
		{
			long rev = Interlocked.Read (ref _rev);
			CometState state = (CometState)info.Context;
			XmlDocument doc = new XmlDocument ();
			doc.AppendChild (doc.CreateElement ("log"));
			doc.DocumentElement.SetAttribute ("rev", rev.ToString ());
			doc.DocumentElement.SetAttribute ("status", _imLastStatus.ToString ());
			WriteChatRoomInfo (state.Revision, doc.DocumentElement);
			return ReturnXML (doc, info.Response, true);
		}

		object ProcessAPI_POST (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			const string OK = "OK";
			switch (Helpers.GetQueryValue (req, "method")) {
				case "exit": {
					_exitWaitHandle.Set ();
					return OK;
				}
				case "connect": {
					System.Net.EndPoint ep = Helpers.Parse (Helpers.GetQueryValue (req, "data"));
					if (ep != null) {
						_node.KeyBasedRouter.Join (new System.Net.EndPoint[] {ep});
						return OK;
					} else {
						return "対応していない書式で入力したか、DNSエラーのため接続できませんでした";
					}
				}
				case "create_room": {
					string name = Helpers.GetQueryValue (req, "data").Trim ();
					if (name.Length == 0)
						return "部屋名に何か文字を入力してください";
					ECKeyPair roomPrivateKey = ECKeyPair.Create (DefaultAlgorithm.ECDomainName);
					Key roomPublicKey = Key.Create (roomPrivateKey);
					ISubscribeInfo info = _node.AnonymousRouter.SubscribeRecipient (roomPublicKey, roomPrivateKey);
					int roomId = Interlocked.Increment (ref _roomId);
					ChatRoomInfo ownerInfo = new ChatRoomInfo (this, roomId, name, roomPublicKey, info, null);
					lock (_rooms) {
						_rooms.Add (roomId, ownerInfo);
						_joinRooms.Add (roomPublicKey, roomId);
					}
					IncrementRevisionAndUpdate ();
					return OK;
				}
				case "join_room": {
					Key roomKey = null;
					try {
						roomKey = new Key (Convert.FromBase64String (Helpers.GetQueryValue (req, "data")));
					} catch {
						return "解析できないIDです";
					}
					try {
						_node.AnonymousRouter.GetSubscribeInfo (roomKey);
						return "自分がオーナとなっている部屋または無効な部屋IDのためこの部屋には接続できません";
					} catch {}
					lock (_rooms) {
						if (_joinRooms.ContainsKey (roomKey))
							return "現在接続中です...";
						_joinRooms.Add (roomKey, Interlocked.Increment (ref _roomId));
					}
					_node.AnonymousRouter.BeginConnect (_imPubKey, roomKey, AnonymousConnectionType.LowLatency, _name, JoinRoom_Callback, roomKey);
					IncrementRevisionAndUpdate ();
					return OK;
				}
				case "leave_room": {
					string str_id = Helpers.GetQueryValue (req, "id");
					int id;
					if (!int.TryParse (str_id, out id))
						return "解析できないIDです";
					ChatRoomInfo roomInfo;
					lock (_rooms) {
						if (!_rooms.TryGetValue (id, out roomInfo))
							return "既に退室した部屋または、無効なIDです";
						_rooms.Remove (id);
						_joinRooms.Remove (roomInfo.RoomKey);
					}
					roomInfo.Close ();
					return OK;
				}
				case "post": {
					string str_id = Helpers.GetQueryValue (req, "id");
					string msg = Helpers.GetQueryValue (req, "msg").TrimEnd ();
					int id;
					if (!int.TryParse (str_id, out id) || msg.Length == 0)
						return "無効なIDか、発言が空です";
					ChatRoomInfo roomInfo;
					lock (_rooms) {
						if (!_rooms.TryGetValue (id, out roomInfo))
							return "既に退室した部屋または、無効なIDです";
					}
					roomInfo.Post (msg);
					return OK;
				}
				case "throughput_test": {
					Key destKey = null;
					try {
						destKey = new Key (Convert.FromBase64String (Helpers.GetQueryValue (req, "data")));
					} catch {
						return "認識できない宛先です";
					}
					ThreadPool.QueueUserWorkItem (ThroughputTest, destKey);
					return OK;
				}
				case "create_bbs": {
					string title = Helpers.GetQueryValue (req, "title").Trim ();
					if (title.Length == 0)
						return "タイトルに何か文字を入力してください";
					_node.MMLC.CreateNew (new SimpleBBSHeader (title), AuthServerInfo.ParseArray ("A/fRkcaUdXFlw0GAmlwfuFzo20SRCEGkkr6rWG0jko9M;i;127.0.0.1:51423"), null);
					return OK;
				}
				default:
					return "Unknown Method";
			}
		}

		void ThroughputTest (object o)
		{
			Key destKey = o as Key;
			DateTime dt = DateTime.Now;
			IAnonymousSocket sock = null;
			try {
				IAsyncResult ar = _node.AnonymousRouter.BeginConnect (_imPubKey, destKey, AnonymousConnectionType.HighThroughput, "ThroughputTest", null, null);
				sock = _node.AnonymousRouter.EndConnect (ar);
				Console.WriteLine ("ThroughputTest: Connected to {0}", Convert.ToBase64String (destKey.GetByteArray ()));
			} catch {
				Console.WriteLine ("ThroughputTest: Connect failed");
				return;
			}

			StreamSocket ssock = new StreamSocket (sock, AnonymousRouter.DummyEndPoint, MaxStreamSocketSegmentSize, DateTime.Now - dt, _ints.StreamSocketTimeoutInt);
			sock.InitializedEventHandlers ();
			try {
				const int SendSize = 1000 * 1000;
				const int HeaderSize = 24;
				byte[] buffer = new byte[HeaderSize + SendSize];
				new Random ().NextBytes (buffer);
				buffer[0] = (byte)((SendSize >> 24) & 0xff);
				buffer[1] = (byte)((SendSize >> 16) & 0xff);
				buffer[2] = (byte)((SendSize >>  8) & 0xff);
				buffer[3] = (byte)((SendSize >>  0) & 0xff);
				byte[] hash = new System.Security.Cryptography.SHA1Managed ().ComputeHash (buffer, HeaderSize, SendSize);
				Buffer.BlockCopy (hash, 0, buffer, 4, hash.Length);

				Stopwatch sw = Stopwatch.StartNew ();
				ssock.Send (buffer, 0, buffer.Length);
				ssock.Shutdown ();
				sw.Stop ();
				Console.WriteLine ("ThroughputTest: {0:f2}Mbps", buffer.Length * 8 / 1000.0 / 1000.0 / sw.Elapsed.TotalSeconds);
				ssock.Dispose ();
			} catch (Exception ex) {
				Console.WriteLine ("ThroughputTest: Timeout");
				Console.WriteLine (ex.ToString ());
				return;
			}
		}

		void ThroughputTest_ReceiverSide (object o)
		{
			const int HeaderSize = 24;
			StreamSocket sock = (StreamSocket)o;
			TimeSpan timeout = TimeSpan.FromSeconds (16);
			byte[] buffer = new byte[64], hash = new byte[20];
			bool in_header = true;
			int received = 0, size = -1;
			
			try {
				while (true) {
					if (in_header) {
						received += sock.Receive (buffer, received, HeaderSize - received);
						if (received == HeaderSize) {
							in_header = false;
							size = (buffer[0] << 24) | (buffer[1] << 16) | (buffer[2] << 8) | buffer[3];
							Buffer.BlockCopy (buffer, 4, hash, 0, 20);
							received = 0;
							buffer = new byte[size];
							Console.WriteLine ("ThroughputTest: Received Header");
							Console.WriteLine ("ThroughputTest:   TestSize={0}", size);
							Console.WriteLine ("ThroughputTest:   Hash={0}", Convert.ToBase64String (hash));
						}
					} else {
						received += sock.Receive (buffer, received, size - received);
						if (received == size)
							break;
					}
				}
				Console.WriteLine ("ThroughputTest: Finished");
				byte[] hash2 = new System.Security.Cryptography.SHA1Managed ().ComputeHash (buffer);
				Console.WriteLine ("ThroughputTest:  Hash'={0}...{1}", Convert.ToBase64String (hash2), Convert.ToBase64String (hash) == Convert.ToBase64String (hash2) ? "OK" : "ERROR");
			} catch {
				Console.WriteLine ("ThroughputTest: ERROR");
			} finally {
				sock.Dispose ();
			}
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

		void JoinRoom_Callback (IAsyncResult ar)
		{
			Key roomKey = ar.AsyncState as Key;
			IAnonymousSocket sock;
			try {
				sock = _node.AnonymousRouter.EndConnect (ar);
			} catch {
				lock (_rooms) {
					_joinRooms.Remove (roomKey);
				}
				IncrementRevisionAndUpdate ();
				return;
			}

			IMessagingSocket msock = new MessagingSocket (sock, true, SymmetricKey.NoneKey, Serializer.Instance,
				null, _ints.AnonymousMessagingInt, WebApp.Timeout, WebApp.MaxRetries, WebApp.RetryBufferSize, WebApp.DuplicationCheckBufferSize);
			lock (_rooms) {
				int roomId = _joinRooms[roomKey];
				ChatRoomInfo room = new ChatRoomInfo (this, roomId, (string)sock.PayloadAtEstablishing,
					roomKey, _node.AnonymousRouter.GetSubscribeInfo (_imPubKey), msock);
				_rooms.Add (roomId, room);
			}
			sock.InitializedEventHandlers ();
			IncrementRevisionAndUpdate ();
		}

		void IncrementRevisionAndUpdate ()
		{
			Interlocked.Increment (ref _rev);
			Update ();
		}

		void Update ()
		{
			ManualResetEvent[] waits;
			lock (_cometWaits) {
				waits = _cometWaits.ToArray ();
				_cometWaits.Clear ();
			}
			for (int i = 0; i < waits.Length; i ++)
				waits[i].Set ();
		}

		void WriteChatRoomInfo (long rev, XmlElement root)
		{
			HashSet<Key> joinedSet = new HashSet<Key> ();
			lock (_rooms) {
				foreach (ChatRoomInfo info in _rooms.Values) {
					info.AppendChatEntry (rev, root);
					joinedSet.Add (info.RoomKey);
				}
				foreach (KeyValuePair<Key,int> pair in _joinRooms) {
					if (joinedSet.Contains (pair.Key))
						continue;
					XmlElement joining = root.OwnerDocument.CreateElement ("joining-room");
					joining.SetAttribute ("id", pair.Value.ToString ());
					XmlElement key = root.OwnerDocument.CreateElement ("key");
					key.AppendChild (root.OwnerDocument.CreateTextNode (Convert.ToBase64String (pair.Key.GetByteArray ())));
					joining.AppendChild (key);
					root.AppendChild (joining);
				}
			}
		}

		public WaitHandle ExitWaitHandle {
			get { return _exitWaitHandle; }
		}

		class ChatRoomInfo
		{
			const int MaxLogSize = 1000;
			const string SYSTEM = "りんごの精";
			Queue<ChatPostEntry> _log = new Queue<ChatPostEntry> ();
			SubscribeRouteStatus _lastStatus = SubscribeRouteStatus.Establishing;
			WebApp _app;
			List<IMessagingSocket> _clients = null;
			List<string> _clientNames = null;
			IMessagingSocket _sock = null;

			public ChatRoomInfo (WebApp app, int id, string roomName, Key roomKey, ISubscribeInfo subscribeInfo, IMessagingSocket sock)
			{
				_app = app;
				ID = id;
				RoomName = roomName;
				RoomKey = roomKey;
				SubscribeInfo = subscribeInfo;
				IsOwner = roomKey.Equals (subscribeInfo.Key);
				if (IsOwner) {
					_clients = new List<IMessagingSocket> ();
					_clientNames = new List<string> ();
					subscribeInfo.Accepting += delegate (object sender, AcceptingEventArgs args) {
						args.Accept (roomName, null);
					};
					subscribeInfo.Accepted += delegate (object sender, AcceptedEventArgs args) {
						AcceptedClient (args.Socket, args.DestinationId, args.Payload);
					};
				} else {
					_sock = sock;
					sock.AddInquiredHandler (typeof (ChatMessage), Messaging_Inquired);
					sock.AddInquiredHandler (typeof (LeaveMessage), LeaveMessage_Inquired);
					sock.AddInquiryDuplicationCheckType (typeof (ChatMessage));
					TestLogger.SetupAcMessagingSocket (sock);
				}
			}

			void Messaging_Inquired (object sender, InquiredEventArgs e)
			{
				ChatMessage msg = e.InquireMessage as ChatMessage;
				IMessagingSocket sock = sender as IMessagingSocket;
				sock.StartResponse (e, ACK.Instance);

				if (IsOwner)
					Broadcast (msg.Name, msg.Message, sock);
				AddChatEntry (msg.Name, msg.Message);
			}

			void LeaveMessage_Inquired (object sender, InquiredEventArgs e)
			{
				IMessagingSocket sock = sender as IMessagingSocket;
				if (IsOwner) {
					string name = null;
					lock (_clients) {
						int idx = _clients.IndexOf (sock);
						if (idx >= 0) {
							name = _clientNames[idx];
							_clients.RemoveAt (idx);
							_clientNames.RemoveAt (idx);
						}
					}
					if (name != null) {
						Broadcast (SYSTEM, name + "さんが退室しました", null);
						AddChatEntry (SYSTEM, name + "さんが退室しました");
					}
				} else {
					AddChatEntry (SYSTEM, "部屋のオーナーによりこの部屋が閉じられました");
				}
				sock.Dispose ();
			}

			public void Post (string msg)
			{
				if (IsOwner) {
					Broadcast (_app._name, msg, null);
				} else {
					_sock.BeginInquire (new ChatMessage (_app._name, msg), AnonymousRouter.DummyEndPoint, PostCallback, null);
				}
				AddChatEntry (_app._name, msg);
			}

			void PostCallback (IAsyncResult ar)
			{
				_sock.EndInquire (ar);
			}

			void Broadcast (string name, string msg, IMessagingSocket exclude)
			{
				ChatMessage cm = new ChatMessage (name, msg);
				lock (_clients) {
					for (int i = 0; i < _clients.Count; i ++) {
						if (exclude == null || exclude != _clients[i])
							_clients[i].BeginInquire (cm, AnonymousRouter.DummyEndPoint, Broadcast_Callback, _clients[i]);
					}
				}
			}
			void Broadcast_Callback (IAsyncResult ar)
			{
				IMessagingSocket msock = ar.AsyncState as IMessagingSocket;
				msock.EndInquire (ar);
			}

			void AddChatEntry (string name, string msg)
			{
				long new_rev = Interlocked.Increment (ref _app._rev);
				lock (_log) {
					ChatPostEntry entry = new ChatPostEntry (new_rev, DateTime.Now, name, msg);
					_log.Enqueue (entry);
					while (_log.Count > MaxLogSize)
						_log.Dequeue ();
				}
				_app.Update ();
			}

			public bool UpdateCheck ()
			{
				bool updated = false;
				if (_lastStatus != SubscribeInfo.Status) {
					_lastStatus = SubscribeInfo.Status;
					updated = true;
				}

				return updated;
			}

			public void AppendChatEntry (long rev, XmlElement root)
			{
				List<ChatPostEntry> list = new List<ChatPostEntry> (_log.Count);
				lock (_log) {
					foreach (ChatPostEntry entry in _log) {
						if (entry.Revision > rev)
							list.Add (entry);
					}
				}
				if (list.Count >= 0) {
					XmlDocument doc = root.OwnerDocument;
					XmlElement tmp, tmp2, room_root = doc.CreateElement ("room");
					room_root.SetAttribute ("id", ID.ToString ());
					room_root.SetAttribute ("owner", IsOwner.ToString().ToLower());
					room_root.SetAttribute ("status", SubscribeInfo.Status.ToString ());
					root.AppendChild (room_root);
					tmp = doc.CreateElement ("key");
					tmp.AppendChild (doc.CreateTextNode (Convert.ToBase64String (RoomKey.GetByteArray ())));
					room_root.AppendChild (tmp);
					tmp = doc.CreateElement ("name");
					tmp.AppendChild (doc.CreateTextNode (RoomName));
					room_root.AppendChild (tmp);
					foreach (ChatPostEntry entry in list) {
						tmp = doc.CreateElement ("post");
						tmp.SetAttribute ("rev", entry.Revision.ToString ());
						tmp2 = doc.CreateElement ("name");
						tmp2.AppendChild (doc.CreateTextNode (entry.Name));
						tmp.AppendChild (tmp2);
						tmp2 = doc.CreateElement ("message");
						tmp2.AppendChild (doc.CreateTextNode (entry.Message));
						tmp.AppendChild (tmp2);
						room_root.AppendChild (tmp);
					}
				}
			}

			public void AcceptedClient (IAnonymousSocket sock, Key destId, object payload)
			{
				IMessagingSocket msock = new MessagingSocket (sock, true,
					SymmetricKey.NoneKey, Serializer.Instance, null, _app._ints.AnonymousMessagingInt, WebApp.Timeout,
					WebApp.MaxRetries, WebApp.RetryBufferSize, WebApp.DuplicationCheckBufferSize);
				msock.AddInquiredHandler (typeof (ChatMessage), Messaging_Inquired);
				msock.AddInquiredHandler (typeof (LeaveMessage), LeaveMessage_Inquired);
				msock.AddInquiryDuplicationCheckType (typeof (ChatMessage));
				TestLogger.SetupAcMessagingSocket (msock);
				sock.InitializedEventHandlers ();
				string msg = ((string)payload) + "さんが入室しました";
				Broadcast (SYSTEM, msg, null);
				lock (_clients) {
					_clients.Add (msock);
					_clientNames.Add ((string)payload);
				}
				AddChatEntry (SYSTEM, msg);
			}

			public void Close ()
			{
				if (IsOwner) {
					lock (_clients) {
						for (int i = 0; i < _clients.Count; i ++) {
							_clients[i].BeginInquire (LeaveMessage.Instance, AnonymousRouter.DummyEndPoint, null, null);
							_clients[i].Dispose ();
						}
						_clients.Clear ();
						_clientNames.Clear ();
					}
					_app._node.AnonymousRouter.UnsubscribeRecipient (RoomKey);
				} else {
					try {
						_sock.BeginInquire (LeaveMessage.Instance, AnonymousRouter.DummyEndPoint, null, null);
						_sock.Dispose ();
					} catch {}
				}
			}

			public int ID { get; set; }
			public bool IsOwner { get; set; }
			public string RoomName { get; set; }
			public Key RoomKey { get; set; }
			public ISubscribeInfo SubscribeInfo { get; set; }

			class ChatPostEntry
			{
				public long Revision;
				public DateTime Posted;
				public string Name;
				public string Message;

				public ChatPostEntry (long rev, DateTime posted, string name, string msg)
				{
					Revision = rev;
					Posted = posted;
					Name = name;
					Message = msg;
				}
			}
		}

		class CometState
		{
			public long Revision;

			public CometState (long rev)
			{
				Revision = rev;
			}
		}

		[SerializableTypeId (0x1000)]
		class ChatMessage
		{
			[SerializableFieldId (0)]
			string _name;

			[SerializableFieldId (1)]
			string _msg;

			public ChatMessage (string name, string msg)
			{
				_name = name;
				_msg = msg;
			}

			public string Name {
				get { return _name; }
			}

			public string Message {
				get { return _msg; }
			}
		}

		[SerializableTypeId (0x1001)]
		class ACK
		{
			public static ACK Instance = new ACK ();
		}

		[SerializableTypeId (0x1002)]
		class LeaveMessage
		{
			public static LeaveMessage Instance = new LeaveMessage ();
		}
	}
}