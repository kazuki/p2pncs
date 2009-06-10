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
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;
using Kazuki.Net.HttpServer;
using openCrypto.EllipticCurve;
using p2pncs.Net;
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.Anonymous;
using p2pncs.Security.Cryptography;

namespace p2pncs
{
	class WebApp : IHttpApplication, IDisposable
	{
		const string DefaultTemplatePath = "templates";
		const string DefaultStaticFilePath = "htdocs";

		static byte[] EmptyData = new byte[0];
		const int MaxRetries = 2;
		static TimeSpan Timeout = TimeSpan.FromSeconds (2);
		const int RetryBufferSize = 512;
		const int DuplicationCheckBufferSize = 512;
		const int MaxStreamSocketSegmentSize = 500;

		Node _node;
		ManualResetEvent _exitWaitHandle = new ManualResetEvent (false);
		XslTemplate _xslCache = new XslTemplate ();
		int _roomId = 1;
		Dictionary<int, ChatRoomInfo> _rooms = new Dictionary<int, ChatRoomInfo> ();
		Dictionary<Key, int> _joinRooms = new Dictionary<Key, int> ();
		long _rev = 1;
		XmlDocument _cometLogDoc = new XmlDocument ();
		List<ManualResetEvent> _cometWaits = new List<ManualResetEvent> ();
		string _name;
		Key _imPubKey;
		ISubscribeInfo _imSubscribe;
		SubscribeRouteStatus _imLastStatus = SubscribeRouteStatus.Establishing;
		List<KeyValuePair<IStreamSocket, DateTime>> _throughputTestSockets = new List<KeyValuePair<IStreamSocket,DateTime>> ();
		Interrupters _ints;

		public WebApp (Node node, Key imPubKey, ECKeyPair imPrivateKey, string name, Interrupters ints)
		{
			_node = node;
			_imPubKey = imPubKey;
			_name = name;
			_ints = ints;
			node.AnonymousRouter.SubscribeRecipient (imPubKey, imPrivateKey);
			_imSubscribe = node.AnonymousRouter.GetSubscribeInfo (imPubKey);
			ints.WebAppInt.AddInterruption (CheckUpdate);
			node.AnonymousRouter.Accepting += delegate (object sender, AcceptingEventArgs e) {
				ChatRoomInfo selected = null;
				lock (_rooms) {
					foreach (ChatRoomInfo room in _rooms.Values) {
						if (room.IsOwner && room.RoomKey.Equals (e.RecipientId)) {
							selected = room;
							break;
						}
					}
				}
				if (selected != null) {
					e.Accept (selected.RoomName, selected);
				} else {
					if (e.Payload is string && ((string)e.Payload) == "ThroughputTest") {
						e.Accept (null, null);
					} else {
						e.Reject ();
					}
				}
			};
			node.AnonymousRouter.Accepted += delegate (object sender, AcceptedEventArgs e) {
				ChatRoomInfo selected = e.State as ChatRoomInfo;
				if (selected != null) {
					selected.AcceptedClient (e.Socket, e.DestinationId, e.Payload);
					return;
				}
				StreamSocket sock = new StreamSocket (e.Socket, AnonymousRouter.DummyEndPoint, MaxStreamSocketSegmentSize, _ints.StreamSocketTimeoutInt);
				e.Socket.InitializedEventHandlers ();
				lock (_throughputTestSockets) {
					_throughputTestSockets.Add (new KeyValuePair<IStreamSocket, DateTime> (sock, DateTime.Now + TimeSpan.FromMinutes (5)));
				}
				Console.WriteLine ("Accepted ThroughputTest");
			};
		}

		public void Dispose ()
		{
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

			lock (_throughputTestSockets) {
				for (int i = 0; i < _throughputTestSockets.Count; i ++) {
					if (_throughputTestSockets[i].Value <= DateTime.Now) {
						try {
							_throughputTestSockets[i].Key.Dispose ();
						} catch {}
						_throughputTestSockets.RemoveAt (i --);
					}
				}
			}
		}

		public object Process (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			if (req.Url.AbsolutePath == "/") {
				return ProcessMainPage (server, req, res);
			} else if (req.Url.AbsolutePath == "/api") {
				if (req.HttpMethod == HttpMethod.POST)
					return ProcessAPI_POST (server, req, res);
				return ProcessAPI_GET (server, req, res);
			}

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
			return _xslCache.Process (req, res, doc, Path.Combine (DefaultTemplatePath, "main.xsl"));
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
			switch (Helpers.GetQueryValue (req, "method")) {
				case "exit": {
					_exitWaitHandle.Set ();
					return EmptyData;
				}
				case "connect": {
					System.Net.EndPoint ep = Helpers.Parse (Helpers.GetQueryValue (req, "data"));
					if (ep != null)
						_node.KeyBasedRouter.Join (new System.Net.EndPoint[] {ep});
					return EmptyData;
				}
				case "create_room": {
					string name = Helpers.GetQueryValue (req, "data");
					if (name.Length == 0)
						break;
					ECKeyPair roomPrivateKey = ECKeyPair.Create (Node.DefaultECDomainName);
					Key roomPublicKey = Key.Create (roomPrivateKey);
					_node.AnonymousRouter.SubscribeRecipient (roomPublicKey, roomPrivateKey);
					ISubscribeInfo info = _node.AnonymousRouter.GetSubscribeInfo (roomPublicKey);
					int roomId = Interlocked.Increment (ref _roomId);
					ChatRoomInfo ownerInfo = new ChatRoomInfo (this, roomId, name, roomPublicKey, info, null);
					lock (_rooms) {
						_rooms.Add (roomId, ownerInfo);
						_joinRooms.Add (roomPublicKey, roomId);
					}
					IncrementRevisionAndUpdate ();
					return EmptyData;
				}
				case "join_room": {
					Key roomKey = null;
					try {
						roomKey = new Key (Convert.FromBase64String (Helpers.GetQueryValue (req, "data")));
					} catch {
						break;
					}
					try {
						_node.AnonymousRouter.GetSubscribeInfo (roomKey);
						break;
					} catch {}
					lock (_rooms) {
						if (_joinRooms.ContainsKey (roomKey))
							break;
						_joinRooms.Add (roomKey, Interlocked.Increment (ref _roomId));
					}
					_node.AnonymousRouter.BeginConnect (_imPubKey, roomKey, AnonymousConnectionType.LowLatency, _name, JoinRoom_Callback, roomKey);
					IncrementRevisionAndUpdate ();
					return EmptyData;
				}
				case "leave_room": {
					string str_id = Helpers.GetQueryValue (req, "id");
					int id;
					if (!int.TryParse (str_id, out id))
						break;
					ChatRoomInfo roomInfo;
					lock (_rooms) {
						if (!_rooms.TryGetValue (id, out roomInfo))
							break;
						_rooms.Remove (id);
						_joinRooms.Remove (roomInfo.RoomKey);
					}
					roomInfo.Close ();
					return EmptyData;
				}
				case "post": {
					string str_id = Helpers.GetQueryValue (req, "id");
					string msg = Helpers.GetQueryValue (req, "msg").TrimEnd ();
					int id;
					if (!int.TryParse (str_id, out id) || msg.Length == 0)
						break;
					ChatRoomInfo roomInfo;
					lock (_rooms) {
						if (!_rooms.TryGetValue (id, out roomInfo))
							break;
					}
					roomInfo.Post (msg);
					return EmptyData;
				}
				case "throughput_test": {
					Key destKey = null;
					try {
						destKey = new Key (Convert.FromBase64String (Helpers.GetQueryValue (req, "data")));
					} catch {
						break;
					}
					ThreadPool.QueueUserWorkItem (ThroughputTest, destKey);
					return EmptyData;
				}
			}
			throw new HttpException (HttpStatusCode.NotFound);
		}

		void ThroughputTest (object o)
		{
			Key destKey = o as Key;
			IAsyncResult ar = _node.AnonymousRouter.BeginConnect (_imPubKey, destKey, AnonymousConnectionType.HighThroughput, "ThroughputTest", null, null);
			IAnonymousSocket sock = null;
			try {
				sock = _node.AnonymousRouter.EndConnect (ar);
			} catch {
				Console.WriteLine ("ThroughputTest: Connect failed");
				return;
			}

			StreamSocket ssock = new StreamSocket (sock, AnonymousRouter.DummyEndPoint, MaxStreamSocketSegmentSize, _ints.StreamSocketTimeoutInt);
			sock.InitializedEventHandlers ();
			try {
				byte[] buffer = new byte[1000 * 1000];
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
			if (supportGzip && File.Exists (path + ".gz")) {
				path = path + ".gz";
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
					room_root.SetAttribute ("id", ID.ToString ("x"));
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

		[SerializableTypeId (0x400)]
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

		[SerializableTypeId (0x401)]
		class ACK
		{
			public static ACK Instance = new ACK ();
		}

		[SerializableTypeId (0x402)]
		class LeaveMessage
		{
			public static LeaveMessage Instance = new LeaveMessage ();
		}
	}
}
