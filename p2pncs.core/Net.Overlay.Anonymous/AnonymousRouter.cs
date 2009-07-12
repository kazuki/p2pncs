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
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using openCrypto;
using openCrypto.EllipticCurve;
using p2pncs.Net.Overlay.DHT;
using p2pncs.Security.Cryptography;
using p2pncs.Threading;
using p2pncs.Utility;
using ConnectionHalfLabel = System.UInt16;
using ConnectionLabel = System.UInt32;
using DupCheckLabel = System.Int64;
using ECDiffieHellman = openCrypto.EllipticCurve.KeyAgreement.ECDiffieHellman;
using RouteLabel = System.Int32;

namespace p2pncs.Net.Overlay.Anonymous
{
	public class AnonymousRouter : IAnonymousRouter
	{
		#region Static Parameters
		const SymmetricAlgorithmType DefaultSymmetricAlgorithmType = SymmetricAlgorithmType.Camellia;
		const CipherModePlus DefaultCipherMode = CipherModePlus.CBC;
		const PaddingMode DefaultPaddingMode = PaddingMode.ISO10126;
		const int DefaultSymmetricKeyBits = 128;
		const int DefaultSymmetricKeyBytes = DefaultSymmetricKeyBits / 8;
		const int DefaultSymmetricBlockBits = 128;
		const int DefaultSymmetricBlockBytes = DefaultSymmetricBlockBits / 8;
		const int DefaultSharedInfoSize = 64;

		/// <summary>多重暗号化されたメッセージのメッセージ長 (常にここで指定したサイズになる)</summary>
		int PayloadFixedSize = 896;
		static int RoutedMessageOverhead;

		public static int DefaultRelayNodes = 3;
		public static int DefaultSubscribeRoutes = 2;
		const float DefaultSubscribeRouteFactor = 1.0F;

		static TimeSpan Messaging_Timeout = TimeSpan.FromMilliseconds (200);
		const int Messaging_MaxRetry = 2;
		static TimeSpan DisconnectMessaging_Timeout = Messaging_Timeout;
		const int DisconnectMessaging_MaxRetry = Messaging_MaxRetry;

		// Static parameters for DHT
		static TimeSpan DHT_GetTimeout = TimeSpan.FromSeconds (5);
		static TimeSpan DHT_PutInterval = TimeSpan.FromSeconds (60);
		static TimeSpan DHT_Lifetime = DHT_PutInterval + DHT_GetTimeout;

		// Static parameters for Multiple Cipher Route (MCR)
		static TimeSpan MCR_EstablishingTimeout = TimeSpan.FromSeconds (DefaultRelayNodes * 2 * Messaging_Timeout.TotalSeconds);
		static TimeSpan MCR_MaxMessageInterval = TimeSpan.FromSeconds (10);
		static TimeSpan MCR_AliveCheckScheduleInterval = MCR_MaxMessageInterval - TimeSpan.FromSeconds (0.5);
		static TimeSpan MCR_MaxMessageIntervalWithMargin = MCR_MaxMessageInterval + new TimeSpan (Messaging_Timeout.Ticks * Messaging_MaxRetry);

		// Static parameters for Anonymous Connection (AC)
		public static int AC_DefaultUseSubscribeRoutes = DefaultSubscribeRoutes;
		static TimeSpan AC_EstablishTimeout = new TimeSpan (MCR_EstablishingTimeout.Ticks * 2) + DHT_GetTimeout;
		static TimeSpan AC_MaxMessageInterval = MCR_MaxMessageInterval;
		static TimeSpan AC_AliveCheckScheduleInterval = MCR_AliveCheckScheduleInterval;
		static TimeSpan AC_MaxMessageIntervalWithMargin = TimeSpan.FromDays (1); //new TimeSpan (AC_MaxMessageInterval.Ticks * 3);

		static object AckMessage = "ACK";

		public static EndPoint DummyEndPoint = new IPEndPoint (IPAddress.Loopback, 0);

		static Serializer DefaultSerializer = Serializer.Instance;
		#endregion

		bool _active = true;
		IMessagingSocket _sock;
		IKeyBasedRouter _kbr;
		IDistributedHashTable _dht;
		ECKeyPair _privateNodeKey;
		IntervalInterrupter _interrupter;

		Dictionary<AnonymousEndPoint, RouteInfo> _routingMap = new Dictionary<AnonymousEndPoint, RouteInfo> ();
		ReaderWriterLockWrapper _routingMapLock = new ReaderWriterLockWrapper ();

		Dictionary<Key, SubscribeInfo> _subscribeMap = new Dictionary<Key, SubscribeInfo> ();
		ReaderWriterLockWrapper _subscribeMapLock = new ReaderWriterLockWrapper ();

		Dictionary<uint, ConnectionInfo> _connectionMap = new Dictionary<uint,ConnectionInfo> ();
		HashSet<ushort> _usedConnectionIDs = new HashSet<ushort> ();
		ReaderWriterLockWrapper _connectionMapLock = new ReaderWriterLockWrapper ();

		Dictionary<Type, EventHandler<BoundaryNodeReceivedEventArgs>> _boundaryHandlers = new Dictionary<Type,EventHandler<BoundaryNodeReceivedEventArgs>> ();
		ReaderWriterLockWrapper _boundaryHandlerLock = new ReaderWriterLockWrapper ();

		const int DefaultDuplicationCheckerBufferSize = 1024;
		DuplicationChecker<DupCheckLabel> _startPointDupChecker = new DuplicationChecker<long> (DefaultDuplicationCheckerBufferSize);
		DuplicationChecker<DupCheckLabel> _terminalDupChecker = new DuplicationChecker<long> (DefaultDuplicationCheckerBufferSize);
		DuplicationChecker<DupCheckLabel> _interterminalDupChecker = new DuplicationChecker<long> (DefaultDuplicationCheckerBufferSize);

		static AnonymousRouter ()
		{
			int payloadSize = 1000;
			RoutedMessageOverhead = DefaultSerializer.Serialize (new RoutedMessage (int.MaxValue, new byte[payloadSize])).Length - payloadSize;
		}

		public AnonymousRouter (IDistributedHashTable dht, ECKeyPair privateNodeKey, IntervalInterrupter interrupter)
		{
			_kbr = dht.KeyBasedRouter;
			_sock = _kbr.MessagingSocket;
			_dht = dht;
			_privateNodeKey = privateNodeKey;
			_interrupter = interrupter;

			PayloadFixedSize = dht.KeyBasedRouter.MessagingSocket.MaxMessageSize - RoutedMessageOverhead;
			if (PayloadFixedSize % DefaultSymmetricBlockBytes != 0)
				PayloadFixedSize -= PayloadFixedSize % DefaultSymmetricBlockBytes;

			dht.RegisterTypeID (typeof (DHTEntry), 1, DHTEntry.Merger);
			_sock.AddInquiryDuplicationCheckType (typeof (EstablishRouteMessage));
			_sock.AddInquiryDuplicationCheckType (typeof (RoutedMessage));
			_sock.AddInquiredHandler (typeof (EstablishRouteMessage), Messaging_Inquired_EstablishRouteMessage);
			_sock.AddInquiredHandler (typeof (RoutedMessage), Messaging_Inquired_RoutedMessage);
			_sock.AddInquiredHandler (typeof (DisconnectMessage), Messaging_Inquired_DisconnectMessage);
			_sock.AddInquiredHandler (typeof (InterterminalMessage), Messaging_Inquired_InterterminalMessage);
			_sock.AddReceivedHandler (typeof (RoutedMessage), Messaging_Received_RoutedMessage);
			_sock.AddReceivedHandler (typeof (InterterminalMessage), Messaging_Received_InterterminalMessage);
			_interrupter.AddInterruption (RouteTimeoutCheck);
			_sock.InquiredUnknownMessage += delegate (object sender, InquiredEventArgs e) {
				Logger.Log (LogLevel.Error, this, "Unknown Inquired Message {0}", e.InquireMessage);
			};
		}

		#region IAnonymousRouter Members

		public ISubscribeInfo SubscribeRecipient (Key recipientId, ECKeyPair privateKey)
		{
			SubscribeInfo info = new SubscribeInfo (this, recipientId, privateKey, DefaultSubscribeRoutes, DefaultRelayNodes, DefaultSubscribeRouteFactor);
			using (_subscribeMapLock.EnterWriteLock ()) {
				if (_subscribeMap.ContainsKey (recipientId))
					return _subscribeMap[recipientId];
				_subscribeMap.Add (recipientId, info);
			}
			info.Start ();
			return info;
		}

		public void UnsubscribeRecipient (Key recipientId)
		{
			SubscribeInfo info;
			using (_subscribeMapLock.EnterWriteLock ()) {
				if (!_subscribeMap.TryGetValue (recipientId, out info))
					return;
				_subscribeMap.Remove (recipientId);
			}
			info.Close ();
		}

		public void AddBoundaryNodeReceivedEventHandler (Type type, EventHandler<BoundaryNodeReceivedEventArgs> handler)
		{
			if (type == null || handler == null)
				throw new ArgumentNullException ();
			using (_boundaryHandlerLock.EnterWriteLock ()) {
				_boundaryHandlers.Add (type, handler);
			}
		}

		public void RemoveBoundaryNodeReceivedEventHandler (Type type)
		{
			using (_boundaryHandlerLock.EnterWriteLock ()) {
				_boundaryHandlers.Remove (type);
			}
		}

		public IAsyncResult BeginConnect (Key recipientId, Key destinationId, AnonymousConnectionType type, object payload, AsyncCallback callback, object state)
		{
			SubscribeInfo subscribeInfo;
			using (_subscribeMapLock.EnterReadLock ()) {
				if (!_subscribeMap.TryGetValue (recipientId, out subscribeInfo))
					throw new KeyNotFoundException ();
				if (_subscribeMap.ContainsKey (destinationId))
					throw new ArgumentException ();
			}

			ConnectionInfo info;
			using (_connectionMapLock.EnterWriteLock ()) {
				byte[] raw_id = new byte[2];
				ushort id;
				do {
					RNG.Instance.GetBytes (raw_id);
					id = BitConverter.ToUInt16 (raw_id, 0);
				} while (!_usedConnectionIDs.Add (id));

				info = new ConnectionInfo (this, subscribeInfo, destinationId, id, type, payload, callback, state);
				_connectionMap.Add (info.ConnectionId, info);
			}

			info.Start ();
			return info.AsyncResult;
		}

		public IAnonymousSocket EndConnect (IAsyncResult ar)
		{
			ConnectionInfo.EstablishRouteAsyncResult ar2 = (ConnectionInfo.EstablishRouteAsyncResult)ar;
			ar.AsyncWaitHandle.WaitOne ();
			if (!ar2.ConnectionInfo.IsConnected)
				throw new System.Net.Sockets.SocketException ();
			return ar2.ConnectionInfo.Socket;
		}

		public ISubscribeInfo GetSubscribeInfo (Key recipientId)
		{
			using (_subscribeMapLock.EnterReadLock ()) {
				return _subscribeMap[recipientId];
			}
		}

		public IList<ISubscribeInfo> GetAllSubscribes ()
		{
			List<ISubscribeInfo> list;
			using (_subscribeMapLock.EnterReadLock ()) {
				list = new List<ISubscribeInfo> (_subscribeMap.Count);
				foreach (SubscribeInfo info in _subscribeMap.Values)
					list.Add (info);
			}
			return list.AsReadOnly ();
		}

		public IList<IAnonymousSocket> GetAllConnections ()
		{
			List<IAnonymousSocket> list;
			using (_connectionMapLock.EnterReadLock ()) {
				list = new List<IAnonymousSocket> (_connectionMap.Count);
				foreach (ConnectionInfo info in _connectionMap.Values) {
					if (!info.IsConnected)
						continue;
					list.Add (info.Socket);
				}
			}
			return list.AsReadOnly ();
		}

		public IKeyBasedRouter KeyBasedRouter {
			get { return _kbr; }
		}

		public IDistributedHashTable DistributedHashTable {
			get { return _dht; }
		}

		public void Close ()
		{
			_active = false;
			_interrupter.RemoveInterruption (RouteTimeoutCheck);
			_sock.RemoveInquiryDuplicationCheckType (typeof (EstablishRouteMessage));
			_sock.RemoveInquiryDuplicationCheckType (typeof (RoutedMessage));
			_sock.RemoveInquiredHandler (typeof (EstablishRouteMessage), Messaging_Inquired_EstablishRouteMessage);
			_sock.RemoveInquiredHandler (typeof (RoutedMessage), Messaging_Inquired_RoutedMessage);
			_sock.RemoveInquiredHandler (typeof (DisconnectMessage), Messaging_Inquired_DisconnectMessage);
			_sock.RemoveInquiredHandler (typeof (InterterminalMessage), Messaging_Inquired_InterterminalMessage);
			_sock.RemoveReceivedHandler (typeof (RoutedMessage), Messaging_Received_RoutedMessage);
			_sock.RemoveReceivedHandler (typeof (InterterminalMessage), Messaging_Received_InterterminalMessage);

			using (_subscribeMapLock.EnterWriteLock ()) {
				foreach (SubscribeInfo info in _subscribeMap.Values)
					info.Close ();
				_subscribeMap.Clear ();
			}

			_subscribeMapLock.Dispose ();
			_routingMapLock.Dispose ();
			_connectionMapLock.Dispose ();
			_boundaryHandlerLock.Dispose ();
		}

		#endregion

		#region Messaging Handlers
		void Messaging_Inquired_EstablishRouteMessage (object sender, InquiredEventArgs e)
		{
			_sock.StartResponse (e, AckMessage);

			EstablishRouteMessage msg = (EstablishRouteMessage)e.InquireMessage;
			EstablishRoutePayload payload = MultipleCipherHelper.DecryptEstablishPayload (msg.Payload, _privateNodeKey, _kbr.SelftNodeId.KeyBytes, PayloadFixedSize);
			RouteLabel label = GenerateRouteLabel ();
			AnonymousEndPoint prev = new AnonymousEndPoint (e.EndPoint, msg.Label), next = null;
			RouteInfo routeInfo;

			if (!payload.IsLast) {
				using (_routingMapLock.EnterWriteLock ()) {
					if (_routingMap.ContainsKey (prev))
						return;
					next = new AnonymousEndPoint (payload.NextHopNode, label);
					while (_routingMap.ContainsKey (next)) {
						label = GenerateRouteLabel ();
						next = new AnonymousEndPoint (payload.NextHopNode, label);
					}
					routeInfo = new RouteInfo (prev, next, payload.Key);
					_routingMap.Add (prev, routeInfo);
					_routingMap.Add (next, routeInfo);
				}

				_sock.BeginInquire (new EstablishRouteMessage (label, payload.Payload), payload.NextHopNode, delegate (IAsyncResult ar) {
					if (_sock.EndInquire (ar) == null) {
						routeInfo.Timeout (null);
						_kbr.RoutingAlgorithm.Fail (new NodeHandle (null, payload.NextHopNode));
					}
				}, null);
			} else {
				TerminalPointInfo termInfo;
				using (_routingMapLock.EnterWriteLock ()) {
					if (_routingMap.ContainsKey (prev))
						return;
					next = new AnonymousEndPoint (DummyEndPoint, label);
					while (_routingMap.ContainsKey (next)) {
						label = GenerateRouteLabel ();
						next = new AnonymousEndPoint (DummyEndPoint, label);
					}
					termInfo = new TerminalPointInfo (this, new Key (payload.Payload), label);
					routeInfo = new RouteInfo (prev, termInfo, payload.Key);
					_routingMap.Add (prev, routeInfo);
					_routingMap.Add (next, routeInfo);
				}
				termInfo.SendMessage (_kbr.MessagingSocket, new EstablishedRouteMessage (label), true);
				termInfo.PutToDHT ();
			}
		}

		void Messaging_Received_RoutedMessage (object sender, ReceivedEventArgs e)
		{
			Process_RoutedMessage (e.Message as RoutedMessage, e.RemoteEndPoint, null);
		}

		void Messaging_Inquired_RoutedMessage (object sender, InquiredEventArgs e)
		{
			Process_RoutedMessage (e.InquireMessage as RoutedMessage, e.EndPoint, e);
		}

		void Process_RoutedMessage (RoutedMessage msg, EndPoint endPoint, InquiredEventArgs e)
		{
			RouteInfo routeInfo;
			AnonymousEndPoint senderEP = new AnonymousEndPoint (endPoint, msg.Label);
			using (_routingMapLock.EnterReadLock ()) {
				if (!_routingMap.TryGetValue (senderEP, out routeInfo))
					routeInfo = null;
			}

			if (routeInfo == null) {
				Logger.Log (LogLevel.Trace, this, "No Route from {0}", endPoint, msg.Label);
				if (e != null)
					_sock.StartResponse (e, null);
				return;
			}

			if (e != null)
				_sock.StartResponse (e, AckMessage);
			bool direction = (routeInfo.Previous != null && routeInfo.Previous.Equals (senderEP));
			if (direction) {
				// direction: prev -> ! -> next
				routeInfo.ReceiveMessageFromPreviousNode ();
				if (routeInfo.TerminalPointInfo == null) {
					byte[] payload = MultipleCipherHelper.DecryptRoutedPayload (routeInfo.Key, msg.Payload);
					RoutedMessage new_msg = new RoutedMessage (routeInfo.Next.Label, payload);
					if (e != null) {
						_sock.BeginInquire (new_msg, routeInfo.Next.EndPoint, Messaging_Timeout, Messaging_MaxRetry,
							Messaging_Inquired_RoutedMessage_Callback, new object[] { routeInfo, routeInfo.Next });
					} else {
						_sock.Send (new_msg, routeInfo.Next.EndPoint);
					}
					Logger.Log (LogLevel.Trace, this, "Recv Routed {0} -> {1}", routeInfo.Previous, routeInfo.Next);
				} else {
					object payload = MultipleCipherHelper.DecryptRoutedPayloadAtTerminal (routeInfo.Key, msg.Payload);
					ICheckDuplication dupCheckMsg = payload as ICheckDuplication;
					if (dupCheckMsg != null && !_terminalDupChecker.Check (dupCheckMsg.DuplicationCheckValue))
						return;
					ProcessMessage (payload, routeInfo.TerminalPointInfo, e != null);
					Logger.Log (LogLevel.Trace, this, "TerminalPoint: Received {0}", payload);
				}
			} else {
				// direction: next -> ! -> prev
				routeInfo.ReceiveMessageFromNextNode ();
				if (routeInfo.StartPointInfo == null) {
					byte[] payload = MultipleCipherHelper.EncryptRoutedPayload (routeInfo.Key, msg.Payload);
					RoutedMessage new_msg = new RoutedMessage (routeInfo.Previous.Label, payload);
					if (e != null) {
						_sock.BeginInquire (new_msg, routeInfo.Previous.EndPoint, Messaging_Timeout, Messaging_MaxRetry,
							Messaging_Inquired_RoutedMessage_Callback, new object[] { routeInfo, routeInfo.Previous });
					} else {
						_sock.Send (new_msg, routeInfo.Previous.EndPoint);
					}
					Logger.Log (LogLevel.Trace, this, "Recv Routed {0} <- {1}", routeInfo.Previous, routeInfo.Next);
				} else {
					object payload = MultipleCipherHelper.DecryptRoutedPayload (routeInfo.StartPointInfo.RelayKeys, msg.Payload);
					ICheckDuplication dupCheckMsg = payload as ICheckDuplication;
					if (dupCheckMsg != null && !_startPointDupChecker.Check (dupCheckMsg.DuplicationCheckValue))
						return;
					ProcessMessage (payload, routeInfo.StartPointInfo, e != null);
					Logger.Log (LogLevel.Trace, this, "StartPoint: Received {0}", payload);
				}
			}
		}

		void Messaging_Inquired_RoutedMessage_Callback (IAsyncResult ar)
		{
			object res = _sock.EndInquire (ar);
			object[] states = (object[])ar.AsyncState;
			RouteInfo routeInfo = states[0] as RouteInfo;
			AnonymousEndPoint dest = (AnonymousEndPoint)states[1];
			if (res == null) {
				routeInfo.Timeout (routeInfo.Next == dest ? _sock : null);
				_kbr.RoutingAlgorithm.Fail (new NodeHandle (null, dest.EndPoint));
			} else {
				if (routeInfo.Previous == dest)
					routeInfo.ReceiveMessageFromPreviousNode ();
				else if (routeInfo.Next == dest)
					routeInfo.ReceiveMessageFromNextNode ();
			}
		}

		void Messaging_Received_InterterminalMessage (object sender, ReceivedEventArgs e)
		{
			Process_InterterminalMessage (e.Message as InterterminalMessage, null);
		}

		void Messaging_Inquired_InterterminalMessage (object sender, InquiredEventArgs e)
		{
			Process_InterterminalMessage (e.InquireMessage as InterterminalMessage, e);
		}

		void Process_InterterminalMessage (InterterminalMessage msg, InquiredEventArgs e)
		{
			RouteInfo routeInfo;
			ICheckDuplication msg2 = msg.Message as ICheckDuplication;
			if (msg2 == null) {
				if (e != null)
					_sock.StartResponse (e, null);
				Logger.Log (LogLevel.Fatal, this, "BUG #1");
				return;
			}

			using (_routingMapLock.EnterReadLock ()) {
				if (!_routingMap.TryGetValue (new AnonymousEndPoint (DummyEndPoint, msg.Label), out routeInfo))
					routeInfo = null;
			}
			if (routeInfo == null || routeInfo.TerminalPointInfo == null) {
				if (e != null)
					_sock.StartResponse (e, null);
				Logger.Log (LogLevel.Trace, this, "No Interterminal Route");
				return;
			}

			if (e != null)
				_sock.StartResponse (e, AckMessage);
			if (!_interterminalDupChecker.Check (msg2.DuplicationCheckValue))
				return;
			routeInfo.TerminalPointInfo.SendMessage (_sock, msg.Message, e != null);
		}

		void Messaging_Inquired_DisconnectMessage (object sender, InquiredEventArgs e)
		{
			_sock.StartResponse (e, AckMessage);

			DisconnectMessage msg = (DisconnectMessage)e.InquireMessage;
			RouteInfo routeInfo;
			AnonymousEndPoint senderEP = new AnonymousEndPoint (e.EndPoint, msg.Label);
			using (_routingMapLock.EnterReadLock ()) {
				if (!_routingMap.TryGetValue (senderEP, out routeInfo))
					return;
			}

			if (routeInfo.Next != null && routeInfo.Next.Equals (senderEP)) {
				routeInfo.Timeout (_sock);
			} else {
				Logger.Log (LogLevel.Error, this, "BUG (Direction of sending disconnect msg)");
			}
		}
		#endregion

		#region Message Handlers
		void ProcessMessage (object msg_obj, StartPointInfo info, bool useInquiry)
		{
			{
				EstablishedRouteMessage msg = msg_obj as EstablishedRouteMessage;
				if (msg != null) {
					info.Established (msg);
					return;
				}
			}
			{
				ConnectionEstablishMessage msg = msg_obj as ConnectionEstablishMessage;
				if (msg != null) {
					ECKeyPair pubKey = ECKeyPairExtensions.CreatePublic (msg.EphemeralPublicKey);
					ConnectionEstablishPayload payload = ConnectionEstablishPayload.Decrypt (info.SubscribeInfo.PrivateKey, pubKey, msg.Encrypted);
					SymmetricKey tmpKey = MultipleCipherHelper.ComputeSharedKey (info.SubscribeInfo.PrivateKey,
						payload.InitiatorId.ToECPublicKey (), payload.SharedInfo, PaddingMode.None, false);
					if (!ConnectionInfo.CheckMAC (tmpKey, msg.Encrypted, msg.MAC)) {
						Logger.Log (LogLevel.Error, this, "MAC Error");
						return;
					}

					SubscribeInfo subscribeInfo = info.SubscribeInfo;
					AcceptingEventArgs args = new AcceptingEventArgs (subscribeInfo.Key, payload.InitiatorId, payload.ConnectionType, payload.Payload);
					if (!subscribeInfo.InvokeAccepting (args))
						return; // Reject

					ConnectionInfo cinfo;
					uint id = (uint)payload.ConnectionId << 16;
					byte[] sharedInfo = RNG.GetRNGBytes (DefaultSharedInfoSize);
					using (_connectionMapLock.EnterWriteLock ()) {
						while (_connectionMap.ContainsKey (id))
							id ++;
						cinfo = new ConnectionInfo (this, subscribeInfo, id, sharedInfo, payload.ConnectionType, pubKey, payload);
						_connectionMap.Add (id, cinfo);
					}
					cinfo.Start (sharedInfo, args.Response);

					subscribeInfo.InvokeAccepted (new AcceptedEventArgs (cinfo.Socket, args));
					return;
				}
			}
			{
				ConnectionReciverSideMessage msg = msg_obj as ConnectionReciverSideMessage;
				if (msg != null) {
					ConnectionInfo cinfo;
					using (_connectionMapLock.EnterReadLock ()) {
						if (!_connectionMap.TryGetValue (msg.ConnectionId, out cinfo)) {
							if (!_connectionMap.TryGetValue (msg.ConnectionId & 0xFFFF0000, out cinfo) || (!cinfo.IsInitiator && !cinfo.IsConnected)) {
								Logger.Log (LogLevel.Warn, this, "Unknown Connection");
								return;
							}
						}
					}
					if (!cinfo.IsConnected && (msg.PayloadMAC == null || msg.PayloadMAC.Length == 0)) {
						ConnectionEstablishedMessage msg2 = (ConnectionEstablishedMessage)DefaultSerializer.Deserialize (msg.Payload);
						SymmetricKey key = cinfo.ComputeSharedKey (msg2.SharedInfo);
						if (!ConnectionInfo.CheckMAC (key, msg2.SharedInfo.Join (msg2.Encrypted), msg2.MAC)) {
							Logger.Log (LogLevel.Error, this, "MAC Error");
							return;
						}
						if (msg.ConnectionId != cinfo.ConnectionId) {
							using (_connectionMapLock.EnterWriteLock ()) {
								_connectionMap.Remove (msg.ConnectionId);
								_connectionMap.Add (msg.ConnectionId, cinfo);
								cinfo.ConnectionId = msg.ConnectionId;
							}
						}
						object response = null;
						if (msg2.Encrypted.Length > 0)
							response = DefaultSerializer.Deserialize (key.Decrypt (msg2.Encrypted, 0, msg2.Encrypted.Length));
						cinfo.Established (key, msg.SenderSideTerminalEndPoints, response);
						Logger.Log (LogLevel.Trace, cinfo, "Connection Established");
					} else {
						cinfo.Received (msg);
					}
					return;
				}
			}
			{
				InsideMessage msg = msg_obj as InsideMessage;
				if (msg != null) {
					info.SubscribeInfo.ReceiveInsideMessage (msg);
					return;
				}
			}
			Logger.Log (LogLevel.Error, this, "Unknown Message at StartPoint {0}", msg_obj);
		}

		void ProcessMessage (object msg_obj, TerminalPointInfo info, bool useInquiry)
		{
			{
				if (msg_obj is Ping)
					return;
			}
			{
				ConnectionEstablishMessage msg = msg_obj as ConnectionEstablishMessage;
				if (msg != null) {
					_dht.BeginGet (msg.DestinationId, typeof (DHTEntry), ProcessMessage_ConnectionEstablish_DHTGet_Callback, msg);
					return;
				}
			}
			{
				ConnectionSenderSideMessage msg = msg_obj as ConnectionSenderSideMessage;
				if (msg != null) {
					ConnectionReciverSideMessage msg2 = new ConnectionReciverSideMessage (
						msg.SenderSideTerminalEndPoints, msg.ConnectionId, msg.DuplicationCheckValue, msg.Payload, msg.PayloadMAC);
					InterterminalState state = new InterterminalState (msg.ReciverSideTerminalEndPoints, msg.Destination, msg2);
					foreach (AnonymousEndPoint ep in msg.ReciverSideTerminalEndPoints) {
						InterterminalMessage imsg = new InterterminalMessage (ep.Label, msg2);
						if (useInquiry) {
							_sock.BeginInquire (imsg, ep.EndPoint, ProcessMessage_ConnectionSenderSide_Interterminal_Callback, new object[]{state, ep.EndPoint});
						} else {
							_sock.Send (imsg, ep.EndPoint);
						}
					}
					return;
				}
			}
			{
				InsideMessage msg = msg_obj as InsideMessage;
				if (msg != null) {
					if (msg.Payload == null) {
						Logger.Log (LogLevel.Error, this, "Payload of InsideMessage is null");
						return;
					}
					EventHandler<BoundaryNodeReceivedEventArgs> handler;
					using (_boundaryHandlerLock.EnterReadLock ()) {
						if (!_boundaryHandlers.TryGetValue (msg.Payload.GetType (), out handler)) {
							Logger.Log (LogLevel.Error, this, "Unknown Payload ({0}) of InsideMessage", msg.Payload.GetType ());
							return;
						}
					}
					handler (this, info.CreateBoundaryNodeReceivedEventArgs (_sock, msg));
					return;
				}
			}
			Logger.Log (LogLevel.Error, this, "Unknown Message at TerminalPointInfo {0}", msg_obj);
		}

		void ProcessMessage_ConnectionEstablish_DHTGet_Callback (IAsyncResult ar)
		{
			ConnectionEstablishMessage msg = (ConnectionEstablishMessage)ar.AsyncState;
			GetResult result = _dht.EndGet (ar);
			if (result == null || result.Values == null || result.Values.Length == 0) {
				Logger.Log (LogLevel.Trace, this, "DHT Lookup Failed. Key={0}", msg.DestinationId);
				return;
			}
			object[] results = result.Values.RandomSelection (AC_DefaultUseSubscribeRoutes);
			for (int i = 0; i < results.Length; i++) {
				DHTEntry entry = results[i] as DHTEntry;
				if (entry == null) continue;
				_sock.BeginInquire (new InterterminalMessage (entry.Label, msg), entry.EndPoint, null, null);
			}
		}

		void ProcessMessage_ConnectionSenderSide_Interterminal_Callback (IAsyncResult ar)
		{
			object[] states = (object[])ar.AsyncState;
			InterterminalState state = (InterterminalState)states[0];
			EndPoint ep = (EndPoint)states[1];
			object ret = _sock.EndInquire (ar);
			bool completed;
			if (ret == null) {
				completed = state.Fail ();
				_kbr.RoutingAlgorithm.Fail (new NodeHandle (null, ep));
			} else {
				completed = state.Success ();
			}
			if (completed) {
				Thread.MemoryBarrier ();
				if (state.Fails == state.Count) {
					_dht.BeginGet (state.Destination, typeof (DHTEntry), ProcessMessage_ConnectionSenderSide_Interterminal_DHT_Callback, state);
				}
			}
		}

		void ProcessMessage_ConnectionSenderSide_Interterminal_DHT_Callback (IAsyncResult ar)
		{
			InterterminalState state = (InterterminalState)ar.AsyncState;
			GetResult ret = _dht.EndGet (ar);
			if (ret == null || ret.Values == null || ret.Values.Length == 0) {
				Logger.Log (LogLevel.Trace, this, "DHT Lookup Failed (Inter-terminal)");
				return;
			}

			List<AnonymousEndPoint> list = new List<AnonymousEndPoint> ();
			for (int i = 0; i < ret.Values.Length; i ++) {
				DHTEntry entry = ret.Values[i] as DHTEntry;
				if (entry == null) continue;
				AnonymousEndPoint ep = new AnonymousEndPoint (entry.EndPoint, entry.Label);
				if (!state.Set.Contains (ep))
					list.Add (ep);
			}
			if (list.Count == 0) {
				Logger.Log (LogLevel.Trace, this, "DHT result data is too old (Inter-terminal)");
				return;
			}
			foreach (AnonymousEndPoint ep in list) {
				_sock.BeginInquire (new InterterminalMessage (ep.Label, state.Message), ep.EndPoint, null, null);
			}
		}

		class InterterminalState
		{
			int _count;
			int _fail = 0;
			int _returned = 0;
			Key _dest;
			ConnectionReciverSideMessage _msg;
			HashSet<AnonymousEndPoint> _set;

			public InterterminalState (AnonymousEndPoint[] eps, Key dest, ConnectionReciverSideMessage msg)
			{
				_count = eps.Length;
				_set = new HashSet<AnonymousEndPoint> (eps);
				_dest = dest;
				_msg = msg;
			}

			public bool Fail ()
			{
				Interlocked.Increment (ref _fail);
				return (Interlocked.Increment (ref _returned) == _count);
			}

			public bool Success ()
			{
				return (Interlocked.Increment (ref _returned) == _count);
			}

			public Key Destination {
				get { return _dest; }
			}

			public ConnectionReciverSideMessage Message {
				get { return _msg; }
			}

			public HashSet<AnonymousEndPoint> Set {
				get { return _set; }
			}

			public int Fails {
				get { return _fail; }
			}

			public int Count {
				get { return _count; }
			}
		}
		#endregion

		#region Timeout Check
		void RouteTimeoutCheck ()
		{
			if (!_active) return;
			using (_connectionMapLock.EnterUpgradeableReadLock ()) {
				List<ConnectionInfo> timeoutConnections = null;
				foreach (ConnectionInfo cinfo in _connectionMap.Values) {
					if (cinfo.IsExpiry) {
						if (timeoutConnections == null)
							timeoutConnections = new List<ConnectionInfo> ();
						timeoutConnections.Add (cinfo);
					} else if (cinfo.NextPingTime <= DateTime.Now) {
						cinfo.SendPing ();
					}
				}

				if (timeoutConnections != null) {
					using (_connectionMapLock.EnterWriteLock ()) {
						foreach (ConnectionInfo cinfo in timeoutConnections) {
							if (cinfo.IsInitiator)
								_usedConnectionIDs.Remove ((ushort)(cinfo.ConnectionId >> 16));
							cinfo.Close ();
							_connectionMap.Remove (cinfo.ConnectionId);
							Logger.Log (LogLevel.Trace, this, "Timeout AC {0}", cinfo.ConnectionId);
						}
					}
				}
			}

			if (!_active) return;
			List<KeyValuePair<AnonymousEndPoint, RouteInfo>> timeouts = null;
			HashSet<StartPointInfo> aliveCheckList = null;
			HashSet<TerminalPointInfo> dhtPutList = null;
			using (_routingMapLock.EnterUpgradeableReadLock ()) {
				foreach (KeyValuePair<AnonymousEndPoint, RouteInfo> pair in _routingMap) {
					if (pair.Value.IsExpiry ()) {
						if (timeouts == null)
							timeouts = new List<KeyValuePair<AnonymousEndPoint, RouteInfo>> ();
						timeouts.Add (pair);
					} else if (pair.Value.StartPointInfo != null && pair.Value.StartPointInfo.NextPingTime <= DateTime.Now) {
						if (aliveCheckList == null)
							aliveCheckList = new HashSet<StartPointInfo> ();
						aliveCheckList.Add (pair.Value.StartPointInfo);
					} else if (pair.Value.TerminalPointInfo != null && pair.Value.TerminalPointInfo.NextPutTime <= DateTime.Now) {
						if (dhtPutList == null)
							dhtPutList = new HashSet<TerminalPointInfo> ();
						dhtPutList.Add (pair.Value.TerminalPointInfo);
					}
				}
				if (timeouts != null && timeouts.Count > 0) {
					using (_routingMapLock.EnterWriteLock ()) {
						for (int i = 0; i < timeouts.Count; i ++)
							_routingMap.Remove (timeouts[i].Key);
					}
				}
			}

			if (!_active) return;
			if (timeouts != null) {
				Logger.Log (LogLevel.Trace, this, "Timeout {0} Relay Nodes", timeouts.Count);
				for (int i = 0; i < timeouts.Count; i ++) {
					RouteInfo ri = timeouts[i].Value;
					if (ri.StartPointInfo != null)
						ri.StartPointInfo.SubscribeInfo.Close (ri.StartPointInfo);
				}
			}
			if (aliveCheckList != null) {
				foreach (StartPointInfo info in aliveCheckList)
					info.SendMessage (_kbr.MessagingSocket, Ping.Instance, true);
			}
			if (dhtPutList != null) {
				foreach (TerminalPointInfo info in dhtPutList)
					info.PutToDHT ();
			}

			if (!_active) return;
			using (_subscribeMapLock.EnterReadLock ()) {
				foreach (SubscribeInfo info in _subscribeMap.Values) {
					info.CheckNumberOfEstablishedRoutes ();
				}
			}
		}
		#endregion

		#region Misc
		static RouteLabel GenerateRouteLabel ()
		{
			byte[] raw = RNG.GetRNGBytes (4);
			return BitConverter.ToInt32 (raw, 0);
		}
		static DupCheckLabel GenerateDuplicationCheckValue ()
		{
			byte[] raw = RNG.GetRNGBytes (8);
			return BitConverter.ToInt64 (raw, 0);
		}
		#endregion

		#region SubscribeInfo
		class SubscribeInfo : ISubscribeInfo
		{
			Key _key;
			ECKeyPair _privateKey;
			AnonymousRouter _router;
			int _numOfRoutes, _numOfRelays;
			float _factor;

			bool _active = true;
			SubscribeRouteStatus _status = SubscribeRouteStatus.Establishing;
			object _listLock = new object ();
			HashSet<StartPointInfo> _establishedList = new HashSet<StartPointInfo> ();
			HashSet<StartPointInfo> _establishingList = new HashSet<StartPointInfo> ();
			MessagingObjectSocket _msock = null;

			public event AcceptingEventHandler Accepting;
			public event AcceptedEventHandler Accepted;

			public SubscribeInfo (AnonymousRouter router, Key key, ECKeyPair privateKey, int numOfRoutes, int numOfRelays, float factor)
			{
				_router = router;
				_key = key;
				_privateKey = privateKey;
				_numOfRoutes = numOfRoutes;
				_numOfRelays = numOfRelays;
				_factor = factor;

				TimeSpan timeout = p2pncs.Net.Overlay.Anonymous.AnonymousRouter.MCR_EstablishingTimeout + p2pncs.Net.Overlay.Anonymous.AnonymousRouter.DHT_GetTimeout;
				_msock = new MessagingObjectSocket (this, router._interrupter, timeout, 2, 128, 16);
			}

			public void Start ()
			{
				CheckNumberOfEstablishedRoutes ();
			}

			public void CheckNumberOfEstablishedRoutes ()
			{
				if (!_active) return;
				lock (_listLock) {
					if (_establishedList.Count < _numOfRoutes) {
						int expectedCount = (int)Math.Ceiling ((_numOfRoutes - _establishedList.Count) * _factor);
						int count = expectedCount - _establishingList.Count;
						while (count-- > 0) {
							NodeHandle[] relays = _router.KeyBasedRouter.RoutingAlgorithm.GetRandomNodes (_numOfRelays);
							if (relays.Length < _numOfRelays) {
								Logger.Log (LogLevel.Trace, this, "Relay node selection failed");
								return;
							}

							SymmetricKey[] relayKeys = new SymmetricKey[relays.Length];
							byte[] payload = MultipleCipherHelper.CreateEstablishPayload (relays, relayKeys, _key, _privateKey.DomainName, _router.PayloadFixedSize);

							RouteLabel label = GenerateRouteLabel ();
							AnonymousEndPoint ep = new AnonymousEndPoint (relays[0].EndPoint, label);
							StartPointInfo info;
							using (_router._routingMapLock.EnterWriteLock ()) {
								while (_router._routingMap.ContainsKey (ep)) {
									label = GenerateRouteLabel ();
									ep = new AnonymousEndPoint (relays[0].EndPoint, label);
								}
								info = new StartPointInfo (this, relays, relayKeys, label);
								RouteInfo routeInfo = new RouteInfo (info, ep);
								_router._routingMap.Add (ep, routeInfo);
							}
							_establishingList.Add (info);
							EstablishRouteMessage msg = new EstablishRouteMessage (info.Label, payload);
							_router.KeyBasedRouter.MessagingSocket.BeginInquire (msg, info.RelayNodes[0].EndPoint,
								Messaging_Timeout, Messaging_MaxRetry, EstablishRoute_Callback, info);
						}
					}
				}
			}

			void EstablishRoute_Callback (IAsyncResult ar)
			{
				object ret = _router.KeyBasedRouter.MessagingSocket.EndInquire (ar);
				StartPointInfo info = (StartPointInfo)ar.AsyncState;
				if (ret == null) {
					info.RouteInfo.Timeout (null);
					lock (_listLock) {
						_establishingList.Remove (info);
						UpdateStatus ();
					}
					_router.KeyBasedRouter.RoutingAlgorithm.Fail (info.RelayNodes[0]);
					CheckNumberOfEstablishedRoutes ();
				}
			}

			public void Established (StartPointInfo info)
			{
				lock (_listLock) {
					_establishingList.Remove (info);
					_establishedList.Add (info);
					UpdateStatus ();
				}
				Logger.Log (LogLevel.Trace, this, "Established");
			}

			public void SendMessage (object msg, int routes, bool useInquiry)
			{
				StartPointInfo[] array;
				lock (_listLock) {
					array = new StartPointInfo[_establishedList.Count];
					_establishedList.CopyTo (array);
				}
				array = array.RandomSelection (routes);
				for (int i = 0; i < array.Length; i ++)
					array[i].SendMessage (_router.KeyBasedRouter.MessagingSocket, msg, useInquiry);
			}

			public void ReceiveInsideMessage (InsideMessage msg)
			{
				_msock.Received (msg);
			}

			public bool InvokeAccepting (AcceptingEventArgs args)
			{
				if (Accepting == null || Accepted == null)
					return false;

				try {
					Accepting (this, args);
				} catch {}
				return args.Accepted;
			}

			public void InvokeAccepted (AcceptedEventArgs args)
			{
				try {
					Accepted (this, args);
				} catch {}
			}

			public AnonymousEndPoint[] GetRouteEndPoints ()
			{
				lock (_listLock) {
					AnonymousEndPoint[] endPoints = new AnonymousEndPoint[_establishedList.Count];
					int i = 0;
					foreach (StartPointInfo info in _establishedList) {
						endPoints[i ++] = info.TerminalEndPoint;
					}
					return endPoints;
				}
			}

			void UpdateStatus ()
			{
				if (!_active) {
					_status = SubscribeRouteStatus.Disconnected;
					return;
				}
				if (_establishedList.Count == 0)
					_status = SubscribeRouteStatus.Establishing;
				else if (_establishedList.Count < DefaultSubscribeRoutes)
					_status = SubscribeRouteStatus.Unstable;
				else
					_status = SubscribeRouteStatus.Stable;
			}

			public void Close (StartPointInfo info)
			{
				lock (_listLock) {
					_establishingList.Remove (info);
					_establishedList.Remove (info);
					UpdateStatus ();
				}
				CheckNumberOfEstablishedRoutes ();
			}

			public void Close ()
			{
				_active = false;
				_msock.Dispose ();
			}

			#region ISubscribeInfo Members

			public Key Key {
				get { return _key; }
			}

			public ECKeyPair PrivateKey {
				get { return _privateKey; }
			}

			public SubscribeRouteStatus Status {
				get { return _status; }
			}

			public IAnonymousRouter AnonymousRouter {
				get { return _router; }
			}

			public IMessagingSocket MessagingSocket {
				get { return _msock; }
			}

			#endregion

			#region Internal Class
			class MessagingObjectSocket : MessagingSocketBase
			{
				SubscribeInfo _info;

				public MessagingObjectSocket (SubscribeInfo info, IntervalInterrupter insideTimeoutTimer,
					TimeSpan timeout, int maxRetry, int retryBufferSize, int inquiryDupCheckSize)
					: base (null, true, insideTimeoutTimer, timeout, maxRetry, retryBufferSize, inquiryDupCheckSize)
				{
					_info = info;
				}

				protected override uint CreateMessageID ()
				{
					while (true) {
						uint id = base.CreateMessageID ();
						if (id != 0)
							return id;
					}
				}

				public void Received (InsideMessage msg)
				{
					if (!IsActive)
						return;
					if (msg.ID == 0) {
						InvokeReceived (this, new ReceivedEventArgs (msg.Payload, null));
						return;
					}

					InquiredAsyncResultBase ar = RemoveFromRetryList (msg.ID, DummyEndPoint);
					if (ar == null)
						return;
					ar.Complete (msg.Payload, this);
					InvokeInquirySuccess (this, new InquiredEventArgs (ar.Request, msg.Payload, null, DateTime.Now - ar.TransmitTime, ar.RetryCount));
				}

				public override void Send (object obj, EndPoint remoteEP)
				{
					_info.SendMessage (new InsideMessage (0, obj), 1, true);
				}

				protected override void StartResponse_Internal (InquiredResponseState state, object response)
				{
					throw new NotSupportedException ();
				}

				public override int MaxMessageSize {
					get { return 0; }
				}

				protected override InquiredAsyncResultBase CreateInquiredAsyncResult (uint id, object obj, EndPoint remoteEP, TimeSpan timeout, int maxRetry, AsyncCallback callback, object state)
				{
					return new InquiredAsyncResult (_info, obj, DummyEndPoint, id, timeout, maxRetry, callback, state);
				}

				class InquiredAsyncResult : InquiredAsyncResultBase
				{
					SubscribeInfo _info;

					public InquiredAsyncResult (SubscribeInfo info, object req, EndPoint remoteEP, uint id, TimeSpan timeout, int maxRetry, AsyncCallback callback, object state)
						: base (req, remoteEP, id, timeout, maxRetry, callback, state)
					{
						_info = info;
					}

					protected override void Transmit_Internal (IDatagramEventSocket sock)
					{
						_info.SendMessage (new InsideMessage (_id, _req), _info._numOfRoutes, true);
					}
				}
			}
			#endregion
		}

		class StartPointInfo
		{
			NodeHandle[] _relayNodes;
			SymmetricKey[] _relayKeys;
			RouteLabel _label;
			AnonymousEndPoint _termEP = null;
			DateTime _nextPingTime = DateTime.MaxValue;
			SubscribeInfo _subscribe;
			RouteInfo _routeInfo = null;

			public StartPointInfo (SubscribeInfo subscribe, NodeHandle[] relayNodes, SymmetricKey[] relayKeys, RouteLabel label)
			{
				_subscribe = subscribe;
				_relayNodes = relayNodes;
				_relayKeys = relayKeys;
				_label = label;
			}

			public byte[] CreateEstablishData (Key recipientId, ECDomainNames domain)
			{
				if (_relayKeys != null)
					throw new Exception ();
				_relayKeys = new SymmetricKey[_relayNodes.Length];
				return MultipleCipherHelper.CreateEstablishPayload (_relayNodes, _relayKeys, recipientId, domain,
					(_subscribe.AnonymousRouter as AnonymousRouter).PayloadFixedSize);
			}

			public void Established (EstablishedRouteMessage msg)
			{
				_termEP = new AnonymousEndPoint (_relayNodes[_relayNodes.Length - 1].EndPoint, msg.Label);
				_nextPingTime = DateTime.Now + MCR_AliveCheckScheduleInterval;
				_subscribe.Established (this);
			}

			public void SendMessage (IMessagingSocket sock, object msg, bool useInquiry)
			{
				_nextPingTime = DateTime.Now + MCR_AliveCheckScheduleInterval;
				byte[] payload = MultipleCipherHelper.CreateRoutedPayload (_relayKeys, msg, (_subscribe.AnonymousRouter as AnonymousRouter).PayloadFixedSize);
				RoutedMessage rmsg = new RoutedMessage (_routeInfo.Next.Label, payload);
				if (useInquiry) {
					sock.BeginInquire (rmsg, _routeInfo.Next.EndPoint, Messaging_Timeout, Messaging_MaxRetry, SendMessage_Callback, sock);
				} else {
					sock.Send (rmsg, _routeInfo.Next.EndPoint);
				}
			}
			void SendMessage_Callback (IAsyncResult ar)
			{
				IMessagingSocket sock = (IMessagingSocket)ar.AsyncState;
				if (sock.EndInquire (ar) == null) {
					_routeInfo.Timeout (null);
					_subscribe.AnonymousRouter.KeyBasedRouter.RoutingAlgorithm.Fail (new NodeHandle (null, _routeInfo.Next.EndPoint));
				} else {
					_routeInfo.ReceiveMessageFromNextNode ();
				}
			}

			public NodeHandle[] RelayNodes {
				get { return _relayNodes; }
			}

			public SymmetricKey[] RelayKeys {
				get { return _relayKeys; }
			}

			public RouteLabel Label {
				get { return _label; }
			}

			public RouteInfo RouteInfo {
				get { return _routeInfo; }
				set { _routeInfo = value;}
			}

			public SubscribeInfo SubscribeInfo {
				get { return _subscribe; }
			}

			public DateTime NextPingTime {
				get { return _nextPingTime; }
			}

			public AnonymousEndPoint TerminalEndPoint {
				get { return _termEP; }
			}
		}

		class TerminalPointInfo
		{
			bool _active = true;
			AnonymousEndPoint _dummyEP;
			Key _recipientKey;
			RouteInfo _routeInfo = null;
			AnonymousRouter _router;
			DateTime _nextPutTime = DateTime.MaxValue;

			public TerminalPointInfo (AnonymousRouter router, Key recipientKey, RouteLabel label)
			{
				_router = router;
				_recipientKey = recipientKey;
				_dummyEP = new AnonymousEndPoint (DummyEndPoint, label);
			}

			public void SendMessage (IMessagingSocket sock, object msg, bool useInquery)
			{
				if (!_active) return;
				byte[] payload = MultipleCipherHelper.CreateRoutedPayload (_routeInfo.Key, msg, _router.PayloadFixedSize);
				RoutedMessage rmsg = new RoutedMessage (_routeInfo.Previous.Label, payload);
				if (useInquery) {
					sock.BeginInquire (rmsg, _routeInfo.Previous.EndPoint, Messaging_Timeout, Messaging_MaxRetry, SendMessage_Callback, sock);
				} else {
					sock.Send (rmsg, _routeInfo.Previous.EndPoint);
				}
			}
			void SendMessage_Callback (IAsyncResult ar)
			{
				IMessagingSocket sock = (IMessagingSocket)ar.AsyncState;
				if (sock.EndInquire (ar) == null) {
					_routeInfo.Timeout (null);
					_router.KeyBasedRouter.RoutingAlgorithm.Fail (new NodeHandle (null, _routeInfo.Previous.EndPoint));
				} else {
					_routeInfo.ReceiveMessageFromPreviousNode ();
				}
			}

			public void PutToDHT ()
			{
				_router.DistributedHashTable.BeginPut (_recipientKey, DHT_Lifetime, new DHTEntry (_dummyEP.Label), null, null);
				_nextPutTime = DateTime.Now + DHT_PutInterval;
			}

			public Key RecipientKey {
				get { return _recipientKey; }
			}

			public AnonymousEndPoint LabelOnlyAnonymousEndPoint {
				get { return _dummyEP; }
			}

			public RouteInfo RouteInfo {
				set { _routeInfo = value; }
			}

			public DateTime NextPutTime {
				get { return _nextPutTime; }
			}

			public BoundaryNodeReceivedEventArgs CreateBoundaryNodeReceivedEventArgs (IMessagingSocket sock, InsideMessage msg)
			{
				return new ReceivedEventArgs (this, sock, msg);
			}

			class ReceivedEventArgs : BoundaryNodeReceivedEventArgs
			{
				TerminalPointInfo _info;
				IMessagingSocket _sock;
				bool _responsed = false;
				uint _id;

				public ReceivedEventArgs (TerminalPointInfo info, IMessagingSocket sock, InsideMessage msg) : base (info.RecipientKey, msg.Payload, msg.ID != 0)
				{
					_info = info;
					_sock = sock;
					_id = msg.ID;
				}

				public override void StartResponse (object response)
				{
					if (_id == 0)
						throw new NotSupportedException ();

					TerminalPointInfo info = _info;
					lock (this) {
						if (_responsed)
							return;
						_responsed = true;
					}
					info.SendMessage (_sock, new InsideMessage (_id, response), true);
				}

				public override void SendMessage (object msg)
				{
					_info.SendMessage (_sock, new InsideMessage (0, msg), true);
				}
			}
		}

		class RouteInfo
		{
			AnonymousEndPoint _prevEP;
			AnonymousEndPoint _nextEP;
			SymmetricKey _key;
			StartPointInfo _startPoint;
			TerminalPointInfo _termPoint;
			DateTime _prevExpiry, _nextExpiry;

			public RouteInfo (StartPointInfo startPoint, AnonymousEndPoint next)
				: this (null, next, null, startPoint, null)
			{
			}

			public RouteInfo (AnonymousEndPoint prev, AnonymousEndPoint next, SymmetricKey key)
				: this (prev, next, key, null, null)
			{
			}

			public RouteInfo (AnonymousEndPoint prev, TerminalPointInfo termPoint, SymmetricKey key)
				: this (prev, null, key, null, termPoint)
			{
			}

			RouteInfo (AnonymousEndPoint prev, AnonymousEndPoint next, SymmetricKey key, StartPointInfo startPoint, TerminalPointInfo termPoint)
			{
				_prevEP = prev;
				_nextEP = next;
				_key = key;
				_startPoint = startPoint;
				_termPoint = termPoint;

				if (termPoint != null)
					termPoint.RouteInfo = this;
				_prevExpiry = (prev == null ? DateTime.MaxValue : DateTime.Now + MCR_MaxMessageIntervalWithMargin);

				if (startPoint != null) {
					startPoint.RouteInfo = this;
					_nextExpiry = DateTime.Now + MCR_EstablishingTimeout;
				} else {
					_nextExpiry = (next == null ? DateTime.MaxValue : DateTime.Now + MCR_MaxMessageIntervalWithMargin);
				}
			}

			public void ReceiveMessageFromNextNode ()
			{
				if (_nextExpiry != DateTime.MinValue && _nextExpiry != DateTime.MaxValue)
					_nextExpiry = DateTime.Now + MCR_MaxMessageIntervalWithMargin;
			}

			public void ReceiveMessageFromPreviousNode ()
			{
				if (_prevExpiry != DateTime.MinValue && _prevExpiry != DateTime.MaxValue)
					_prevExpiry = DateTime.Now + MCR_MaxMessageIntervalWithMargin;
			}

			public bool IsExpiry ()
			{
				return _nextExpiry <= DateTime.Now || _prevExpiry <= DateTime.Now;
			}

			public void Timeout (IMessagingSocket sock)
			{
				_nextExpiry = DateTime.MinValue;
				_prevExpiry = DateTime.MinValue;
				if (sock != null && _prevEP != null)
					sock.BeginInquire (new DisconnectMessage (_prevEP.Label), _prevEP.EndPoint,
						DisconnectMessaging_Timeout, DisconnectMessaging_MaxRetry, null, null);
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

			public TerminalPointInfo TerminalPointInfo {
				get { return _termPoint; }
			}
		}
		#endregion

		#region ConnectionInfo
		class ConnectionInfo
		{
			bool _initiator;
			bool _connected = false;
			ManualResetEvent _estalibhDone;
			EstablishRouteAsyncResult _ar;
			AnonymousRouter _router;
			SubscribeInfo _subscribeInfo;
			Key _destKey;
			ECKeyPair _connectionPrivate;
			uint _connectionId;
			byte[] _sharedInfo;
			SymmetricKey _key;
			AnonymousEndPoint[] _otherSideTermEPs;
			ConnectionSocket _sock = null;
			DateTime _nextPingTime = DateTime.MaxValue;
			DateTime _expiry = DateTime.MaxValue;
			AnonymousConnectionType _type;
			object _dtLock = new object (), _payload, _msgAtEstablishing;
			int _useRoutesWhenEstablishing = AC_DefaultUseSubscribeRoutes;
			int _useRoutes = AC_DefaultUseSubscribeRoutes;

			public ConnectionInfo (AnonymousRouter router, SubscribeInfo subscribeInfo, Key destKey, ushort connectionId, AnonymousConnectionType type, object payload, AsyncCallback callback, object state)
			{
				_initiator = true;
				_router = router;
				_subscribeInfo = subscribeInfo;
				_destKey = destKey;
				_connectionId = (uint)connectionId << 16;
				_type = type;
				_payload = payload;
				_estalibhDone = new ManualResetEvent (false);
				_ar = new EstablishRouteAsyncResult (this, callback, state);
				_sharedInfo = RNG.GetRNGBytes (DefaultSharedInfoSize);
				_sock = new ConnectionSocket (this);
				Setup ();
			}

			public ConnectionInfo (AnonymousRouter router, SubscribeInfo subscribeInfo, uint connectionId, byte[] sharedInfo, AnonymousConnectionType type,
				ECKeyPair publicKey, ConnectionEstablishPayload payload)
			{
				_initiator = false;
				_connected = true;
				_router = router;
				_subscribeInfo = subscribeInfo;
				_ar = null;
				_destKey = payload.InitiatorId;
				_connectionId = connectionId;
				_type = type;
				_msgAtEstablishing = payload.Payload;
				_key = ComputeSharedKey (subscribeInfo.PrivateKey, publicKey, payload.SharedInfo, sharedInfo);
				_otherSideTermEPs = payload.InitiatorSideTerminalEndPoints;
				_sock = new ConnectionSocket (this);
				_nextPingTime = DateTime.Now + AC_AliveCheckScheduleInterval;
				_expiry = DateTime.Now + AC_MaxMessageIntervalWithMargin;
				Setup ();
			}

			void Setup ()
			{
				if (_type == AnonymousConnectionType.HighThroughput)
					_useRoutes = 1;
			}

			public SymmetricKey ComputeSharedKey (byte[] sharedInfo)
			{
				return ComputeSharedKey (_connectionPrivate, _destKey.ToECPublicKey (), _sharedInfo, sharedInfo);
			}

			static SymmetricKey ComputeSharedKey (ECKeyPair privateKey, ECKeyPair publicKey, byte[] initiatorSharedInfo, byte[] otherSharedInfo)
			{
				byte[] sharedInfo = new byte[initiatorSharedInfo.Length + otherSharedInfo.Length];
				Buffer.BlockCopy (initiatorSharedInfo, 0, sharedInfo, 0, initiatorSharedInfo.Length);
				Buffer.BlockCopy (otherSharedInfo, 0, sharedInfo, initiatorSharedInfo.Length, otherSharedInfo.Length);
				return MultipleCipherHelper.ComputeSharedKey (privateKey, publicKey, sharedInfo, DefaultPaddingMode, true);				
			}

			public static byte[] ComputeMAC (SymmetricKey key, byte[] message)
			{
				using (HMACSHA1 hmac = new HMACSHA1 (key.Key)) {
					return hmac.ComputeHash (message);
				}
			}

			public static bool CheckMAC (SymmetricKey key, byte[] message, byte[] mac)
			{
				byte[] mac2 = ComputeMAC (key, message);
				if (mac.Length != mac2.Length)
					return false;
				for (int i = 0; i < mac.Length; i ++)
					if (mac[i] != mac2[i])
						return false;
				return true;
			}

			public void Established (SymmetricKey key, AnonymousEndPoint[] eps, object response)
			{
				_key = key;
				_otherSideTermEPs = eps;
				_connected = true;
				_msgAtEstablishing = response;
				lock (_dtLock) {
					_nextPingTime = DateTime.Now + AC_AliveCheckScheduleInterval;
					_expiry = DateTime.Now + AC_MaxMessageIntervalWithMargin;
				}
				_ar.Done ();
				_ar = null;
			}

			public void Start ()
			{
				_connectionPrivate = ECKeyPair.Create (_subscribeInfo.PrivateKey.DomainName);
				byte[] publicKey = _connectionPrivate.ExportPublicKey (true);
				ConnectionEstablishPayload payload = new ConnectionEstablishPayload (_subscribeInfo.Key,
					(ushort)(_connectionId >> 16), _subscribeInfo.GetRouteEndPoints (), _payload, _type, _sharedInfo);
				ECKeyPair destPub = _destKey.ToECPublicKey ();
				byte[] encrypted_payload = payload.Encrypt (_connectionPrivate, destPub);
				SymmetricKey tmpKey = MultipleCipherHelper.ComputeSharedKey (_subscribeInfo.PrivateKey, destPub, _sharedInfo, PaddingMode.None, false);
				byte[] mac = ComputeMAC (tmpKey, encrypted_payload);
				ConnectionEstablishMessage msg = new ConnectionEstablishMessage (_destKey, publicKey, GenerateDuplicationCheckValue (), mac, encrypted_payload);
				_subscribeInfo.SendMessage (msg, _useRoutesWhenEstablishing, true);
				lock (_dtLock) {
					_expiry = DateTime.Now + AC_EstablishTimeout;
				}
			}

			public void Start (byte[] sharedInfo, object response)
			{
				byte[] encrypted;
				if (response == null) {
					encrypted = new byte[0];
				} else {
					byte[] temp = DefaultSerializer.Serialize (response);
					encrypted = _key.Encrypt (temp, 0, temp.Length);
				}
				byte[] mac = ComputeMAC (_key, sharedInfo.Join (encrypted));
				ConnectionEstablishedMessage msg = new ConnectionEstablishedMessage (sharedInfo, encrypted, mac);
				Send (msg);
			}

			public void Send (object payload)
			{
				byte[] plain = DefaultSerializer.Serialize (payload);
				bool encrypt = true;
				if (payload is ConnectionEstablishedMessage)
					encrypt = false;
				Send (plain, 0, plain.Length, encrypt);
			}

			public void Send (byte[] data, int offset, int length)
			{
				Send (data, offset, length, true);
			}

			void Send (byte[] data, int offset, int length, bool encrypt)
			{
				lock (_dtLock) {
					if (_expiry == DateTime.MinValue)
						return;
					_nextPingTime = DateTime.Now + AC_AliveCheckScheduleInterval;
				}
				byte[] payload, mac;
				if (encrypt) {
					payload = _key.Encrypt (data, offset, length);
					mac = ComputeMAC (_key, payload);
				} else {
					payload = data;
					mac = null;
				}
				ConnectionSenderSideMessage msg = new ConnectionSenderSideMessage (_destKey,
					_subscribeInfo.GetRouteEndPoints ().RandomSelection (AC_DefaultUseSubscribeRoutes),
					_otherSideTermEPs, _connectionId, GenerateDuplicationCheckValue (), payload, mac);
				_subscribeInfo.SendMessage (msg, _useRoutes, !encrypt || _type == AnonymousConnectionType.LowLatency);
			}

			public void SendPing ()
			{
				Send (new byte[0], 0, 0, false);
			}

			public void Received (ConnectionReciverSideMessage msg)
			{
				if (_estalibhDone != null) _estalibhDone.WaitOne ();

				if (msg.Payload.Length > 0 && msg.PayloadMAC != null && !CheckMAC (_key, msg.Payload, msg.PayloadMAC)) {
					Logger.Log (LogLevel.Error, this, "MAC Error");
					return;
				}

				lock (_dtLock) {
					if (_expiry == DateTime.MinValue)
						return;
					_expiry = DateTime.Now + AC_MaxMessageIntervalWithMargin;
				}
				_otherSideTermEPs = msg.SenderSideTerminalEndPoints;
				if (msg.Payload.Length > 0) {
					byte[] plain = _key.Decrypt (msg.Payload, 0, msg.Payload.Length);
					_sock.InvokeReceivedEvent (new DatagramReceiveEventArgs (plain, plain.Length, DummyEndPoint));
				}
			}

			public IAsyncResult AsyncResult {
				get { return _ar; }
			}

			public bool IsConnected {
				get { return _connected; }
			}

			public bool IsInitiator {
				get { return _initiator; }
			}

			public uint ConnectionId {
				get { return _connectionId; }
				set { _connectionId = value;}
			}

			public IAnonymousSocket Socket {
				get { return _sock; }
			}

			public DateTime NextPingTime {
				get { return _nextPingTime; }
			}

			public bool IsExpiry {
				get { return _expiry <= DateTime.Now; }
			}

			public void Close ()
			{
				lock (_dtLock) {
					_expiry = DateTime.MinValue;
				}
				if (_connected) {
					_connected = false;
					_sock.Dispose_Internal ();
				} else if (_ar != null && !_ar.IsCompleted) {
					_ar.Done ();
				}
			}

			public class EstablishRouteAsyncResult : IAsyncResult
			{
				ConnectionInfo _owner;
				object _state;
				AsyncCallback _callback;
				bool _completed = false;
				ManualResetEvent _done;

				public EstablishRouteAsyncResult (ConnectionInfo owner, AsyncCallback callback, object state)
				{
					_owner = owner;
					_state = state;
					_callback = callback;
					_done = owner._estalibhDone;
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
				long _recvBytes = 0, _recvDgrams = 0, _sendBytes = 0, _sendDgrams = 0;
				Queue<DatagramReceiveEventArgs> _queue = new Queue<DatagramReceiveEventArgs> ();

				public ConnectionSocket (ConnectionInfo info)
				{
					_info = info;
				}

				public void InvokeReceivedEvent (DatagramReceiveEventArgs args)
				{
					bool enqueueMode = false;
					lock (this) {
						if (_queue != null)
							enqueueMode = true;
					}
					if (enqueueMode) {
						lock (_queue) {
							_queue.Enqueue (args);
						}
					} else if (Received != null) {
						try {
							Received (this, args);
						} catch {}
					}
				}

				public void Dispose_Internal ()
				{
					_info = null;
				}

				#region IAnonymousSocket Members

				public AnonymousConnectionType ConnectionType {
					get { return _info._type; }
				}

				public Key LocalEndPoint {
					get { return _info._subscribeInfo.Key; }
				}

				public Key RemoteEndPoint {
					get { return _info._destKey; }
				}

				public object PayloadAtEstablishing {
					get { return _info._msgAtEstablishing; }
				}

				public void InitializedEventHandlers ()
				{
					Queue<DatagramReceiveEventArgs> queue;
					lock (this) {
						queue = _queue;
						_queue = null;
					}
					if (queue == null || Received == null)
						return;
					while (queue.Count > 0) {
						try {
							Received (this, queue.Dequeue ());
						} catch {}
					}
				}

				#endregion

				#region IDatagramEventSocket Members

				public void Bind (EndPoint bindEP)
				{
				}

				public void Close ()
				{
					Dispose ();
				}

				public void SendTo (byte[] buffer, EndPoint remoteEP)
				{
					SendTo (buffer, 0, buffer.Length, remoteEP);
				}

				public void SendTo (byte[] buffer, int offset, int size, EndPoint remoteEP)
				{
					if (_info == null)
						throw new ObjectDisposedException (this.GetType ().Name);
					Interlocked.Increment (ref _sendDgrams);
					Interlocked.Add (ref _sendBytes, size);
					_info.Send (buffer, offset, size);
				}

				public event DatagramReceiveEventHandler Received;

				public int MaxDatagramSize {
					/// TODO: 正しい値を返すようにする
					get { return 500; }
				}

				public long ReceivedBytes {
					get { return Interlocked.Read (ref _recvBytes); }
				}

				public long SentBytes {
					get { return Interlocked.Read (ref _sendBytes); }
				}

				public long ReceivedDatagrams {
					get { return Interlocked.Read (ref _recvDgrams); }
				}

				public long SentDatagrams {
					get { return Interlocked.Read (ref _sendDgrams); }
				}

				#endregion

				#region IDisposable Members

				public void Dispose ()
				{
					if (_info != null) {
						_info.Close ();
						Dispose_Internal ();
					}
				}

				#endregion
			}
		}
		#endregion

		#region MultipleCipherHelper
		static class MultipleCipherHelper
		{
			static byte[] Copy (byte[] src, int offset, int size)
			{
				byte[] tmp = new byte[size];
				Buffer.BlockCopy (src, offset, tmp, 0, size);
				return tmp;
			}

			public static SymmetricKey ComputeSharedKey (ECKeyPair privateKey, ECKeyPair pubKey, byte[] sharedInfo, PaddingMode padding, bool enableIVShuffle)
			{
				byte[] iv = new byte[DefaultSymmetricBlockBytes];
				byte[] key = new byte[DefaultSymmetricKeyBytes];
				ECDiffieHellman ecdh = new ECDiffieHellman (privateKey);
				if (sharedInfo != null && sharedInfo.Length > 0)
					ecdh.SharedInfo = sharedInfo;
				byte[] sharedKey = ecdh.PerformKeyAgreement (pubKey, iv.Length + key.Length);
				Buffer.BlockCopy (sharedKey, 0, iv, 0, iv.Length);
				Buffer.BlockCopy (sharedKey, iv.Length, key, 0, key.Length);
				return new SymmetricKey (DefaultSymmetricAlgorithmType, iv, key, DefaultCipherMode, padding, enableIVShuffle);
			}

			public static byte[] CreateEstablishPayload (NodeHandle[] relayNodes, SymmetricKey[] relayKeys, Key recipientId, ECDomainNames domain, int fixedPayloadSize)
			{
				byte[] payload = new byte[fixedPayloadSize];
				byte[] iv = new byte[DefaultSymmetricBlockBytes];
				byte[] key = new byte[DefaultSymmetricKeyBytes];
				int payload_size = 0;

				RNG.Instance.GetBytes (payload);

				// 終端ノードに配送するデータを設定
				payload[0] = 0;
				payload[1] = (byte)(recipientId.KeyBytes >> 8);
				payload[2] = (byte)(recipientId.KeyBytes & 0xFF);
				recipientId.CopyTo (payload, 3);
				payload_size = 3 + recipientId.KeyBytes;

				for (int i = relayKeys.Length - 1; i >= 0; i--) {
					// PaddingMode==Noneなのでペイロードサイズをivバイトの整数倍に合わせる
					if (payload_size % iv.Length != 0)
						payload_size += iv.Length - (payload_size % iv.Length);

					// Ephemeralキーと中継ノードの公開鍵とで鍵共有
					ECKeyPair privateKey = ECKeyPair.Create (domain);
					byte[] pubKey = privateKey.ExportPublicKey (true);
					relayKeys[i] = ComputeSharedKey (privateKey, relayNodes[i].NodeID.ToECPublicKey (), null, PaddingMode.None, false);
					byte[] cipher = relayKeys[i].Encrypt (payload, 0, payload_size);
					if (i == 0) {
						Buffer.BlockCopy (pubKey, 0, payload, 0, pubKey.Length);
						Buffer.BlockCopy (cipher, 0, payload, pubKey.Length, cipher.Length);
					} else {
						payload[0] = 1;
						byte[] nextHop = Serializer.Instance.Serialize (relayNodes[i].EndPoint);
						Buffer.BlockCopy (nextHop, 0, payload, 1, nextHop.Length);
						Buffer.BlockCopy (pubKey, 0, payload, 1 + nextHop.Length, pubKey.Length);
						Buffer.BlockCopy (cipher, 0, payload, 1 + nextHop.Length + pubKey.Length, cipher.Length);
						payload_size = 1 + nextHop.Length + pubKey.Length + cipher.Length;
					}
				}
				return payload;
			}

			public static EstablishRoutePayload DecryptEstablishPayload (byte[] payload, ECKeyPair privateNodeKey, int pubKeyLen, int fixedPayloadSize)
			{
				ECKeyPair ephemeralKey = ECKeyPairExtensions.CreatePublic (Copy (payload, 0, pubKeyLen));
				SymmetricKey sk = ComputeSharedKey (privateNodeKey, ephemeralKey, null, PaddingMode.None, false);
				
				int len = payload.Length - pubKeyLen;
				if (len % sk.IV.Length != 0) len -= len % sk.IV.Length;
				byte[] decrypted = sk.Decrypt (payload, pubKeyLen, len);

				switch (decrypted[0]) {
					case 0:
						int payload_size = (decrypted[1] << 8) | decrypted[2];
						if (payload_size > decrypted.Length - 2)
							throw new FormatException ();
						payload = new byte[payload_size];
						Buffer.BlockCopy (decrypted, 3, payload, 0, payload.Length);
						return new EstablishRoutePayload (sk, payload);
					case 1:
						payload = new byte[fixedPayloadSize];
						EndPoint nextHop;
						RNG.Instance.GetBytes (payload);
						using (MemoryStream ms = new MemoryStream (decrypted, 1, decrypted.Length - 1)) {
							nextHop = (EndPoint)Serializer.Instance.Deserialize (ms);
							Buffer.BlockCopy (decrypted, 1 + (int)ms.Position, payload, 0, decrypted.Length - 1 - (int)ms.Position);
						}
						return new EstablishRoutePayload (sk, nextHop, payload);
					default:
						throw new FormatException ();
				}
			}

			public static byte[] CreateRoutedPayload (SymmetricKey key, object msg, int fixedPayloadSize)
			{
				return CreateRoutedPayload (key, DefaultSerializer.Serialize (msg), fixedPayloadSize);
			}

			public static byte[] CreateRoutedPayload (SymmetricKey[] keys, object msg, int fixedPayloadSize)
			{
				return CreateRoutedPayload (keys, DefaultSerializer.Serialize (msg), fixedPayloadSize);
			}

			static byte[] CreateRoutedPayload (SymmetricKey[] keys, byte[] data, int fixedPayloadSize)
			{
				byte[] payload = CreateRoutedPayload (keys[keys.Length - 1], data, fixedPayloadSize);
				for (int i = keys.Length - 2; i >= 0; i --)
					payload = keys[i].Encrypt (payload, 0, payload.Length);
				return payload;
			}

			static byte[] CreateRoutedPayload (SymmetricKey key, byte[] data, int fixedPayloadSize)
			{
				int header_size = key.IV.Length; // 簡易IVシャッフル
				int payload_size = data.Length + header_size;
				byte[] payload = new byte[fixedPayloadSize];

				RNG.Instance.GetBytes (payload);
				payload[0] = (byte)((data.Length >> 8) & 0xff);
				payload[1] = (byte)(data.Length & 0xff);
				Buffer.BlockCopy (data, 0, payload, header_size, data.Length);
				if (payload_size % key.IV.Length != 0)
					payload_size += key.IV.Length - (payload_size % key.IV.Length);

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

			public static object DecryptRoutedPayloadAtTerminal (SymmetricKey key, byte[] payload)
			{
				payload = key.Decrypt (payload, 0, payload.Length);
				int offset = key.IV.Length;
				int size = (payload[0] << 8) | payload[1];
				using (MemoryStream ms = new MemoryStream (payload, offset, size)) {
					return DefaultSerializer.Deserialize (ms);
				}
			}

			public static byte[] EncryptRoutedPayload (SymmetricKey key, byte[] payload)
			{
				return key.Encrypt (payload, 0, payload.Length);
			}

			public static object DecryptRoutedPayload (SymmetricKey[] keys, byte[] payload)
			{
				for (int i = 0; i < keys.Length - 1; i++) {
					byte[] decrypted = keys[i].Decrypt (payload, 0, payload.Length);
					if (payload.Length != decrypted.Length)
						throw new ApplicationException ();
					payload = decrypted;
				}
				return DecryptRoutedPayloadAtTerminal (keys[keys.Length - 1], payload);
			}
		}
		#endregion

		#region Messages
		[SerializableTypeId (0x210)]
		class EstablishRouteMessage
		{
			[SerializableFieldId (0)]
			RouteLabel _label;

			[SerializableFieldId (1)]
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

		[SerializableTypeId (0x211)]
		class EstablishedRouteMessage
		{
			[SerializableFieldId (0)]
			RouteLabel _label;

			public EstablishedRouteMessage (RouteLabel label)
			{
				_label = label;
			}

			public RouteLabel Label {
				get { return _label; }
			}
		}

		[SerializableTypeId (0x212)]
		class RoutedMessage
		{
			[SerializableFieldId (0)]
			RouteLabel _label;

			[SerializableFieldId (1)]
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

		[SerializableTypeId (0x213)]
		class ConnectionEstablishMessage : ICheckDuplication
		{
			[SerializableFieldId (0)]
			Key _destId;

			[SerializableFieldId (1)]
			byte[] _ephemeralPublicKey;

			[SerializableFieldId (2)]
			DupCheckLabel _dupCheckValue;

			[SerializableFieldId (3)]
			byte[] _mac;

			[SerializableFieldId (4)]
			byte[] _encrypted;

			public ConnectionEstablishMessage (Key destId, byte[] pubKey, DupCheckLabel dupCheckValue, byte[] mac, byte[] encrypted)
			{
				_destId = destId;
				_ephemeralPublicKey = pubKey;
				_dupCheckValue = dupCheckValue;
				_mac = mac;
				_encrypted = encrypted;
			}

			public Key DestinationId {
				get { return _destId; }
			}

			public byte[] EphemeralPublicKey {
				get { return _ephemeralPublicKey; }
			}

			public DupCheckLabel DuplicationCheckValue {
				get { return _dupCheckValue; }
			}

			public byte[] MAC {
				get { return _mac; }
			}

			public byte[] Encrypted {
				get { return _encrypted; }
			}
		}

		[SerializableTypeId (0x21a)]
		class ConnectionEstablishedMessage
		{
			[SerializableFieldId (0)]
			byte[] _sharedInfo;

			[SerializableFieldId (1)]
			byte[] _encrypted;

			[SerializableFieldId (2)]
			byte[] _mac;

			public ConnectionEstablishedMessage (byte[] sharedInfo, byte[] encrypted, byte[] mac)
			{
				_sharedInfo = sharedInfo;
				_encrypted = encrypted;
				_mac = mac;
			}

			public byte[] SharedInfo {
				get { return _sharedInfo; }
			}

			public byte[] Encrypted {
				get { return _encrypted; }
			}

			public byte[] MAC {
				get { return _mac; }
			}
		}

		[SerializableTypeId (0x214)]
		class ConnectionSenderSideMessage
		{
			[SerializableFieldId (0)]
			Key _dest;

			[SerializableFieldId (1)]
			AnonymousEndPoint[] _senderSideTermEPs;

			[SerializableFieldId (2)]
			AnonymousEndPoint[] _receiverSideTermEPs;

			[SerializableFieldId (3)]
			ConnectionLabel _connectionId;

			[SerializableFieldId (4)]
			DupCheckLabel _dupCheckValue;

			[SerializableFieldId (5)]
			byte[] _payload_mac;

			[SerializableFieldId (6)]
			byte[] _payload;

			public ConnectionSenderSideMessage (Key dest, AnonymousEndPoint[] senderSide, AnonymousEndPoint[] reciverSide, ConnectionLabel connectionId, DupCheckLabel dupCheckValue, byte[] payload, byte[] mac)
			{
				_dest = dest;
				_senderSideTermEPs = senderSide;
				_receiverSideTermEPs = reciverSide;
				_connectionId = connectionId;
				_dupCheckValue = dupCheckValue;
				_payload = payload;
				_payload_mac = mac;
			}

			public Key Destination {
				get { return _dest; }
			}

			public AnonymousEndPoint[] SenderSideTerminalEndPoints {
				get { return _senderSideTermEPs; }
			}

			public AnonymousEndPoint[] ReciverSideTerminalEndPoints {
				get { return _receiverSideTermEPs; }
			}

			public ConnectionLabel ConnectionId {
				get { return _connectionId; }
			}

			public DupCheckLabel DuplicationCheckValue {
				get { return _dupCheckValue; }
			}

			public byte[] Payload {
				get { return _payload; }
			}

			public byte[] PayloadMAC {
				get { return _payload_mac; }
			}
		}

		[SerializableTypeId (0x215)]
		class ConnectionReciverSideMessage : ICheckDuplication
		{
			[SerializableFieldId (0)]
			AnonymousEndPoint[] _senderSideTermEPs;

			[SerializableFieldId (1)]
			ConnectionLabel _connectionId;

			[SerializableFieldId (2)]
			DupCheckLabel _dupCheckValue;

			[SerializableFieldId (3)]
			byte[] _payload_mac;

			[SerializableFieldId (4)]
			byte[] _payload;

			public ConnectionReciverSideMessage (AnonymousEndPoint[] senderSide, ConnectionLabel connectionId, DupCheckLabel dupCheckValue, byte[] payload, byte[] mac)
			{
				_senderSideTermEPs = senderSide;
				_connectionId = connectionId;
				_dupCheckValue = dupCheckValue;
				_payload = payload;
				_payload_mac = mac;
			}

			public AnonymousEndPoint[] SenderSideTerminalEndPoints {
				get { return _senderSideTermEPs; }
			}

			public ConnectionLabel ConnectionId {
				get { return _connectionId; }
			}

			public DupCheckLabel DuplicationCheckValue {
				get { return _dupCheckValue; }
			}

			public byte[] Payload {
				get { return _payload; }
			}

			public byte[] PayloadMAC {
				get { return _payload_mac; }
			}
		}

		[SerializableTypeId (0x216)]
		class DisconnectMessage
		{
			RouteLabel _label;

			public DisconnectMessage (RouteLabel label)
			{
				_label = label;
			}

			public RouteLabel Label {
				get { return _label; }
			}
		}

		[SerializableTypeId (0x217)]
		class Ping
		{
			static Ping _instance = new Ping ();
			Ping () {}
			public static Ping Instance {
				get { return _instance; }
			}
		}

		[SerializableTypeId (0x219)]
		class InterterminalMessage
		{
			[SerializableFieldId (0)]
			RouteLabel _label;

			[SerializableFieldId (1)]
			object _msg;

			public InterterminalMessage (RouteLabel label, object msg)
			{
				_label = label;
				_msg = msg;
			}

			public RouteLabel Label {
				get { return _label; }
			}

			public object Message {
				get { return _msg; }
			}
		}

		[SerializableTypeId (0x21b)]
		class InsideMessage
		{
			[SerializableFieldId (0)]
			uint _id;

			[SerializableFieldId (1)]
			object _payload;

			public InsideMessage (uint id, object payload)
			{
				_id = id;
				_payload = payload;
			}

			public uint ID {
				get { return _id; }
			}

			public object Payload {
				get { return _payload; }
			}
		}

		interface ICheckDuplication
		{
			DupCheckLabel DuplicationCheckValue { get; }
		}
		#endregion

		#region Structures
		class EstablishRoutePayload
		{
			SymmetricKey _key;
			EndPoint _nextHopNode;
			byte[] _payload;
			bool _isLast;

			public EstablishRoutePayload (SymmetricKey key, EndPoint nextHop, byte[] payload)
			{
				_key = key;
				_nextHopNode = nextHop;
				_payload = payload;
				_isLast = false;
			}

			public EstablishRoutePayload (SymmetricKey key, byte[] payload)
			{
				_key = key;
				_nextHopNode = null;
				_payload = payload;
				_isLast = true;
			}

			public SymmetricKey Key {
				get { return _key; }
			}

			public EndPoint NextHopNode {
				get { return _nextHopNode; }
			}

			public byte[] Payload {
				get { return _payload; }
			}

			public bool IsLast {
				get { return _isLast; }
			}
		}

		[SerializableTypeId (0x218)]
		class ConnectionEstablishPayload
		{
			[SerializableFieldId (0)]
			byte[] _sharedInfo;

			[SerializableFieldId (1)]
			Key _initiatorId;

			[SerializableFieldId (2)]
			ConnectionHalfLabel _connectionId;

			[SerializableFieldId (3)]
			AnonymousEndPoint[] _initiatorSideTermEPs;

			[SerializableFieldId (4)]
			object _payload;

			[SerializableFieldId (5)]
			AnonymousConnectionType _type;

			public ConnectionEstablishPayload (Key initiator, ConnectionHalfLabel connectionId, AnonymousEndPoint[] eps, object payload, AnonymousConnectionType type, byte[] sharedInfo)
			{
				_initiatorId = initiator;
				_connectionId = connectionId;
				_initiatorSideTermEPs = eps;
				_payload = payload;
				_type = type;
				_sharedInfo = sharedInfo;
			}

			public static ConnectionEstablishPayload Decrypt (ECKeyPair privateKey, ECKeyPair publicKey, byte[] encrypted_payload)
			{
				SymmetricKey sk = MultipleCipherHelper.ComputeSharedKey (privateKey, publicKey, null, DefaultPaddingMode, false);
				byte[] plain = sk.Decrypt (encrypted_payload, 0, encrypted_payload.Length);
				return (ConnectionEstablishPayload)DefaultSerializer.Deserialize (plain);
			}

			public byte[] Encrypt (ECKeyPair privateKey, ECKeyPair publicKey)
			{
				SymmetricKey sk = MultipleCipherHelper.ComputeSharedKey (privateKey, publicKey, null, DefaultPaddingMode, false);
				byte[] plain = DefaultSerializer.Serialize (this);
				return sk.Encrypt (plain, 0, plain.Length);
			}

			public byte[] SharedInfo {
				get { return _sharedInfo; }
			}

			public Key InitiatorId {
				get { return _initiatorId; }
			}

			public ConnectionHalfLabel ConnectionId {
				get { return _connectionId; }
			}

			public AnonymousEndPoint[] InitiatorSideTerminalEndPoints {
				get { return _initiatorSideTermEPs; }
			}

			public object Payload {
				get { return _payload; }
			}

			public AnonymousConnectionType ConnectionType {
				get { return _type; }
			}
		}

		[Serializable]
		[SerializableTypeId (0x20a)]
		public class AnonymousEndPoint : IEquatable<AnonymousEndPoint>
		{
			[SerializableFieldId (0)]
			EndPoint _ep;

			[SerializableFieldId (1)]
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

			public override bool Equals (object obj)
			{
				AnonymousEndPoint other = obj as AnonymousEndPoint;
				if (other == null)
					return false;
				return Equals (other);
			}

			public override int GetHashCode ()
			{
				return _ep.GetHashCode () ^ _label.GetHashCode ();
			}

			public override string ToString ()
			{
				return _ep.ToString () + "#" + _label.ToString ("x");
			}

			public bool Equals (AnonymousEndPoint other)
			{
				return this._ep.Equals (other._ep) && (this._label == other._label);
			}
		}

		[Serializable]
		[SerializableTypeId (0x20b)]
		public class DHTEntry : IPutterEndPointStore, IEquatable<DHTEntry>
		{
			[SerializableFieldId (0)]
			RouteLabel _label;

			[SerializableFieldId (1)]
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
				return (_ep == null ? "localhost" : _ep.ToString ()) + "#" + _label.ToString ("x");
			}

			public bool Equals (DHTEntry other)
			{
				if (_label != other._label)
					return false;
				if (_ep == null)
					return other._ep == null;
				return _ep.Equals (other._ep);
			}

			static ILocalHashTableValueMerger _mergerInstance = new LocalHashTableValueMerger<DHTEntry> ();
			public static ILocalHashTableValueMerger Merger {
				get { return _mergerInstance; }
			}
		}
		#endregion
	}
}