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
using System.Net;
using System.Security.Cryptography;
using System.IO;
using openCrypto;
using openCrypto.EllipticCurve;
using p2pncs.Net.Overlay.DHT;
using p2pncs.Security.Cryptography;
using p2pncs.Threading;
using ECDiffieHellman = openCrypto.EllipticCurve.KeyAgreement.ECDiffieHellman;
using RouteLabel = System.Int32;
using System.Runtime.Serialization;

namespace p2pncs.Net.Overlay.Anonymous
{
	public class AnonymousRouter : IAnonymousRouter
	{
		#region Static Parameters
		static readonly SymmetricAlgorithmPlus DefaultSymmetricAlgorithm;
		const int DefaultSymmetricKeyBits = 128;
		const int DefaultSymmetricBlockBits = 128;

		const int PayloadFixedSize = (DefaultSymmetricKeyBits / 8) * 56; // 896 bytes

		const int DefaultSubscribeRoutes = 3;
		const int DefaultRealyNodes = 3;

		static TimeSpan MaxRRT = TimeSpan.FromMilliseconds (1000); // included cost of cryptography
		static TimeSpan MultipleCipherRouteMaxRoundtripTime = new TimeSpan (MaxRRT.Ticks * DefaultRealyNodes);
		static int MultipleCipherRouteMaxRetry = 1;
		static TimeSpan MultipleCipherRelayTimeout = new TimeSpan (MaxRRT.Ticks * (DefaultRealyNodes - 1));
		static int MultipleCipherRelayMaxRetry = 1;
		static TimeSpan MultipleCipherReverseRelayTimeout = MaxRRT;
		static int MultipleCipherReverseRelayMaxRetry = 1;

		static TimeSpan RelayRouteTimeout = TimeSpan.FromSeconds (30);
		static TimeSpan RelayRouteTimeoutWithMargin = RelayRouteTimeout + (MultipleCipherRouteMaxRoundtripTime + MultipleCipherRouteMaxRoundtripTime);
		static IFormatter DefaultFormatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter ();
		#endregion

		#region Variables
		IMessagingSocket _sock;
		IKeyBasedRouter _kbr;
		IDistributedHashTable _dht;
		ECDiffieHellman _ecdh;
		IntervalInterrupter _interrupter;

		Dictionary<AnonymousEndPoint, RouteInfo> _routingMap = new Dictionary<AnonymousEndPoint,RouteInfo> ();
		ReaderWriterLockWrapper _routingMapLock = new ReaderWriterLockWrapper ();

		Dictionary<Key, SubscribeInfo> _subscribeMap = new Dictionary<Key,SubscribeInfo> ();
		ReaderWriterLockWrapper _subscribeMapLock = new ReaderWriterLockWrapper ();

		Dictionary<Key, List<BoundaryInfo>> _boundMap = new Dictionary<Key, List<BoundaryInfo>> ();
		ReaderWriterLockWrapper _boundMapLock = new ReaderWriterLockWrapper ();

		Dictionary<Type, InquiredEventHandler> _inquireHandlers = new Dictionary<Type,InquiredEventHandler> ();
		#endregion

		static AnonymousRouter ()
		{
			DefaultSymmetricAlgorithm = new CamelliaManaged ();
			DefaultSymmetricAlgorithm.Mode = CipherMode.CBC;
			DefaultSymmetricAlgorithm.Padding = PaddingMode.None;
		}

		public AnonymousRouter (IDistributedHashTable dht, ECKeyPair privateNodeKey, IntervalInterrupter interrupter)
		{
			_kbr = dht.KeyBasedRouter;
			_sock = _kbr.MessagingSocket;
			_dht = dht;
			_ecdh = new ECDiffieHellman (privateNodeKey);
			_interrupter = interrupter;

			_inquireHandlers.Add (typeof (EstablishRouteMessage), MessagingSocket_Inquired_EstablishRouteMessage);
			_inquireHandlers.Add (typeof (RoutedMessage), MessagingSocket_Inquired_RoutedMessage);
			_sock.Inquired += MessagingSocket_Inquired;
			_sock.InquirySuccess += new InquiredEventHandler (MessagingSocket_Success);
			interrupter.AddInterruption (RouteTimeoutCheck);
		}

		#region MessagingSocket
		void MessagingSocket_Inquired (object sender, InquiredEventArgs e)
		{
			Type inqMsgType = e.InquireMessage.GetType ();
			InquiredEventHandler handler;
			if (_inquireHandlers.TryGetValue (inqMsgType, out handler))
				handler (sender, e);
		}

		void MessagingSocket_Inquired_EstablishRouteMessage (object sender, InquiredEventArgs e)
		{
			EstablishRouteMessage msg = (EstablishRouteMessage)e.InquireMessage;
			IMessagingSocket sock = (IMessagingSocket)sender;

			EstablishRoutePayload payload = MultipleCipherHelper.DecryptEstablishPayload (msg.Payload, _ecdh, _kbr.SelftNodeId.KeyBytes);
			if (payload.NextHopEndPoint != null) {
				RouteLabel label = GenerateRouteLabel ();
				Console.WriteLine ("{0}: Received {1} -> (this) -> {3}@{2}", _kbr.SelftNodeId, msg.Label, label, payload.NextHopEndPoint);
				EstablishRouteMessage msg2 = new EstablishRouteMessage (label, payload.NextHopPayload);
				sock.BeginInquire (msg2, payload.NextHopEndPoint, MultipleCipherRelayTimeout, MultipleCipherRelayMaxRetry,
					MessagingSocket_Inquired_EstablishRouteMessage_Callback, new object[] {sock, e, msg, payload, msg2});
			} else {
				Key key = new Key (payload.NextHopPayload);
				Console.WriteLine ("{0}: Received {1} -> (end)\r\n: Subcribe {2}", _kbr.SelftNodeId, msg.Label, key);
				sock.StartResponse (e, new AcknowledgeMessage (MultipleCipherHelper.CreateRoutedPayload (payload.SharedKey, "ACK")));
				BoundaryInfo boundaryInfo = new BoundaryInfo (payload.SharedKey, new AnonymousEndPoint (e.EndPoint, msg.Label));
				RouteInfo info = new RouteInfo (boundaryInfo.Previous, boundaryInfo, payload.SharedKey);
				using (IDisposable cookie = _routingMapLock.EnterWriteLock ()) {
					_routingMap.Add (info.Previous, info);
				}
				using (IDisposable cookie = _boundMapLock.EnterWriteLock ()) {
					List<BoundaryInfo> list;
					if (!_boundMap.TryGetValue (key, out list)) {
						list = new List<BoundaryInfo> (2);
						_boundMap.Add (key, list);
					}
					list.Add (boundaryInfo);
				}

				/// TODO: DHTにキー情報を登録
			}
		}

		void MessagingSocket_Inquired_EstablishRouteMessage_Callback (IAsyncResult ar)
		{
			object[] state = (object[])ar.AsyncState;
			IMessagingSocket sock = (IMessagingSocket)state[0];
			InquiredEventArgs e = (InquiredEventArgs)state[1];
			EstablishRouteMessage msg = (EstablishRouteMessage)state[2];
			EstablishRoutePayload payload = (EstablishRoutePayload)state[3];
			EstablishRouteMessage msg2 = (EstablishRouteMessage)state[4];
			AcknowledgeMessage ack = (AcknowledgeMessage)sock.EndInquire (ar);
			sock.StartResponse (e, new AcknowledgeMessage (MultipleCipherHelper.EncryptRoutedPayload (payload.SharedKey, ack.Payload)));

			RouteInfo info = new RouteInfo (new AnonymousEndPoint (e.EndPoint, msg.Label),
				new AnonymousEndPoint (payload.NextHopEndPoint, msg2.Label), payload.SharedKey);
			using (IDisposable cookie = _routingMapLock.EnterWriteLock ()) {
				_routingMap.Add (info.Previous, info);
				_routingMap.Add (info.Next, info);
			}
		}

		void MessagingSocket_Inquired_RoutedMessage (object sender, InquiredEventArgs e)
		{
			RoutedMessage msg = (RoutedMessage)e.InquireMessage;
			RouteInfo routeInfo;
			using (IDisposable cookie = _routingMapLock.EnterReadLock ()) {
				if (!_routingMap.TryGetValue (new AnonymousEndPoint (e.EndPoint, msg.Label), out routeInfo))
					routeInfo = null;
			}

			if (routeInfo != null) {
				bool direction = (routeInfo.Previous != null && routeInfo.Previous.EndPoint.Equals (e.EndPoint));
				byte[] payload;
				if (direction) {
					// direction: prev -> ! -> next
					routeInfo.ReceiveMessageFromPreviousNode ();
					if (routeInfo.BoundaryInfo == null) {
						payload = MultipleCipherHelper.DecryptRoutedPayload (routeInfo.Key, msg.Payload);
						_sock.BeginInquire (new RoutedMessage (routeInfo.Next.Label, payload), routeInfo.Next.EndPoint,
							MultipleCipherRelayTimeout, MultipleCipherRelayMaxRetry,
							MessagingSocket_Inquired_RoutedMessage_Callback, new object[] {e, routeInfo});
						Console.WriteLine ("{0}: Received RoutedMessage", _kbr.SelftNodeId);
					} else {
						object routedMsg = MultipleCipherHelper.DecryptRoutedPayloadAtEnd (routeInfo.Key, msg.Payload);
						Console.WriteLine ("{0}: Received RoutedMessage (end) : {1}", _kbr.SelftNodeId, routedMsg);

						// Response "ACK" message
						payload = MultipleCipherHelper.CreateRoutedPayload (routeInfo.Key, "ACK");
						_sock.StartResponse (e, new AcknowledgeMessage (payload));
					}
				} else {
					// direction: next -> ! -> prev
					routeInfo.ReceiveMessageFromNextNode ();
					if (routeInfo.StartPointInfo == null) {
						payload = MultipleCipherHelper.EncryptRoutedPayload (routeInfo.Key, msg.Payload);
						_sock.BeginInquire (new RoutedMessage (routeInfo.Previous.Label, payload), routeInfo.Previous.EndPoint,
							MultipleCipherReverseRelayTimeout, MultipleCipherReverseRelayMaxRetry, null, null);
						Console.WriteLine ("{0}: Received RoutedMessage", _kbr.SelftNodeId);
					} else {
						object routedMsg = MultipleCipherHelper.DecryptRoutedPayload (routeInfo.StartPointInfo.RelayNodeKeys, msg.Payload);
						Console.WriteLine ("{0}: Received RoutedMessage (start) : {1}", _kbr.SelftNodeId, routedMsg);
					}
					_sock.StartResponse (e, "ACK");
				}
			} else {
				Console.WriteLine ("{0}: No Route ({1}@{2})", _kbr.SelftNodeId, e.EndPoint, msg.Label);
			}
		}

		void MessagingSocket_Inquired_RoutedMessage_Callback (IAsyncResult ar)
		{
			object[] state = (object[])ar.AsyncState;
			AcknowledgeMessage ack = (AcknowledgeMessage)_sock.EndInquire (ar);
			InquiredEventArgs e = (InquiredEventArgs)state[0];
			RouteInfo routeInfo = (RouteInfo)state[1];
			ack = new AcknowledgeMessage (MultipleCipherHelper.EncryptRoutedPayload (routeInfo.Key, ack.Payload));
			_sock.StartResponse (e, ack);
		}

		void MessagingSocket_Success (object sender, InquiredEventArgs e)
		{
			RoutedMessage msg = e.InquireMessage as RoutedMessage;
			if (msg == null) return;

			RouteInfo routeInfo;
			AnonymousEndPoint aep = new AnonymousEndPoint (e.EndPoint, msg.Label);
			using (IDisposable cookie = _routingMapLock.EnterReadLock ()) {
				if (!_routingMap.TryGetValue (aep, out routeInfo))
					return;
			}

			if (aep.Equals (routeInfo.Next))
				routeInfo.ReceiveMessageFromNextNode ();
			else if (aep.Equals (routeInfo.Previous))
				routeInfo.ReceiveMessageFromPreviousNode ();
		}
		#endregion

		#region TimeoutCheck IntervalInterrupter
		void RouteTimeoutCheck ()
		{
			using (IDisposable cookie = _subscribeMapLock.EnterReadLock ()) {
				foreach (SubscribeInfo info in _subscribeMap.Values)
					info.CheckTimeout ();
			}
			List<Key> emptyList = null;
			using (IDisposable cookie = _boundMapLock.EnterReadLock ()) {
				foreach (KeyValuePair<Key, List<BoundaryInfo>> pair in _boundMap) {
					if (pair.Value.Count == 0) {
						if (emptyList == null)
							emptyList = new List<Key> ();
						emptyList.Add (pair.Key);
					}
				}
			}
			if (emptyList != null) {
				using (IDisposable cookie = _boundMapLock.EnterWriteLock ()) {
					foreach (Key key in emptyList) {
						List<BoundaryInfo> list;
						if (_boundMap.TryGetValue (key, out list) && list.Count == 0)
							_boundMap.Remove (key);
					}
				}
			}

			List<AnonymousEndPoint> timeoutRoutes = null;
			List<RouteInfo> timeoutRouteEnds = null;
			using (IDisposable cookie = _routingMapLock.EnterReadLock ()) {
				HashSet<RouteInfo> checkedList = new HashSet<RouteInfo> ();
				foreach (RouteInfo info in _routingMap.Values) {
					if (!checkedList.Add (info))
						continue;
					if (info.IsExpiry ()) {
						if (timeoutRoutes == null) {
							timeoutRoutes = new List<AnonymousEndPoint> ();
							timeoutRouteEnds = new List<RouteInfo> ();
						}
						if (info.Previous != null) timeoutRoutes.Add (info.Previous);
						if (info.Next != null) timeoutRoutes.Add (info.Next);
						if (info.StartPointInfo != null || info.BoundaryInfo != null)
							timeoutRouteEnds.Add (info);
						Console.WriteLine ("Timeout: {0} <- {1} -> {2}",
							info.Previous == null ? "(null)" : info.Previous.Label.ToString(),
							_kbr.SelftNodeId,
							info.Next == null ? "(null)" : info.Next.Label.ToString ());
					}
				}
			}
			if (timeoutRoutes != null) {
				using (IDisposable cookie = _routingMapLock.EnterWriteLock ()) {
					for (int i = 0; i < timeoutRoutes.Count; i ++)
						_routingMap.Remove (timeoutRoutes[i]);
				}
				for (int i = 0; i < timeoutRouteEnds.Count; i ++) {
					if (timeoutRouteEnds[i].StartPointInfo != null)
						timeoutRouteEnds[i].StartPointInfo.Timeout ();
					if (timeoutRouteEnds[i].BoundaryInfo != null)
						timeoutRouteEnds[i].BoundaryInfo.Timeout ();
				}
			}
		}
		#endregion

		#region IAnonymousRouter Members

		public event AcceptingEventHandler Accepting;

		public event AcceptedEventHandler Accepted;

		public void SubscribeRecipient (Key recipientId, ECKeyPair privateKey)
		{
			SubscribeInfo info;
			using (IDisposable cookie = _subscribeMapLock.EnterWriteLock ()) {
				if (_subscribeMap.ContainsKey (recipientId))
					return;
				info = new SubscribeInfo (this, recipientId, privateKey, _kbr);
				_subscribeMap.Add (recipientId, info);
			}
			info.Start ();
		}

		public void UnsubscribeRecipient (Key recipientId)
		{
			SubscribeInfo info;
			using (IDisposable cookie = _subscribeMapLock.EnterWriteLock ()) {
				if (!_subscribeMap.TryGetValue (recipientId, out info))
					return;
				_subscribeMap.Remove (recipientId);
			}
			info.Close ();
		}

		public IAsyncResult BeginEstablishRoute (Key recipientId, Key destinationId, AsyncCallback callback, object state)
		{
			throw new NotImplementedException ();
		}

		public IAnonymousSocket EndEstablishRoute (IAsyncResult ar)
		{
			throw new NotImplementedException ();
		}

		public void Close ()
		{
			_sock.Inquired -= MessagingSocket_Inquired;
			_interrupter.RemoveInterruption (RouteTimeoutCheck);

			using (IDisposable cookie = _subscribeMapLock.EnterWriteLock ()) {
				foreach (SubscribeInfo info in _subscribeMap.Values)
					info.Close ();
				_subscribeMap.Clear ();
			}
		}

		#endregion

		#region Misc
		static RouteLabel GenerateRouteLabel ()
		{
			byte[] raw = RNG.GetRNGBytes (4);
			return BitConverter.ToInt32 (raw, 0);
		}
		#endregion

		#region SubscribeInfo
		class SubscribeInfo
		{
			bool _active = true;
			float FACTOR = 1.0F;
			int _numOfRoutes = DefaultSubscribeRoutes;
			Key _recipientId;
			IKeyBasedRouter _kbr;
			ECKeyPair _privateKey;
			AnonymousRouter _router;
			object _listLock = new object ();
			List<StartPointInfo> _establishedList = new List<StartPointInfo> ();
			List<StartPointInfo> _establishingList = new List<StartPointInfo> ();

			public SubscribeInfo (AnonymousRouter router, Key id, ECKeyPair privateKey, IKeyBasedRouter kbr)
			{
				_router = router;
				_recipientId = id;
				_privateKey = privateKey;
				_kbr = kbr;
			}

			public void Start ()
			{
				CheckNumberOfEstablishedRoutes ();
			}

			public void CheckTimeout ()
			{
				DateTime border = DateTime.Now - RelayRouteTimeout;
				lock (_listLock) {
					for (int i = 0; i < _establishedList.Count; i ++) {
						if (_establishedList[i].LastSendTime < border)
							_establishedList[i].SendMessage (_kbr.MessagingSocket, Ping.Instance);
					}
				}

				CheckNumberOfEstablishedRoutes ();
			}

			void CheckNumberOfEstablishedRoutes ()
			{
				if (!_active) return;
				lock (_listLock) {
					if (_establishedList.Count < _numOfRoutes) {
						int expectedCount = (int)Math.Ceiling ((_numOfRoutes - _establishedList.Count) * FACTOR);
						int count = expectedCount - _establishingList.Count;
						while (count-- > 0) {
							NodeHandle[] relays = _kbr.RoutingAlgorithm.GetRandomNodes (DefaultRealyNodes);
							if (relays.Length < DefaultRealyNodes) {
								Console.WriteLine ("{0}: Relay node selection failed", _kbr.SelftNodeId);
								return;
							}
							StartPointInfo info = new StartPointInfo (this, relays);
							_establishingList.Add (info);
							byte[] payload = info.CreateEstablishData (_recipientId, _privateKey.DomainName);
							EstablishRouteMessage msg = new EstablishRouteMessage (info.Label, payload);
							_kbr.MessagingSocket.BeginInquire (msg, info.RelayNodes[0].EndPoint,
								MultipleCipherRouteMaxRoundtripTime, MultipleCipherRouteMaxRetry, EstablishRoute_Callback, info);
						}
					}
				}
			}

			void EstablishRoute_Callback (IAsyncResult ar)
			{
				AcknowledgeMessage ack = _kbr.MessagingSocket.EndInquire (ar) as AcknowledgeMessage;
				StartPointInfo startInfo = (StartPointInfo)ar.AsyncState;
				lock (_listLock) {
					_establishingList.Remove (startInfo);
				}
				Console.WriteLine (ack == null ? "ESTABLISH FAILED" : "ESTABLISHED !!");

				if (!_active) return;
				if (ack == null) {
					startInfo.Close ();
					CheckNumberOfEstablishedRoutes ();
				} else {
					object msg = MultipleCipherHelper.DecryptRoutedPayload (startInfo.RelayNodeKeys, ack.Payload);
					RouteInfo routeInfo = new RouteInfo (startInfo, new AnonymousEndPoint (startInfo.RelayNodes[0].EndPoint, startInfo.Label), null);
					lock (_listLock) {
						_establishedList.Add (startInfo);
					}
					using (IDisposable cookie = _router._routingMapLock.EnterWriteLock ()) {
						_router._routingMap.Add (routeInfo.Next, routeInfo);
					}
				}
			}

			public void Close (StartPointInfo start)
			{
				lock (_listLock) {
					start.Close ();
					_establishedList.Remove (start);
				}
				CheckNumberOfEstablishedRoutes ();
			}

			public void Close ()
			{
				_active = false;
			}

			public Key RecipientID {
				get { return _recipientId; }
			}
		}
		#endregion

		#region StartPointInfo
		class StartPointInfo
		{
			NodeHandle[] _relayNodes;
			SymmetricKey[] _relayKeys = null;
			RouteLabel _label;
			DateTime _lastSendTime = DateTime.Now;
			SubscribeInfo _subscribe;

			public StartPointInfo (SubscribeInfo subscribe, NodeHandle[] relayNodes)
			{
				_subscribe = subscribe;
				_relayNodes = relayNodes;
				_label = GenerateRouteLabel ();
			}

			public NodeHandle[] RelayNodes {
				get { return _relayNodes; }
			}

			public SymmetricKey[] RelayNodeKeys {
				get { return _relayKeys; }
			}

			public RouteLabel Label {
				get { return _label; }
			}

			public DateTime LastSendTime {
				get { return _lastSendTime; }
			}

			public void SendMessage (IMessagingSocket sock, object msg)
			{
				byte[] payload = MultipleCipherHelper.CreateRoutedPayload (_relayKeys, msg);
				_lastSendTime = DateTime.Now;
				sock.BeginInquire (new RoutedMessage (_label, payload), _relayNodes[0].EndPoint,
					MultipleCipherRouteMaxRoundtripTime, MultipleCipherRouteMaxRetry,
					SendMessage_Callback, sock);
			}

			void SendMessage_Callback (IAsyncResult ar)
			{
				IMessagingSocket sock = (IMessagingSocket)ar.AsyncState;
				AcknowledgeMessage ack = sock.EndInquire (ar) as AcknowledgeMessage;
				if (ack == null) {
					Timeout ();
					return;
				}

				object msg = MultipleCipherHelper.DecryptRoutedPayload (_relayKeys, ack.Payload);
				Console.WriteLine ("{0}: ACK: {1}", _subscribe.RecipientID, msg);
			}

			public byte[] CreateEstablishData (Key recipientId, ECDomainNames domain)
			{
				if (_relayKeys != null)
					throw new Exception ();
				_relayKeys = new SymmetricKey[_relayNodes.Length];
				return MultipleCipherHelper.CreateEstablishPayload (_relayNodes, _relayKeys, recipientId, domain);
			}

			public void Timeout ()
			{
				_subscribe.Close (this);
			}

			public void Close ()
			{
			}
		}
		#endregion

		#region BoundaryInfo
		class BoundaryInfo
		{
			SymmetricKey _key;
			AnonymousEndPoint _prevEP;
			bool _closed = false;

			public BoundaryInfo (SymmetricKey key, AnonymousEndPoint prev)
			{
				_key = key;
				_prevEP = prev;
			}

			public void SendMessage (IMessagingSocket sock, object msg)
			{
				if (_closed) return;
				byte[] payload = MultipleCipherHelper.CreateRoutedPayload (_key, msg);
				sock.BeginInquire (new RoutedMessage (_prevEP.Label, payload), _prevEP.EndPoint,
					MultipleCipherReverseRelayTimeout, MultipleCipherReverseRelayMaxRetry, null, null);
			}

			public SymmetricKey SharedKey {
				get { return _key; }
			}

			public AnonymousEndPoint Previous {
				get { return _prevEP; }
			}

			public void Timeout ()
			{
				_closed = true;

				/// TODO: DHTからキー情報を削除
			}
		}
		#endregion

		#region RouteInfo
		class RouteInfo
		{
			AnonymousEndPoint _prevEP;
			AnonymousEndPoint _nextEP;
			DateTime _prevExpiry, _nextExpiry;
			SymmetricKey _key;
			StartPointInfo _startPoint;
			BoundaryInfo _boundary;

			public RouteInfo (StartPointInfo startPoint, AnonymousEndPoint next, SymmetricKey key)
				: this (null, next, key, startPoint, null)
			{
			}

			public RouteInfo (AnonymousEndPoint prev, AnonymousEndPoint next, SymmetricKey key)
				: this (prev, next, key, null, null)
			{
			}

			public RouteInfo (AnonymousEndPoint prev, BoundaryInfo boundary, SymmetricKey key)
				: this (prev, null, key, null, boundary)
			{
			}

			RouteInfo (AnonymousEndPoint prev, AnonymousEndPoint next, SymmetricKey key, StartPointInfo startPoint, BoundaryInfo boundary)
			{
				_prevEP = prev;
				_nextEP = next;
				_key = key;
				_startPoint = startPoint;
				_boundary = boundary;

				_prevExpiry = (prev == null ? DateTime.MaxValue : DateTime.Now + RelayRouteTimeoutWithMargin);
				_nextExpiry = (next == null ? DateTime.MaxValue : DateTime.Now + RelayRouteTimeoutWithMargin);
			}

			public AnonymousEndPoint Previous {
				get { return _prevEP; }
			}

			public AnonymousEndPoint Next {
				get { return _nextEP; }
			}

			public SymmetricKey Key {
				get { return _key; }
			}

			public StartPointInfo StartPointInfo {
				get { return _startPoint; }
			}

			public BoundaryInfo BoundaryInfo {
				get { return _boundary; }
			}

			public void ReceiveMessageFromNextNode ()
			{
				_nextExpiry = DateTime.Now + RelayRouteTimeoutWithMargin;
			}

			public void ReceiveMessageFromPreviousNode ()
			{
				_prevExpiry = DateTime.Now + RelayRouteTimeoutWithMargin;
			}

			public bool IsExpiry ()
			{
				return _nextExpiry < DateTime.Now || _prevExpiry < DateTime.Now;
			}
		}
		#endregion

		#region MultipleCipherHelper
		static class MultipleCipherHelper
		{
			static byte[] SerializeEndPoint (EndPoint ep)
			{
				IPEndPoint ipep = (IPEndPoint)ep;
				byte[] rawAdrs = ipep.Address.GetAddressBytes ();
				byte[] ret = new byte[1 + rawAdrs.Length + 2];
				ret[0] = (byte)(ret.Length - 1);
				Buffer.BlockCopy (rawAdrs, 0, ret, 1, rawAdrs.Length);
				ret[rawAdrs.Length + 1] = (byte)((ipep.Port >> 8) & 0xff);
				ret[rawAdrs.Length + 2] = (byte)(ipep.Port & 0xff);
				return ret;
			}

			static EndPoint DeserializeEndPoint (byte[] buffer, int offset, out int bytes)
			{
				bytes = buffer[offset] + 1;
				if (buffer.Length - offset < bytes)
					throw new FormatException ();
				byte[] rawAdrs = new byte[bytes - 3];
				Buffer.BlockCopy (buffer, offset + 1, rawAdrs, 0, rawAdrs.Length);
				IPAddress adrs = new IPAddress (rawAdrs);
				int port = (buffer[offset + bytes - 2] << 8) | buffer[offset + bytes - 1];
				return new IPEndPoint (adrs, port);
			}

			public static byte[] CreateEstablishPayload (NodeHandle[] relayNodes, SymmetricKey[] relayKeys, Key recipientId, ECDomainNames domain)
			{
				byte[] iv = new byte[DefaultSymmetricBlockBits / 8];
				byte[] key = new byte[DefaultSymmetricKeyBits / 8];

				int payload_size = recipientId.KeyBytes + 1;
				byte[] payload = new byte[PayloadFixedSize];

				RNG.Instance.GetBytes (payload);
				payload[0] = 0;
				recipientId.CopyTo (payload, 1);

				for (int i = relayKeys.Length - 1; i >= 0; i--) {
					if (payload_size % iv.Length != 0)
						payload_size += iv.Length - (payload_size % iv.Length);

					ECDiffieHellman ecdh = new ECDiffieHellman (domain);
					byte[] shared = ecdh.PerformKeyAgreement (relayNodes[i].NodeID.ToECPublicKey (domain), iv.Length + key.Length);
					Buffer.BlockCopy (shared, 0, iv, 0, iv.Length);
					Buffer.BlockCopy (shared, iv.Length, key, 0, key.Length);
					relayKeys[i] = new SymmetricKey (DefaultSymmetricAlgorithm, (byte[])iv.Clone (), (byte[])key.Clone (), false);
					byte[] cipher = relayKeys[i].Encrypt (payload, 0, payload_size);
					byte[] pubKey = ecdh.Parameters.ExportPublicKey (true);
					if (i == 0) {
						Buffer.BlockCopy (pubKey, 0, payload, 0, pubKey.Length);
						Buffer.BlockCopy (cipher, 0, payload, pubKey.Length, cipher.Length);
					} else {
						payload[0] = 1;
						byte[] nextHop = SerializeEndPoint (relayNodes[i].EndPoint);
						int bytes;
						EndPoint tmp = DeserializeEndPoint (nextHop, 0, out bytes);
						if (bytes != nextHop.Length)
							throw new Exception ();
						if (!tmp.Equals (relayNodes[i].EndPoint))
							throw new Exception ();
						Buffer.BlockCopy (nextHop, 0, payload, 1, nextHop.Length);
						Buffer.BlockCopy (pubKey, 0, payload, 1 + nextHop.Length, pubKey.Length);
						Buffer.BlockCopy (cipher, 0, payload, 1 + nextHop.Length + pubKey.Length, cipher.Length);
						payload_size = 1 + nextHop.Length + pubKey.Length + cipher.Length;
					}
				}

				return payload;
			}

			public static EstablishRoutePayload DecryptEstablishPayload (byte[] payload, ECDiffieHellman dh, int pubKeyLen)
			{
				byte[] iv = new byte[DefaultSymmetricBlockBits / 8];
				byte[] key = new byte[DefaultSymmetricKeyBits / 8];

				byte[] pubKey = new byte[pubKeyLen];
				Buffer.BlockCopy (payload, 0, pubKey, 0, pubKeyLen);
				
				byte[] shared = dh.PerformKeyAgreement (pubKey, iv.Length + key.Length);
				Buffer.BlockCopy (shared, 0, iv, 0, iv.Length);
				Buffer.BlockCopy (shared, iv.Length, key, 0, key.Length);
				SymmetricKey sharedKey = new SymmetricKey (DefaultSymmetricAlgorithm, iv, key, false);

				int len = payload.Length - pubKeyLen;
				if (len % iv.Length != 0) len -= len % iv.Length;
				byte[] decrypted = sharedKey.Decrypt (payload, pubKeyLen, len);

				switch (decrypted[0]) {
					case 0:
						byte[] rawKey = new byte[pubKeyLen];
						Buffer.BlockCopy (decrypted, 1, rawKey, 0, rawKey.Length);
						return new EstablishRoutePayload (null, rawKey, sharedKey);
					case 1:
						byte[] new_payload = new byte[PayloadFixedSize];
						RNG.Instance.GetBytes (new_payload);
						EndPoint nextHop = DeserializeEndPoint (decrypted, 1, out len);
						Buffer.BlockCopy (decrypted, 1 + len, new_payload, 0, decrypted.Length - 1 - len);
						return new EstablishRoutePayload (nextHop, new_payload, sharedKey);
					default:
						throw new FormatException ();
				}
			}

			public static byte[] CreateRoutedPayload (SymmetricKey[] relayKeys, object msg)
			{
				using (MemoryStream ms = new MemoryStream ()) {
					DefaultFormatter.Serialize (ms, msg);
					ms.Close ();
					return CreateRoutedPayload (relayKeys, ms.ToArray ());
				}
			}

			static byte[] CreateRoutedPayload (SymmetricKey[] relayKeys, byte[] msg)
			{
				int iv_size = DefaultSymmetricBlockBits / 8;
				int payload_size = msg.Length + 2;
				byte[] payload = new byte[PayloadFixedSize];

				RNG.Instance.GetBytes (payload);
				payload[0] = (byte)((msg.Length >> 8) & 0xff);
				payload[1] = (byte)(msg.Length & 0xff);
				Buffer.BlockCopy (msg, 0, payload, 2, msg.Length);
				if (payload_size % iv_size != 0)
					payload_size += iv_size - (payload_size % iv_size);

				for (int i = relayKeys.Length - 1; i >= 0; i--) {
					byte[] encrypted = relayKeys[i].Encrypt (payload, 0, payload_size);
					if (payload_size != encrypted.Length)
						throw new ApplicationException ();
					Buffer.BlockCopy (encrypted, 0, payload, 0, encrypted.Length);
				}

				return payload;
			}

			public static byte[] CreateRoutedPayload (SymmetricKey key, object msg)
			{
				using (MemoryStream ms = new MemoryStream ()) {
					DefaultFormatter.Serialize (ms, msg);
					ms.Close ();
					return CreateRoutedPayload (key, ms.ToArray ());
				}
			}

			static byte[] CreateRoutedPayload (SymmetricKey key, byte[] msg)
			{
				int iv_size = DefaultSymmetricBlockBits / 8;
				int payload_size = msg.Length + 2;
				byte[] payload = new byte[PayloadFixedSize];

				RNG.Instance.GetBytes (payload);
				payload[0] = (byte)((msg.Length >> 8) & 0xff);
				payload[1] = (byte)(msg.Length & 0xff);
				Buffer.BlockCopy (msg, 0, payload, 2, msg.Length);
				if (payload_size % iv_size != 0)
					payload_size += iv_size - (payload_size % iv_size);

				byte[] encrypted = key.Encrypt (payload, 0, payload_size);
				if (payload_size != encrypted.Length)
					throw new ApplicationException ();
				Buffer.BlockCopy (encrypted, 0, payload, 0, encrypted.Length);

				return payload;
			}

			public static byte[] DecryptRoutedPayload (SymmetricKey key, byte[] payload)
			{
				return key.Decrypt (payload, 0, payload.Length);
			}

			public static object DecryptRoutedPayloadAtEnd (SymmetricKey key, byte[] payload)
			{
				int offset, size;
				payload = DecryptRoutedPayloadAtEnd (key, payload, out offset, out size);
				using (MemoryStream ms = new MemoryStream (payload, offset, size)) {
					return DefaultFormatter.Deserialize (ms);
				}
			}

			static byte[] DecryptRoutedPayloadAtEnd (SymmetricKey key, byte[] payload, out int offset, out int size)
			{
				byte[] ret = key.Decrypt (payload, 0, payload.Length);
				offset = 2;
				size = (ret[0] << 8) | ret[1];
				return ret;
			}

			public static byte[] EncryptRoutedPayload (SymmetricKey key, byte[] payload)
			{
				return key.Encrypt (payload, 0, payload.Length);
			}

			public static object DecryptRoutedPayload (SymmetricKey[] relayKeys, byte[] payload)
			{
				int offset, size;
				payload = DecryptRoutedPayload (relayKeys, payload, out offset, out size);
				using (MemoryStream ms = new MemoryStream (payload, offset, size)) {
					return DefaultFormatter.Deserialize (ms);
				}
			}

			static byte[] DecryptRoutedPayload (SymmetricKey[] relayKeys, byte[] payload, out int offset, out int size)
			{
				for (int i = 0; i < relayKeys.Length; i ++) {
					byte[] decrypted = relayKeys[i].Decrypt (payload, 0, payload.Length);
					if (payload.Length != decrypted.Length)
						throw new ApplicationException ();
					payload = decrypted;
				}
				offset = 2;
				size = (payload[0] << 8) | payload[1];
				return payload;
			}
		}
		#endregion

		#region AnonymousEndPoint
		[Serializable]
		class AnonymousEndPoint// : EndPoint
		{
			EndPoint _ep;
			RouteLabel _label;

			public AnonymousEndPoint (EndPoint ep, RouteLabel label)
			{
				_ep = ep;
				_label = label;
			}

			public EndPoint EndPoint {
				get { return _ep; }
			}

			public RouteLabel Label {
				get { return _label; }
			}

			/*public override System.Net.Sockets.AddressFamily AddressFamily {
				get { return _ep.AddressFamily; }
			}*/

			public override bool Equals (object obj)
			{
				AnonymousEndPoint other = obj as AnonymousEndPoint;
				if (other == null)
					return false;
				return this._ep.Equals (other._ep) && (this._label == other._label);
			}

			public override int GetHashCode ()
			{
				return _ep.GetHashCode () ^ _label.GetHashCode ();
			}

			public override string ToString ()
			{
				return _ep.ToString () + "@" + _label.ToString ("x");
			}
		}
		#endregion

		#region Messages
		[Serializable]
		class EstablishRouteMessage
		{
			RouteLabel _label;
			byte[] _payload;

			public EstablishRouteMessage (RouteLabel label, byte[] payload)
			{
				_label = label;
				_payload = payload;
			}

			public RouteLabel Label {
				get { return _label; }
			}

			public byte[] Payload {
				get { return _payload; }
			}
		}

		class EstablishRoutePayload
		{
			SymmetricKey _key;
			EndPoint _nextHop;
			byte[] _nextHopPayload;

			public EstablishRoutePayload (EndPoint nextHop, byte[] nextHopPayload, SymmetricKey key)
			{
				_nextHop = nextHop;
				_nextHopPayload = nextHopPayload;
				_key = key;
			}

			public EndPoint NextHopEndPoint {
				get { return _nextHop; }
			}

			public byte[] NextHopPayload {
				get { return _nextHopPayload; }
			}

			public SymmetricKey SharedKey {
				get { return _key; }
			}
		}

		[Serializable]
		class Ping
		{
			static Ping _instance = new Ping ();
			private Ping () {}
			public static Ping Instance {
				get { return _instance; }
			}
		}

		[Serializable]
		class RoutedMessage
		{
			RouteLabel _label;
			byte[] _payload;

			public RoutedMessage (RouteLabel label, byte[] payload)
			{
				_label = label;
				_payload = payload;
			}

			public RouteLabel Label {
				get { return _label; }
			}

			public byte[] Payload {
				get { return _payload; }
			}
		}

		[Serializable]
		class AcknowledgeMessage
		{
			byte[] _payload;

			public AcknowledgeMessage (byte[] payload)
			{
				_payload = payload;
			}

			public byte[] Payload {
				get { return _payload; }
			}
		}
		#endregion
	}
}
