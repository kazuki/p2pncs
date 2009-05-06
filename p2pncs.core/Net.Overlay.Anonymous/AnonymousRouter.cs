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

/*
 * ToDo:
 * - データグラムサイズを常に900バイト未満に押さえる
 * - 複数経路を利用したコネクションの確立・メッセージのやりとりに対応させる
 * - ConnectionMessageにおいて終端ノードの情報が平文でやりとりされているが、コネクション鍵で暗号化を施すようにする
 * - コネクション間メッセージにはHMACを付けるほか、再送攻撃にも耐えられるようにする
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Threading;
using openCrypto;
using openCrypto.EllipticCurve;
using p2pncs.Net.Overlay.DHT;
using p2pncs.Security.Cryptography;
using p2pncs.Threading;
using p2pncs.Utility;
using ECDiffieHellman = openCrypto.EllipticCurve.KeyAgreement.ECDiffieHellman;
using RouteLabel = System.Int32;

namespace p2pncs.Net.Overlay.Anonymous
{
	public class AnonymousRouter : IAnonymousRouter
	{
		#region Static Parameters
		const SymmetricAlgorithmType DefaultSymmetricAlgorithmType = SymmetricAlgorithmType.Camellia;
		static readonly SymmetricAlgorithmPlus DefaultSymmetricAlgorithm = new CamelliaManaged ();
		const int DefaultSymmetricKeyBits = 128;
		const int DefaultSymmetricBlockBits = 128;

		//const int PayloadFixedSize = (DefaultSymmetricKeyBits / 8) * 56; // 896 bytes
		const int PayloadFixedSize = 5120; // TODO: シリアライザを見直して896バイト未満に収まるように調整する

		const int DefaultSubscribeRoutes = 2;
		const int DefaultRealyNodes = 3;

		static TimeSpan MaxRRT = TimeSpan.FromMilliseconds (1000); // included cost of cryptography
		static TimeSpan MultipleCipherRouteMaxRoundtripTime = new TimeSpan (MaxRRT.Ticks * DefaultRealyNodes);
		static int MultipleCipherRouteMaxRetry = 1;
		static TimeSpan MultipleCipherRelayTimeout = new TimeSpan (MaxRRT.Ticks * (DefaultRealyNodes - 1));
		static int MultipleCipherRelayMaxRetry = 1;
		static TimeSpan MultipleCipherReverseRelayTimeout = MaxRRT;
		static int MultipleCipherReverseRelayMaxRetry = 1;

		static TimeSpan DHTPutInterval = TimeSpan.FromSeconds (60);
		static TimeSpan DHTPutValueLifeTime = DHTPutInterval + TimeSpan.FromSeconds (5);

		static TimeSpan RelayRouteTimeout = TimeSpan.FromSeconds (30);
		static TimeSpan RelayRouteTimeoutWithMargin = RelayRouteTimeout + (MultipleCipherRouteMaxRoundtripTime + MultipleCipherRouteMaxRoundtripTime);
		static IFormatter DefaultFormatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter ();

		static EndPoint DummyEndPoint = new IPEndPoint (IPAddress.Loopback, 0);

		const int DHT_TYPEID = 1;

		static object DummyAckMsg = "ACK";

		const int ConnectionEstablishSharedInfoBytes = 16;
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

		List<ConnectionInfo> _connections = new List<ConnectionInfo> ();
		DuplicationChecker<long> _dupCheck = new DuplicationChecker<long> (8192);
		#endregion

		static AnonymousRouter ()
		{
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

			dht.RegisterTypeID (typeof (DHTEntry), DHT_TYPEID);
			(dht as SimpleDHT).NumberOfReplicas = 1;
			_sock.AddInquiredHandler (typeof (EstablishRouteMessage), MessagingSocket_Inquired_EstablishRouteMessage);
			_sock.AddInquiredHandler (typeof (RoutedMessage), MessagingSocket_Inquired_RoutedMessage);
			_sock.AddInquiredHandler (typeof (ConnectionEstablishMessage), MessagingSocket_Inquired_ConnectionEstablishMessage);
			_sock.AddInquiredHandler (typeof (ConnectionMessage), MessagingSocket_Inquired_ConnectionMessage);
			_sock.InquirySuccess += new InquiredEventHandler (MessagingSocket_Success);
			interrupter.AddInterruption (RouteTimeoutCheck);
		}

		#region MessagingSocket
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
				BoundaryInfo boundaryInfo = new BoundaryInfo (payload.SharedKey, new AnonymousEndPoint (e.EndPoint, msg.Label), key);
				EstablishedMessage ack = new EstablishedMessage (boundaryInfo.LabelOnlyAnonymousEndPoint.Label);
				sock.StartResponse (e, new AcknowledgeMessage (MultipleCipherHelper.CreateRoutedPayload (payload.SharedKey, ack)));
				if (_dupCheck.Check (payload.DuplicationCheckId)) {
					Console.WriteLine ("{0}: Received {1} -> (end)\r\n: Subcribe {2}", _kbr.SelftNodeId, msg.Label, key);
					RouteInfo info = new RouteInfo (boundaryInfo.Previous, boundaryInfo, payload.SharedKey);
					using (IDisposable cookie = _routingMapLock.EnterWriteLock ()) {
						_routingMap.Add (info.Previous, info);
						_routingMap.Add (boundaryInfo.LabelOnlyAnonymousEndPoint, info);
					}
					using (IDisposable cookie = _boundMapLock.EnterWriteLock ()) {
						List<BoundaryInfo> list;
						if (!_boundMap.TryGetValue (key, out list)) {
							list = new List<BoundaryInfo> (2);
							_boundMap.Add (key, list);
						}
						list.Add (boundaryInfo);
					}
					boundaryInfo.PutToDHT (_dht);
				}
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
			if (ack == null) {
				// Inquiry fail.
				sock.StartResponse (e, null);
				return;
			}
			sock.StartResponse (e, new AcknowledgeMessage (MultipleCipherHelper.EncryptRoutedPayload (payload.SharedKey, ack.Payload)));

			AnonymousEndPoint prevEP = new AnonymousEndPoint (e.EndPoint, msg.Label);
			AnonymousEndPoint nextEP = new AnonymousEndPoint (payload.NextHopEndPoint, msg2.Label);
			RouteInfo info = new RouteInfo (prevEP, nextEP, payload.SharedKey);
			using (IDisposable cookie = _routingMapLock.EnterWriteLock ()) {
				if (!_routingMap.ContainsKey (prevEP)) {
					_routingMap.Add (info.Previous, info);
					_routingMap.Add (info.Next, info);
				}
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
						Console.WriteLine ("{0}: -> Recv RoutedMessage", _kbr.SelftNodeId);
					} else {
						long dupCheckId;
						object routedMsg = MultipleCipherHelper.DecryptRoutedPayloadAtEnd (routeInfo.Key, msg.Payload, out dupCheckId);
						if (_dupCheck.Check (dupCheckId)) {
							Console.WriteLine ("{0}: -> Recv RoutedMessage (end) : {1}", _kbr.SelftNodeId, routedMsg);
							object ack = ProcessMessage (routedMsg, routeInfo.BoundaryInfo);
							payload = MultipleCipherHelper.CreateRoutedPayload (routeInfo.Key, ack);
							_sock.StartResponse (e, new AcknowledgeMessage (payload));
						} else {
							// TODO: どうする?
						}
					}
				} else {
					// direction: next -> ! -> prev
					routeInfo.ReceiveMessageFromNextNode ();
					if (routeInfo.StartPointInfo == null) {
						payload = MultipleCipherHelper.EncryptRoutedPayload (routeInfo.Key, msg.Payload);
						_sock.BeginInquire (new RoutedMessage (routeInfo.Previous.Label, payload), routeInfo.Previous.EndPoint,
							MultipleCipherReverseRelayTimeout, MultipleCipherReverseRelayMaxRetry, null, null);
						Console.WriteLine ("{0}: <- Recv RoutedMessage", _kbr.SelftNodeId);
					} else {
						long dupCheckId;
						object routedMsg = MultipleCipherHelper.DecryptRoutedPayload (routeInfo.StartPointInfo.RelayNodeKeys, msg.Payload, out dupCheckId);
						if (_dupCheck.Check (dupCheckId)) {
							Console.WriteLine ("{0}: <- Recv RoutedMessage (start) : {1}", _kbr.SelftNodeId, routedMsg);
							ProcessMessage (routedMsg, routeInfo.StartPointInfo);
						}
					}
					_sock.StartResponse (e, DummyAckMsg);
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
			if (ack == null) {
				// inquiry fail
				_sock.StartResponse (e, null);
				return;
			}
			
			ack = new AcknowledgeMessage (MultipleCipherHelper.EncryptRoutedPayload (routeInfo.Key, ack.Payload));
			_sock.StartResponse (e, ack);
		}

		void MessagingSocket_Inquired_ConnectionEstablishMessage (object sender, InquiredEventArgs e)
		{
			ConnectionEstablishMessage msg = (ConnectionEstablishMessage)e.InquireMessage;
			_sock.StartResponse (e, DummyAckMsg);
			if (!_dupCheck.Check (msg.DuplicationCheckId))
				return;
			RouteInfo routeInfo;
			using (IDisposable cookie = _routingMapLock.EnterReadLock ()) {
				if (!_routingMap.TryGetValue (new AnonymousEndPoint (DummyEndPoint, msg.Label), out routeInfo))
					routeInfo = null;
			}
			if (routeInfo == null)
				return;

			routeInfo.BoundaryInfo.SendMessage (_sock, msg);
		}

		void MessagingSocket_Inquired_ConnectionMessage (object sender, InquiredEventArgs e)
		{
			ConnectionMessage msg = (ConnectionMessage)e.InquireMessage;
			_sock.StartResponse (e, DummyAckMsg);
			if (!_dupCheck.Check (msg.DuplicationCheckId))
				return;

			RouteInfo routeInfo;
			using (IDisposable cookie = _routingMapLock.EnterReadLock ()) {
				if (!_routingMap.TryGetValue (new AnonymousEndPoint (DummyEndPoint, msg.Label), out routeInfo))
					routeInfo = null;
			}
			if (routeInfo == null)
				return;

			routeInfo.BoundaryInfo.SendMessage (_sock, msg);
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

		#region Message Handlers
		void ProcessMessage (object msg_obj, StartPointInfo info)
		{
			{
				ConnectionEstablishMessage msg = msg_obj as ConnectionEstablishMessage;
				if (msg != null) {
					if (!_dupCheck.Check (msg.DuplicationCheckId))
						return;
					SubscribeInfo subscribeInfo;
					using (IDisposable cookie = _subscribeMapLock.EnterReadLock ()) {
						if (!_subscribeMap.TryGetValue (msg.RecipientID, out subscribeInfo))
							return;
					}
					ConnectionEstablishInfo establishInfo = msg.Decrypt (subscribeInfo.DiffieHellman);
					AcceptingEventArgs args = new AcceptingEventArgs (subscribeInfo.RecipientID, establishInfo.Initiator);
					if (Accepting != null) {
						try {
							Accepting (this, args);
						} catch {}
					}
					if (args.ReceiveEventHandler == null || Accepted == null)
						return; // Reject

					byte[] sharedInfo2 = RNG.GetRNGBytes (ConnectionEstablishSharedInfoBytes);
					byte[] sharedInfo = new byte[establishInfo.SharedInfo.Length + ConnectionEstablishSharedInfoBytes];
					Buffer.BlockCopy (establishInfo.SharedInfo, 0, sharedInfo, 0, establishInfo.SharedInfo.Length);
					Buffer.BlockCopy (sharedInfo2, 0, sharedInfo, establishInfo.SharedInfo.Length, sharedInfo2.Length);
					ECDiffieHellman ecdh = new ECDiffieHellman (subscribeInfo.DiffieHellman.Parameters);
					ecdh.SharedInfo = sharedInfo;
					byte[] iv = new byte[DefaultSymmetricBlockBits / 8];
					byte[] key = new byte[DefaultSymmetricKeyBits / 8];
					byte[] shared = ecdh.PerformKeyAgreement (establishInfo.Initiator.GetByteArray (), iv.Length + key.Length);
					Buffer.BlockCopy (shared, 0, iv, 0, iv.Length);
					Buffer.BlockCopy (shared, iv.Length, key, 0, key.Length);
					SymmetricKey key2 = new SymmetricKey (DefaultSymmetricAlgorithmType, iv, key);
					ConnectionInfo connection = new ConnectionInfo (this, subscribeInfo, establishInfo.Initiator, establishInfo.ConnectionId, key2, args.ReceiveEventHandler);
					connection.DestinationSideTerminalNodes = establishInfo.EndPoints;
					lock (_connections) {
						_connections.Add (connection);
					}
					subscribeInfo.SendToAllRoutes (new ConnectionMessageBeforeBoundary (subscribeInfo.GetRouteEndPoints(),
						establishInfo.EndPoints,establishInfo.ConnectionId, sharedInfo2), null, null);

					try {
						Accepted (this, new AcceptedEventArgs (connection.Socket, args.State));
					} catch {}
					return;
				}
			}
			{
				ConnectionMessage msg = msg_obj as ConnectionMessage;
				if (msg != null) {
					if (!_dupCheck.Check (msg.DuplicationCheckId))
						return;
					ConnectionInfo connection = null;
					lock (_connections) {
						for (int i = 0; i < _connections.Count; i ++)
							if (_connections[i].ConnectionID == msg.ConnectionId) {
								connection = _connections[i];
								break;
							}
					}
					if (connection == null)
						return;
					if (connection.IsInitiator && !connection.IsConnected) {
						connection.ReceiveFirstConnectionMessage (msg);
					} else {
						connection.Receive (msg);
					}
					return;
				}
			}
		}

		object ProcessMessage (object msg_obj, BoundaryInfo info)
		{
			{
				LookupRecipientProxyMessage msg = msg_obj as LookupRecipientProxyMessage;
				if (msg != null) {
					_dht.BeginGet (msg.RecipientKey, DHT_TYPEID, Process_LookupRecipientProxyMessage_Callback, msg);
					return DummyAckMsg;
				}
			}
			{
				ConnectionMessageBeforeBoundary msg = msg_obj as ConnectionMessageBeforeBoundary;
				if (msg != null) {
					if (_dupCheck.Check (msg.DuplicationCheckId)) {
						for (int i = 0; i < msg.OtherSideTerminalEndPoints.Length; i ++) {
							ConnectionMessage msg2 = new ConnectionMessage (msg.MySideTerminalEndPoints,
								msg.OtherSideTerminalEndPoints[i].Label, msg.ConnectionId, msg.DuplicationCheckId, msg.Payload);
							_sock.BeginInquire (msg2, msg.OtherSideTerminalEndPoints[i].EndPoint, null, null);
						}
					}
					return DummyAckMsg;
				}
			}
			return DummyAckMsg;
		}

		void Process_LookupRecipientProxyMessage_Callback (IAsyncResult ar)
		{
			LookupRecipientProxyMessage msg = (LookupRecipientProxyMessage)ar.AsyncState;
			GetResult result = _dht.EndGet (ar);
			if (result == null || result.Values == null || result.Values.Length == 0) {
				Console.WriteLine ("{0}: DHT Lookup failed {1}", _kbr.SelftNodeId, msg.RecipientKey);
				return;
			}
			string temp = "";
			for (int i = 0; i < result.Values.Length; i ++) {
				DHTEntry entry = result.Values[i] as DHTEntry;
				if (entry == null) continue;
				_sock.BeginInquire (msg.Message.Copy (entry.Label), entry.EndPoint, delegate (IAsyncResult ar2) {
					object res = _sock.EndInquire (ar2);
					if (res == null)
						Console.WriteLine ("{0}: FAIL", _kbr.SelftNodeId);
					else
						Console.WriteLine ("{0}: OK", _kbr.SelftNodeId);
				}, null);
				temp += entry.ToString () + ", ";
			}
			if (temp.Length > 0) {
				temp = temp.Substring (0, temp.Length - 2);
				temp = _kbr.SelftNodeId.ToString() + ": Send ConnectionEstablishMessage to " + temp;
				Console.WriteLine (temp);
			}
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
						if (info.StartPointInfo != null || info.BoundaryInfo != null) {
							if (info.BoundaryInfo != null)
								timeoutRoutes.Add (info.BoundaryInfo.LabelOnlyAnonymousEndPoint);
							timeoutRouteEnds.Add (info);
						}
						Console.WriteLine ("Timeout: {0} <- {1} -> {2}",
							info.Previous == null ? "(null)" : info.Previous.Label.ToString(),
							_kbr.SelftNodeId,
							info.Next == null ? "(null)" : info.Next.Label.ToString ());
					} else {
						if (info.BoundaryInfo != null) {
							if (info.BoundaryInfo.NextPutToDHTTime <= DateTime.Now)
								info.BoundaryInfo.PutToDHT (_dht);
						}
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

		public IAsyncResult BeginEstablishRoute (Key recipientId, Key destinationId, DatagramReceiveEventHandler receivedHandler, AsyncCallback callback, object state)
		{
			SubscribeInfo subscribeInfo;
			using (IDisposable cookie = _subscribeMapLock.EnterReadLock ()) {
				if (!_subscribeMap.TryGetValue (recipientId, out subscribeInfo))
					throw new KeyNotFoundException ();
			}

			ConnectionInfo info = new ConnectionInfo (this, subscribeInfo, destinationId, receivedHandler, callback, state);
			lock (_connections) {
				_connections.Add (info);
			}
			return info.AsyncResult;
		}

		public IAnonymousSocket EndEstablishRoute (IAsyncResult ar)
		{
			IConnectionAsyncResult ar2 = ar as IConnectionAsyncResult;
			if (ar2 == null)
				throw new ArgumentException ();
			ar.AsyncWaitHandle.WaitOne ();
			return ar2.ConnectionInfo.Socket;
		}

		public void Close ()
		{
			_sock.RemoveInquiredHandler (typeof (EstablishRouteMessage), MessagingSocket_Inquired_EstablishRouteMessage);
			_sock.RemoveInquiredHandler (typeof (RoutedMessage), MessagingSocket_Inquired_RoutedMessage);
			_sock.RemoveInquiredHandler (typeof (ConnectionEstablishMessage), MessagingSocket_Inquired_ConnectionEstablishMessage);
			_sock.RemoveInquiredHandler (typeof (ConnectionMessage), MessagingSocket_Inquired_ConnectionMessage);
			_sock.InquirySuccess -= MessagingSocket_Success;
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
			ECDiffieHellman _ecdh;
			AnonymousRouter _router;
			object _listLock = new object ();
			List<StartPointInfo> _establishedList = new List<StartPointInfo> ();
			List<StartPointInfo> _establishingList = new List<StartPointInfo> ();

			public SubscribeInfo (AnonymousRouter router, Key id, ECKeyPair privateKey, IKeyBasedRouter kbr)
			{
				_router = router;
				_recipientId = id;
				_ecdh = new ECDiffieHellman (privateKey);
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
							_establishedList[i].SendMessage (_kbr.MessagingSocket, Ping.Instance, null, null);
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
							byte[] payload = info.CreateEstablishData (_recipientId, _ecdh.Parameters.DomainName);
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

				if (!_active) return;
				long dupCheckId = 0;
				EstablishedMessage msg = (ack == null ? null : MultipleCipherHelper.DecryptRoutedPayload (startInfo.RelayNodeKeys, ack.Payload, out dupCheckId) as EstablishedMessage);
				if (msg != null && !_router._dupCheck.Check (dupCheckId))
					return;

				if (ack == null || msg == null) {
					startInfo.Close ();
					Console.WriteLine ("ESTABLISH FAILED");
					CheckNumberOfEstablishedRoutes ();
				} else {
					Console.WriteLine ("ESTABLISHED !!");
					startInfo.Established (msg);
					RouteInfo routeInfo = new RouteInfo (startInfo, new AnonymousEndPoint (startInfo.RelayNodes[0].EndPoint, startInfo.Label), null);
					lock (_listLock) {
						_establishedList.Add (startInfo);
					}
					using (IDisposable cookie = _router._routingMapLock.EnterWriteLock ()) {
						_router._routingMap.Add (routeInfo.Next, routeInfo);
					}
				}
			}

			public AnonymousEndPoint[] GetRouteEndPoints ()
			{
				lock (_establishedList) {
					AnonymousEndPoint[] endPoints = new AnonymousEndPoint[_establishedList.Count];
					for (int i = 0; i < _establishedList.Count; i ++)
						endPoints[i] = _establishedList[i].TerminalEndPoint;
					return endPoints;
				}
			}

			public void SendToAllRoutes (object msg, AckMessageHandler handler, object state)
			{
				lock (_establishedList) {
					for (int i = 0; i < _establishedList.Count; i ++)
						_establishedList[i].SendMessage (_router._sock, msg, handler, state);
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

			public ECDiffieHellman DiffieHellman {
				get { return _ecdh; }
			}

			public AnonymousRouter AnonymousRouter {
				get { return _router; }
			}
		}
		#endregion

		#region StartPointInfo
		delegate void AckMessageHandler (object msg);
		class StartPointInfo
		{
			NodeHandle[] _relayNodes;
			SymmetricKey[] _relayKeys = null;
			RouteLabel _label;
			AnonymousEndPoint _termEP = null;
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

			public AnonymousEndPoint TerminalEndPoint {
				get { return _termEP; }
			}

			public DateTime LastSendTime {
				get { return _lastSendTime; }
			}

			public void SendMessage (IMessagingSocket sock, object msg, AckMessageHandler ackHandler, object state)
			{
				byte[] payload = MultipleCipherHelper.CreateRoutedPayload (_relayKeys, msg);
				_lastSendTime = DateTime.Now;
				sock.BeginInquire (new RoutedMessage (_label, payload), _relayNodes[0].EndPoint,
					MultipleCipherRouteMaxRoundtripTime, MultipleCipherRouteMaxRetry,
					SendMessage_Callback, new object[] {sock, ackHandler, state});
			}

			void SendMessage_Callback (IAsyncResult ar)
			{
				object[] states = (object[])ar.AsyncState;
				IMessagingSocket sock = (IMessagingSocket)states[0];
				AckMessageHandler handler = (AckMessageHandler)states[1];
				object state = states[2];
				AcknowledgeMessage ack = sock.EndInquire (ar) as AcknowledgeMessage;
				if (ack == null) {
					Timeout ();
					return;
				}

				long dupCheckId;
				object msg = MultipleCipherHelper.DecryptRoutedPayload (_relayKeys, ack.Payload, out dupCheckId);
				if (!_subscribe.AnonymousRouter._dupCheck.Check (dupCheckId))
					return;

				if (handler != null) {
					try {
						handler (msg);
					} catch {}
				}
				Console.WriteLine ("{0}: ACK: {1}", _subscribe.RecipientID, msg);
			}

			public byte[] CreateEstablishData (Key recipientId, ECDomainNames domain)
			{
				if (_relayKeys != null)
					throw new Exception ();
				_relayKeys = new SymmetricKey[_relayNodes.Length];
				return MultipleCipherHelper.CreateEstablishPayload (_relayNodes, _relayKeys, recipientId, domain);
			}

			public void Established (EstablishedMessage msg)
			{
				_termEP = new AnonymousEndPoint (_relayNodes[_relayNodes.Length - 1].EndPoint, msg.Label);
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
			DateTime _nextPutToDHTTime = DateTime.Now + DHTPutInterval;
			AnonymousEndPoint _dummyEP;
			Key _recipientKey;

			public BoundaryInfo (SymmetricKey key, AnonymousEndPoint prev, Key recipientKey)
			{
				_key = key;
				_prevEP = prev;
				_recipientKey = recipientKey;
				_dummyEP = new AnonymousEndPoint (DummyEndPoint, GenerateRouteLabel ());
			}

			public void SendMessage (IMessagingSocket sock, object msg)
			{
				if (_closed) return;
				byte[] payload = MultipleCipherHelper.CreateRoutedPayload (_key, msg);
				sock.BeginInquire (new RoutedMessage (_prevEP.Label, payload), _prevEP.EndPoint,
					MultipleCipherReverseRelayTimeout, MultipleCipherReverseRelayMaxRetry,
					SendMessage_Callback, sock);
			}
			void SendMessage_Callback (IAsyncResult ar)
			{
				IMessagingSocket sock = (IMessagingSocket)ar.AsyncState;
				if (sock.EndInquire (ar) == null)
					Timeout ();
			}

			public void PutToDHT (IDistributedHashTable dht)
			{
				_nextPutToDHTTime = DateTime.Now + DHTPutInterval;
				dht.BeginPut (_recipientKey, DHTPutValueLifeTime, new DHTEntry (_dummyEP.Label), null, null);
			}

			public SymmetricKey SharedKey {
				get { return _key; }
			}

			public AnonymousEndPoint Previous {
				get { return _prevEP; }
			}

			public DateTime NextPutToDHTTime {
				get { return _nextPutToDHTTime; }
			}

			public AnonymousEndPoint LabelOnlyAnonymousEndPoint {
				get { return _dummyEP; }
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

		#region ConnectionInfo
		class ConnectionInfo
		{
			bool _initiator;
			bool _connected = false;
			EstablishRouteAsyncResult _ar;
			AnonymousRouter _router;
			SubscribeInfo _subscribeInfo;
			ConnectionEstablishInfo _establishInfo;
			ConnectionEstablishMessage _establishMsg;
			LookupRecipientProxyMessage _lookupMsg;
			Key _destKey;
			ECKeyPair _destPubKey;
			int _connectionId;
			SymmetricKey _key = null;
			AnonymousEndPoint[] _destSideTermEPs;
			ConnectionSocket _sock;

			public ConnectionInfo (AnonymousRouter router, SubscribeInfo subscribeInfo, Key destKey, DatagramReceiveEventHandler receivedHandler, AsyncCallback callback, object state)
			{
				_initiator = true;
				_router = router;
				_subscribeInfo = subscribeInfo;
				_destKey = destKey;
				_destPubKey = destKey.ToECPublicKey (_subscribeInfo.DiffieHellman.Parameters.DomainName);
				_connectionId = BitConverter.ToInt32 (RNG.GetRNGBytes (4), 0);
				_ar = new EstablishRouteAsyncResult (this, callback, state);
				_sock = new ConnectionSocket (this, receivedHandler);

				// Create connection establish message
				_establishInfo = new ConnectionEstablishInfo (subscribeInfo.RecipientID, subscribeInfo.GetRouteEndPoints (),
					RNG.GetRNGBytes (ConnectionEstablishSharedInfoBytes), _connectionId);
				_establishMsg = new ConnectionEstablishMessage (_establishInfo, _destPubKey, destKey);
				_lookupMsg = new LookupRecipientProxyMessage (destKey, _establishMsg);

				_subscribeInfo.SendToAllRoutes (_lookupMsg, AckLookup, null);
			}

			public ConnectionInfo (AnonymousRouter router, SubscribeInfo subscribeInfo, Key destKey, int connectionId, SymmetricKey key, DatagramReceiveEventHandler receivedHandler)
			{
				_initiator = false;
				_connected = true;
				_router = router;
				_subscribeInfo = subscribeInfo;
				_destKey = destKey;
				_connectionId = connectionId;
				_key = key;
				_sock = new ConnectionSocket (this, receivedHandler);
			}

			void AckLookup (object ack)
			{
				Console.WriteLine (ack);
			}

			public bool IsInitiator {
				get { return _initiator; }
			}

			public bool IsConnected {
				get { return _connected; }
			}

			public int ConnectionID {
				get { return _connectionId; }
			}

			public AnonymousEndPoint[] DestinationSideTerminalNodes {
				get { return _destSideTermEPs; }
				set { _destSideTermEPs = value;}
			}

			public Key RecipientID {
				get { return _subscribeInfo.RecipientID; }
			}

			public Key DestinationID {
				get { return _destKey; }
			}

			public IAnonymousSocket Socket {
				get {
					if (_connected)
						return _sock;
					return null;
				}
			}

			public void Send (object obj)
			{
				byte[] payload;
				using (MemoryStream ms = new MemoryStream ()) {
					DefaultFormatter.Serialize (ms, obj);
					ms.Close ();
					payload = ms.ToArray ();
				}
				Send (payload, 0, payload.Length);
			}

			public void Send (byte[] buffer, int offset, int size)
			{
				byte[] payload = _key.Encrypt (buffer, offset, size);
				ConnectionMessageBeforeBoundary msg = new ConnectionMessageBeforeBoundary (_subscribeInfo.GetRouteEndPoints (),
					_destSideTermEPs, _connectionId, payload);
				_subscribeInfo.SendToAllRoutes (msg, null, null);
			}

			public void Receive (ConnectionMessage msg)
			{
				_destSideTermEPs = msg.EndPoints;
				byte[] payload = _key.Decrypt (msg.Payload, 0, msg.Payload.Length);
				/*object obj;
				using (MemoryStream ms = new MemoryStream (payload)) {
					obj = DefaultFormatter.Deserialize (ms);
				}
				Console.WriteLine ("{0}@{1}: Conn.Recv {2}", RecipientID, ConnectionID, obj);*/
				_sock.InvokeReceivedEvent (new DatagramReceiveEventArgs (payload, payload.Length, null));
			}

			public void ReceiveFirstConnectionMessage (ConnectionMessage msg)
			{
				_connected = true;
				byte[] sharedInfo = new byte[msg.Payload.Length + _establishInfo.SharedInfo.Length];
				Buffer.BlockCopy (_establishInfo.SharedInfo, 0, sharedInfo, 0, _establishInfo.SharedInfo.Length);
				Buffer.BlockCopy (msg.Payload, 0, sharedInfo, _establishInfo.SharedInfo.Length, msg.Payload.Length);
				ECDiffieHellman ecdh = new ECDiffieHellman (_subscribeInfo.DiffieHellman.Parameters);
				ecdh.SharedInfo = sharedInfo;
				byte[] iv = new byte[DefaultSymmetricBlockBits / 8];
				byte[] key = new byte[DefaultSymmetricKeyBits / 8];
				byte[] shared = ecdh.PerformKeyAgreement (_destPubKey, iv.Length + key.Length);
				Buffer.BlockCopy (shared, 0, iv, 0, iv.Length);
				Buffer.BlockCopy (shared, iv.Length, key, 0, key.Length);
				_key = new SymmetricKey (DefaultSymmetricAlgorithmType, iv, key);
				_destSideTermEPs = msg.EndPoints;
				_ar.Done ();
			}

			public void Close ()
			{
			}

			public IAsyncResult AsyncResult {
				get { return _ar; }
			}

			class EstablishRouteAsyncResult : IAsyncResult, IConnectionAsyncResult
			{
				ConnectionInfo _owner;
				object _state;
				AsyncCallback _callback;
				bool _completed = false;
				ManualResetEvent _done = new ManualResetEvent (false);

				public EstablishRouteAsyncResult (ConnectionInfo owner, AsyncCallback callback, object state)
				{
					_owner = owner;
					_state = state;
					_callback = callback;
				}

				public object AsyncState {
					get { return _state; }
				}

				public WaitHandle AsyncWaitHandle {
					get { return _done; }
				}

				public bool CompletedSynchronously {
					get { return false; }
				}

				public bool IsCompleted {
					get { return _completed; }
				}

				public void Done ()
				{
					_completed = true;
					_done.Set ();
					if (_callback != null) {
						try {
							_callback (this);
						} catch {}
					}
				}

				public ConnectionInfo ConnectionInfo {
					get { return _owner; }
				}
			}

			class ConnectionSocket : IAnonymousSocket
			{
				ConnectionInfo _info;
				DatagramReceiveEventHandler _handler;

				public ConnectionSocket (ConnectionInfo info, DatagramReceiveEventHandler received_handler)
				{
					_info = info;
					_handler = received_handler;
				}

				public void InvokeReceivedEvent (DatagramReceiveEventArgs args)
				{
					if (_handler != null) {
						try {
							_handler (this, args);
						} catch {}
					}
				}

				#region IAnonymousSocket Members

				public void Send (byte[] buffer)
				{
					Send (buffer, 0, buffer.Length);
				}

				public void Send (byte[] buffer, int offset, int size)
				{
					_info.Send (buffer, offset, size);
				}

				public void Close ()
				{
					Dispose ();
				}

				#endregion

				#region IDisposable Members

				public void Dispose ()
				{
					if (_info != null) {
						_info.Close ();
						_info = null;
					}
				}

				#endregion
			}
		}
		interface IConnectionAsyncResult
		{
			ConnectionInfo ConnectionInfo { get; }
		}
		#endregion

		#region MultipleCipherHelper
		static class MultipleCipherHelper
		{
			const int RoutedPayloadHeaderSize = 10;

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

				int payload_size = recipientId.KeyBytes + 8;
				byte[] payload = new byte[PayloadFixedSize];

				RNG.Instance.GetBytes (payload);
				payload[0] = 0;
				recipientId.CopyTo (payload, 9);

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
						Buffer.BlockCopy (decrypted, 9, rawKey, 0, rawKey.Length);
						return new EstablishRoutePayload (null, rawKey, sharedKey, BitConverter.ToInt64 (decrypted, 1));
					case 1:
						byte[] new_payload = new byte[PayloadFixedSize];
						RNG.Instance.GetBytes (new_payload);
						EndPoint nextHop = DeserializeEndPoint (decrypted, 1, out len);
						Buffer.BlockCopy (decrypted, 1 + len, new_payload, 0, decrypted.Length - 1 - len);
						return new EstablishRoutePayload (nextHop, new_payload, sharedKey, 0);
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
				int payload_size = msg.Length + RoutedPayloadHeaderSize;
				byte[] payload = new byte[PayloadFixedSize];

				RNG.Instance.GetBytes (payload);
				payload[0] = (byte)((msg.Length >> 8) & 0xff);
				payload[1] = (byte)(msg.Length & 0xff);
				Buffer.BlockCopy (msg, 0, payload, RoutedPayloadHeaderSize, msg.Length);
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
				int payload_size = msg.Length + RoutedPayloadHeaderSize;
				byte[] payload = new byte[PayloadFixedSize];

				RNG.Instance.GetBytes (payload);
				payload[0] = (byte)((msg.Length >> 8) & 0xff);
				payload[1] = (byte)(msg.Length & 0xff);
				Buffer.BlockCopy (msg, 0, payload, RoutedPayloadHeaderSize, msg.Length);
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

			public static object DecryptRoutedPayloadAtEnd (SymmetricKey key, byte[] payload, out long dupCheckId)
			{
				int offset, size;
				payload = DecryptRoutedPayloadAtEnd (key, payload, out dupCheckId, out offset, out size);
				using (MemoryStream ms = new MemoryStream (payload, offset, size)) {
					return DefaultFormatter.Deserialize (ms);
				}
			}

			static byte[] DecryptRoutedPayloadAtEnd (SymmetricKey key, byte[] payload, out long dupCheckId, out int offset, out int size)
			{
				byte[] ret = key.Decrypt (payload, 0, payload.Length);
				offset = RoutedPayloadHeaderSize;
				size = (ret[0] << 8) | ret[1];
				dupCheckId = BitConverter.ToInt64 (ret, 2);
				return ret;
			}

			public static byte[] EncryptRoutedPayload (SymmetricKey key, byte[] payload)
			{
				return key.Encrypt (payload, 0, payload.Length);
			}

			public static object DecryptRoutedPayload (SymmetricKey[] relayKeys, byte[] payload, out long dupCheckId)
			{
				int offset, size;
				payload = DecryptRoutedPayload (relayKeys, payload, out dupCheckId, out offset, out size);
				using (MemoryStream ms = new MemoryStream (payload, offset, size)) {
					return DefaultFormatter.Deserialize (ms);
				}
			}

			static byte[] DecryptRoutedPayload (SymmetricKey[] relayKeys, byte[] payload, out long dupCheckId, out int offset, out int size)
			{
				for (int i = 0; i < relayKeys.Length; i ++) {
					byte[] decrypted = relayKeys[i].Decrypt (payload, 0, payload.Length);
					if (payload.Length != decrypted.Length)
						throw new ApplicationException ();
					payload = decrypted;
				}
				offset = RoutedPayloadHeaderSize;
				size = (payload[0] << 8) | payload[1];
				dupCheckId = BitConverter.ToInt64 (payload, 2);
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

		#region DHT Entry
		[Serializable]
		class DHTEntry : IPutterEndPointStore, IEquatable<DHTEntry>
		{
			RouteLabel _label;
			EndPoint _ep = null;

			public DHTEntry (RouteLabel label)
			{
				_label = label;
			}

			public EndPoint EndPoint {
				get { return _ep; }
			}

			public RouteLabel Label {
				get { return _label; }
			}

			EndPoint IPutterEndPointStore.EndPoint {
				get { return _ep; }
				set { _ep = value;}
			}

			public override bool Equals (object obj)
			{
				DHTEntry entry = obj as DHTEntry;
				if (entry == null)
					return false;
				return Equals (entry);
			}

			public override int GetHashCode ()
			{
				int hash = _label.GetHashCode ();
				if (_ep != null)
					hash ^= _ep.GetHashCode ();
				return hash;
			}

			public override string ToString ()
			{
				return (_ep == null ? "localhost" : _ep.ToString ()) + "@" + _label.ToString ();
			}

			public bool Equals (DHTEntry other)
			{
				if (_label != other._label)
					return false;
				if (_ep == null)
					return other._ep == null;
				return _ep.Equals (other._ep);
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
			long _dupCheckId;

			public EstablishRoutePayload (EndPoint nextHop, byte[] nextHopPayload, SymmetricKey key, long dupCheckId)
			{
				_nextHop = nextHop;
				_nextHopPayload = nextHopPayload;
				_key = key;
				_dupCheckId = dupCheckId;
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

			public long DuplicationCheckId {
				get { return _dupCheckId; }
			}
		}

		[Serializable]
		class EstablishedMessage
		{
			RouteLabel _label;

			public EstablishedMessage (RouteLabel label)
			{
				_label = label;
			}

			public RouteLabel Label {
				get { return _label; }
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

		[Serializable]
		class LookupRecipientProxyMessage
		{
			Key _recipientKey;
			ConnectionEstablishMessage _msg;

			public LookupRecipientProxyMessage (Key recipientKey, ConnectionEstablishMessage msg)
			{
				_recipientKey = recipientKey;
				_msg = msg;
			}

			public Key RecipientKey {
				get { return _recipientKey; }
			}

			public ConnectionEstablishMessage Message {
				get { return _msg; }
			}
		}

		[Serializable]
		class ConnectionEstablishMessage
		{
			byte[] _tempPubKey;
			byte[] _encryptedInfo;
			Key _recipientId;
			RouteLabel _label = 0;
			long _dupCheckId;

			ConnectionEstablishMessage (byte[] tempPubKey, byte[] encryptedInfo, RouteLabel label, Key recipientID, long dupCheckId)
			{
				_tempPubKey = tempPubKey;
				_encryptedInfo = encryptedInfo;
				_label = label;
				_recipientId = recipientID;
				_dupCheckId = dupCheckId;
			}

			public ConnectionEstablishMessage (ConnectionEstablishInfo info, ECKeyPair pubKey, Key recipientID)
			{
				ECDiffieHellman dh = new ECDiffieHellman (pubKey.DomainName);
				byte[] key_bytes = new byte[DefaultSymmetricKeyBits / 8];
				byte[] iv_bytes = new byte[DefaultSymmetricBlockBits / 8];
				byte[] shared = dh.PerformKeyAgreement (pubKey, key_bytes.Length + iv_bytes.Length);
				Buffer.BlockCopy (shared, 0, iv_bytes, 0, iv_bytes.Length);
				Buffer.BlockCopy (shared, iv_bytes.Length, key_bytes, 0, key_bytes.Length);
				SymmetricKey key = new SymmetricKey (DefaultSymmetricAlgorithmType, iv_bytes, key_bytes);
				using (MemoryStream ms = new MemoryStream ()) {
					DefaultFormatter.Serialize (ms, info);
					ms.Close ();
					byte[] raw = ms.ToArray ();
					_encryptedInfo = key.Encrypt (raw, 0, raw.Length);
				}
				_tempPubKey = dh.Parameters.ExportPublicKey (true);
				_recipientId = recipientID;
				_dupCheckId = BitConverter.ToInt64 (RNG.GetRNGBytes (8), 0);
			}

			public ConnectionEstablishInfo Decrypt (ECDiffieHellman dh)
			{
				ECKeyPair pair = ECKeyPair.CreatePublic (dh.Parameters.DomainName, _tempPubKey);
				byte[] key_bytes = new byte[DefaultSymmetricKeyBits / 8];
				byte[] iv_bytes = new byte[DefaultSymmetricBlockBits / 8];
				byte[] shared = dh.PerformKeyAgreement (pair, key_bytes.Length + iv_bytes.Length);
				Buffer.BlockCopy (shared, 0, iv_bytes, 0, iv_bytes.Length);
				Buffer.BlockCopy (shared, iv_bytes.Length, key_bytes, 0, key_bytes.Length);
				SymmetricKey key = new SymmetricKey (DefaultSymmetricAlgorithmType, iv_bytes, key_bytes);
				byte[] raw = key.Decrypt (_encryptedInfo, 0, _encryptedInfo.Length);
				using (MemoryStream ms = new MemoryStream (raw)) {
					return (ConnectionEstablishInfo)DefaultFormatter.Deserialize (ms);
				}
			}

			public ConnectionEstablishMessage Copy (RouteLabel label)
			{
				return new ConnectionEstablishMessage (_tempPubKey, _encryptedInfo, label, _recipientId, _dupCheckId);
			}

			public RouteLabel Label {
				get { return _label; }
			}

			public Key RecipientID {
				get { return _recipientId; }
			}

			public long DuplicationCheckId {
				get { return _dupCheckId; }
			}
		}

		[Serializable]
		class ConnectionEstablishInfo
		{
			byte[] _sharedInfo;
			Key _initiator;
			int _connectionId;
			AnonymousEndPoint[] _endPoints;

			public ConnectionEstablishInfo (Key initiator, AnonymousEndPoint[] eps, byte[] sharedInfo, int connectionId)
			{
				_initiator = initiator;
				_endPoints = eps;
				_sharedInfo = sharedInfo;
				_connectionId = connectionId;
			}

			public byte[] SharedInfo {
				get { return _sharedInfo; }
			}

			public Key Initiator {
				get { return _initiator; }
			}

			public AnonymousEndPoint[] EndPoints {
				get { return _endPoints; }
			}

			public int ConnectionId {
				get { return _connectionId; }
			}
		}

		[Serializable]
		class ConnectionMessageBeforeBoundary
		{
			AnonymousEndPoint[] _mySideTermEPs;
			AnonymousEndPoint[] _otherSideTermEPs;
			byte[] _payload;
			int _connectionId;
			long _dupCheckId;

			public ConnectionMessageBeforeBoundary (AnonymousEndPoint[] mySideTermEPs, AnonymousEndPoint[] otherSideTermEPs, int connectionId, byte[] payload)
			{
				_mySideTermEPs = mySideTermEPs;
				_otherSideTermEPs = otherSideTermEPs;
				_connectionId = connectionId;
				_payload = payload;
				_dupCheckId = BitConverter.ToInt64 (RNG.GetRNGBytes (8), 0);
			}

			public AnonymousEndPoint[] MySideTerminalEndPoints {
				get { return _mySideTermEPs; }
			}

			public AnonymousEndPoint[] OtherSideTerminalEndPoints {
				get { return _otherSideTermEPs; }
			}

			public int ConnectionId {
				get { return _connectionId; }
			}

			public byte[] Payload {
				get { return _payload; }
			}

			public long DuplicationCheckId {
				get { return _dupCheckId; }
			}
		}

		[Serializable]
		class ConnectionMessage
		{
			RouteLabel _label;
			int _connectionId;
			AnonymousEndPoint[] _termEPs;
			byte[] _payload;
			long _dupCheckId;

			public ConnectionMessage (AnonymousEndPoint[] termEPs, RouteLabel label, int connectionId, long dupCheckId, byte[] payload)
			{
				_termEPs = termEPs;
				_label = label;
				_connectionId = connectionId;
				_payload = payload;
				_dupCheckId = dupCheckId;
			}

			public RouteLabel Label {
				get { return _label; }
			}

			public int ConnectionId {
				get { return _connectionId; }
			}

			public AnonymousEndPoint[] EndPoints {
				get { return _termEPs; }
			}

			public byte[] Payload {
				get { return _payload; }
			}

			public long DuplicationCheckId {
				get { return _dupCheckId; }
			}
		}
		#endregion
	}
}
