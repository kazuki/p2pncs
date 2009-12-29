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
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using openCrypto;
using openCrypto.EllipticCurve;
using p2pncs.Security.Cryptography;
using p2pncs.Threading;
using p2pncs.Utility;
using ECDiffieHellman = openCrypto.EllipticCurve.KeyAgreement.ECDiffieHellman;
using RouteLabel = System.UInt32;

namespace p2pncs.Net.Overlay.Anonymous
{
	public class MCRManager : IDisposable
	{
		IInquirySocket _sock;
		ECKeyPair _privateKeyPair;
		int _pubKeySize;
		IntervalInterrupter _int;
		HashSet<IRouteInfo> _timeoutCheckList = new HashSet<IRouteInfo> ();
		Dictionary<MCREndPoint, IRouteInfo> _routes = new Dictionary<MCREndPoint,IRouteInfo> ();
		Dictionary<RouteLabel, TerminalRouteInfo> _terms = new Dictionary<uint,TerminalRouteInfo> ();
		EventHandlers<Type, MCRTerminalNodeReceivedEventArgs> _received = new EventHandlers<Type, MCRTerminalNodeReceivedEventArgs> ();
		internal readonly static SymmetricKeyOption DefaultSymmetricKeyOption = new SymmetricKeyOption ();
		internal const int FixedMessageSize = 512; /// TODO:
		internal const int AntiReplayWindowSize = 128;
		internal const int DuplicationCheckSize = 128;
		internal static readonly TimeSpan PingInterval = TimeSpan.FromMinutes (1);
		internal static readonly TimeSpan MaxPingInterval = PingInterval + PingInterval;
		internal static readonly string ACK = "ACK";
		internal static readonly string FAILED = "FAILED";

		public event EventHandler<FailedEventArgs> InquiryFailed;

		public MCRManager (IInquirySocket sock, ECKeyPair keyPair, IntervalInterrupter timeoutCheckInt)
		{
			_sock = sock;
			_privateKeyPair = keyPair;
			_pubKeySize = keyPair.ExportPublicKey (true).Length;
			_int = timeoutCheckInt;

			_sock.Inquired.Add (typeof (EstablishRouteMessage), EstablishRouteMessage_Inquired);
			_sock.Inquired.Add (typeof (RoutedMessage), RoutedMessage_Inquired);
			_sock.Inquired.Add (typeof (InterTerminalMessage), InterTerminalMessage_Inquired);
			_sock.Inquired.Add (typeof (DisconnectMessage), DisconnectMessage_Inquired);
			_sock.Received.Add (typeof (RoutedMessage), RoutedMessage_Received);
			_sock.Received.Add (typeof (InterTerminalMessage), InterTerminalMessage_Received);

			_int.AddInterruption (CheckTimeout);
		}

		#region Timeout Check
		void CheckTimeout ()
		{
			List<IRouteInfo> list;
			lock (_routes) {
				list = new List<IRouteInfo> (_timeoutCheckList);
			}
			for (int i = 0; i < list.Count; i ++)
				list[i].CheckTimeout ();
		}
		#endregion

		#region Socket Received/Inquired Handlers
		void EstablishRouteMessage_Inquired (object sender, InquiredEventArgs e)
		{
			EstablishRouteMessage msg = (EstablishRouteMessage)e.InquireMessage;
			SymmetricKey key = null;
			object payload = null;
			EndPoint nextHop = null;
			byte[] nextPayload = null;
			try {
				nextPayload = CipherUtility.DecryptEstablishMessageData (_privateKeyPair, msg.Encrypted,
					_pubKeySize, DefaultSymmetricKeyOption, out key, out nextHop, out payload);
			} catch { nextPayload = null; payload = null; }
			if (nextPayload == null && payload == null) {
				_sock.RespondToInquiry (e, FAILED); // decrypt failed
				return;
			}
			_sock.RespondToInquiry (e, ACK);

			MCREndPoint prevEP = new MCREndPoint (e.EndPoint, msg.Label);
			if (nextPayload != null) {
				// relay
				MCREndPoint nextEP;
				RelayRouteInfo info;
				lock (_routes) {
					if (_routes.ContainsKey (prevEP))
						return; // 既にルート情報が存在する
					while (true) {
						nextEP = new MCREndPoint (nextHop, GenerateRouteLabel ());
						if (_routes.ContainsKey (nextEP))
							continue;
						info = new RelayRouteInfo (this, key, prevEP, nextEP);
						_routes.Add (prevEP, info);
						_routes.Add (nextEP, info);
						_timeoutCheckList.Add (info);
						break;
					}
				}
				msg = new EstablishRouteMessage (nextEP.Label, nextPayload);
				_sock.BeginInquire (msg, nextEP.EndPoint, delegate (IAsyncResult ar) {
					object res = _sock.EndInquire (ar);
					if (ACK.Equals (res))
						return;
					info.Close ();
					if (res == null)
						RaiseInquiryFailedEvent (nextEP.EndPoint);
				}, null);
			} else {
				// terminal
				RouteLabel lbl;
				TerminalRouteInfo info = new TerminalRouteInfo (this, key, prevEP);
				lock (_routes) {
					if (_routes.ContainsKey (prevEP))
						return;
					_routes.Add (prevEP, info);
					_timeoutCheckList.Add (info);
				}
				lock (_terms) {
					while (true) {
						lbl = GenerateRouteLabel ();
						if (_terms.ContainsKey (lbl))
							continue;
						info.Label = lbl;
						_terms.Add (lbl, info);
						break;
					}
				}
				info.Send (new EstablishedRouteMessage (lbl, "WORLD"), true);
			}
		}

		void RoutedMessage_Inquired (object sender, InquiredEventArgs e)
		{
			RoutedMessage msg = (RoutedMessage)e.InquireMessage;
			MCREndPoint ep = new MCREndPoint (e.EndPoint, msg.Label);
			RoutedMessage_Handler (msg, ep, e);
		}

		void RoutedMessage_Received (object sender, ReceivedEventArgs e)
		{
			RoutedMessage msg = (RoutedMessage)e.Message;
			MCREndPoint ep = new MCREndPoint (e.RemoteEndPoint, msg.Label);
			RoutedMessage_Handler (msg, ep, null);
		}

		void RoutedMessage_Handler (RoutedMessage msg, MCREndPoint ep, InquiredEventArgs e)
		{
			IRouteInfo routeInfo;
			lock (_routes) {
				_routes.TryGetValue (ep, out routeInfo);
			}
			if (routeInfo == null) {
				if (e != null)
					_sock.RespondToInquiry (e, FAILED);
				return;
			}
			if (e != null)
				_sock.RespondToInquiry (e, ACK);
			routeInfo.Received (_sock, ep, msg, e != null);
		}

		void DisconnectMessage_Inquired (object sender, InquiredEventArgs e)
		{
			DisconnectMessage msg = (DisconnectMessage)e.InquireMessage;
			MCREndPoint ep = new MCREndPoint (e.EndPoint, msg.Label);
			IRouteInfo routeInfo;
			lock (_routes) {
				_routes.TryGetValue (ep, out routeInfo);
			}
			_sock.RespondToInquiry (e, ACK);
			if (routeInfo == null)
				return;

			if (routeInfo is RelayRouteInfo) {
				(routeInfo as RelayRouteInfo).RelayDisconnectMessage (_sock, ep);
			} else {
				routeInfo.Close ();
			}
		}

		void InterTerminalMessage_Inquired (object sender, InquiredEventArgs e)
		{
			InterTerminalMessage msg = (InterTerminalMessage)e.InquireMessage;
			MCREndPoint ep = new MCREndPoint (e.EndPoint, msg.Label);
			InterTerminalMessage_Handler (msg, ep, e);
		}

		void InterTerminalMessage_Received (object sender, ReceivedEventArgs e)
		{
			InterTerminalMessage msg = (InterTerminalMessage)e.Message;
			MCREndPoint ep = new MCREndPoint (e.RemoteEndPoint, msg.Label);
			InterTerminalMessage_Handler (msg, ep, null);
		}

		void InterTerminalMessage_Handler (InterTerminalMessage msg, MCREndPoint ep, InquiredEventArgs e)
		{
			TerminalRouteInfo routeInfo;
			lock (_terms) {
				_terms.TryGetValue (msg.Label, out routeInfo);
			}
			if (routeInfo == null) {
				if (e != null)
					_sock.RespondToInquiry (e, FAILED);
				return;
			}
			if (e != null)
				_sock.RespondToInquiry (e, ACK);

			if (!routeInfo.DuplicationChecker.Check (msg.ID)) {
				Console.WriteLine ("Terminal Drop#2 id={0}", msg.ID);
				return;
			}

			InterTerminalPayload itp = new InterTerminalPayload (msg.SrcEndPoints, msg.Payload, msg.ID);
			routeInfo.Send (itp, e != null);
		}
		#endregion

		#region IDisposable Members

		public void Dispose ()
		{
			lock (this) {
				if (_privateKeyPair == null)
					return;
				_privateKeyPair = null;
			}

			_timeoutCheckList.Clear ();
			_received.Clear ();
			_routes.Clear ();
			_terms.Clear ();
			_int.RemoveInterruption (CheckTimeout);
			_sock.Inquired.Remove (typeof (EstablishRouteMessage), EstablishRouteMessage_Inquired);
			_sock.Inquired.Remove (typeof (RoutedMessage), RoutedMessage_Inquired);
			_sock.Inquired.Remove (typeof (InterTerminalMessage), InterTerminalMessage_Inquired);
			_sock.Inquired.Remove (typeof (DisconnectMessage), DisconnectMessage_Inquired);
			_sock.Received.Remove (typeof (RoutedMessage), RoutedMessage_Received);
			_sock.Received.Remove (typeof (InterTerminalMessage), InterTerminalMessage_Received);
		}

		#endregion

		#region Misc
		internal bool AddRouteInfo (MCREndPoint ep, IRouteInfo info)
		{
			lock (_routes) {
				if (_routes.ContainsKey (ep))
					return false;
				_routes.Add (ep, info);
			}
			return true;
		}

		internal void RemoveRouteInfo (MCREndPoint ep)
		{
			lock (_routes) {
				_routes.Remove (ep);
			}
		}

		internal static RouteLabel GenerateRouteLabel ()
		{
			return ThreadSafeRandom.NextUInt32 ();
		}

		void RaiseReceivedEvent (Type type, TerminalNodeReceivedEventArgs e)
		{
			try {
				_received.Invoke (type, this, e);
			} catch {}
		}

		internal void RaiseInquiryFailedEvent (EndPoint ep)
		{
			if (InquiryFailed == null)
				return;
			try {
				InquiryFailed (this, new FailedEventArgs (ep));
			} catch {}
		}
		#endregion

		#region Properties
		internal IInquirySocket Socket {
			get { return _sock; }
		}

		public ECKeyPair PrivateKey {
			get { return _privateKeyPair; }
		}

		internal IntervalInterrupter TimeoutCheckInterrupter {
			get { return _int; }
		}

		public EventHandlers<Type, MCRTerminalNodeReceivedEventArgs> Received {
			get { return _received; }
		}
		#endregion

		#region Internal Class
		internal interface IRouteInfo
		{
			void Received (IInquirySocket sock, MCREndPoint ep, RoutedMessage msg, bool isReliableMode);
			void CheckTimeout ();
			void Close ();
		}
		sealed class RelayRouteInfo : IRouteInfo
		{
			MCRManager _mgr;
			SymmetricKey _key;
			MCREndPoint _prev;
			MCREndPoint _next;
			DateTime _expiryFromPrev = DateTime.Now + MaxPingInterval;
			DateTime _expiryFromNext = DateTime.Now + MaxPingInterval;

			public RelayRouteInfo (MCRManager mgr, SymmetricKey key, MCREndPoint prev, MCREndPoint next)
			{
				_mgr = mgr;
				_key = key;
				_prev = prev;
				_next = next;
			}

			public void RelayDisconnectMessage (IInquirySocket sock, MCREndPoint src)
			{
				if (!src.Equals (_next))
					return;
				sock.BeginInquire (new DisconnectMessage (_prev.Label), _prev.EndPoint, null, null);
			}

			#region IRouteInfo Members

			public void Received (IInquirySocket sock, MCREndPoint ep, RoutedMessage msg, bool isReliableMode)
			{
				byte[] new_payload;
				MCREndPoint next;
				if (ep.Equals (_prev)) {
					next = _next;
					new_payload = _key.Decrypt (msg.Payload, 0, msg.Payload.Length);
					_expiryFromPrev = DateTime.Now + MaxPingInterval;
				} else {
					next = _prev;
					new_payload = _key.Encrypt (msg.Payload, 0, msg.Payload.Length);
					_expiryFromNext = DateTime.Now + MaxPingInterval;
				}
				RoutedMessage new_msg = new RoutedMessage (next.Label, new_payload);
				if (isReliableMode) {
					sock.BeginInquire (new_msg, next.EndPoint, delegate (IAsyncResult ar) {
						object res = sock.EndInquire (ar);
						if (ACK.Equals (res))
							return;
						if (ep.Equals (_prev))
							sock.BeginInquire (new DisconnectMessage (_prev.Label), _prev.EndPoint, null, null);
						Close ();
						if (res == null)
							_mgr.RaiseInquiryFailedEvent (next.EndPoint);
					}, null);
				} else {
					sock.SendTo (new_msg, next.EndPoint);
				}
			}

			public void CheckTimeout ()
			{
				if (_expiryFromNext < DateTime.Now || _expiryFromPrev < DateTime.Now) {
					Close ();
					Console.WriteLine ("Relay: Timeout...");
				}
			}

			public void Close ()
			{
				lock (_mgr._routes) {
					_mgr._routes.Remove (_prev);
					_mgr._routes.Remove (_next);
					_mgr._timeoutCheckList.Remove (this);
				}
			}

			#endregion
		}
		sealed class TerminalRouteInfo : IRouteInfo
		{
			MCRManager _mgr;
			int _seq = 0;
			SymmetricKey _key;
			MCREndPoint _prev;
			DateTime _nextPingTime = DateTime.Now + PingInterval;
			DateTime _pingRecvExpire = DateTime.Now + MaxPingInterval;
			AntiReplayWindow _antiReplay = new AntiReplayWindow (AntiReplayWindowSize);
			DuplicationChecker<ulong> _dupChecker = new DuplicationChecker<ulong> (DuplicationCheckSize);

			public TerminalRouteInfo (MCRManager mgr, SymmetricKey key, MCREndPoint prev)
			{
				_mgr = mgr;
				_key = key;
				_prev = prev;
			}

			public void Send (object msg, bool isReliableMode)
			{
				IInquirySocket sock = _mgr._sock;
				uint seq = (uint)Interlocked.Increment (ref _seq);
				byte[] payload = CipherUtility.CreateRoutedPayload (_key, seq, msg, FixedMessageSize);
				RoutedMessage routedMsg = new RoutedMessage (_prev.Label, payload);
				_nextPingTime = DateTime.Now + PingInterval;
				if (isReliableMode) {
					sock.BeginInquire (routedMsg, _prev.EndPoint, delegate (IAsyncResult ar) {
						object res = sock.EndInquire (ar);
						if (ACK.Equals (res))
							return;
						Close ();
						if (res == null)
							_mgr.RaiseInquiryFailedEvent (_prev.EndPoint);
					}, null);
				} else {
					sock.SendTo (routedMsg, _prev.EndPoint);
				}
			}

			public RouteLabel Label { get; set; }

			public DuplicationChecker<ulong> DuplicationChecker {
				get { return _dupChecker; }
			}

			#region IRouteInfo Members

			public void Received (IInquirySocket sock, MCREndPoint ep, RoutedMessage msg, bool isReliableMode)
			{
				uint seq;
				object payload = CipherUtility.DecryptRoutedPayload (_key, out seq, msg.Payload);
				_pingRecvExpire = DateTime.Now + MaxPingInterval;
				if (!_antiReplay.Check (seq)) {
					Console.WriteLine ("Terminal Drop seq={0}", seq);
					return;
				}

				InterTerminalRequestMessage interTermReqMsg = payload as InterTerminalRequestMessage;
				if (interTermReqMsg == null) {
					TerminalNodeReceivedEventArgs args = new TerminalNodeReceivedEventArgs (this, payload);
					_mgr.RaiseReceivedEvent (payload.GetType (), args);
				} else {
					if (!_dupChecker.Check (interTermReqMsg.ID)) {
						Console.WriteLine ("Terminal Drop#1 id={0}", interTermReqMsg.ID);
						return;
					}
					for (int i = 0; i < interTermReqMsg.DestEndPoints.Length; i ++) {
						MCREndPoint mcrEp = interTermReqMsg.DestEndPoints[i];
						InterTerminalMessage interTermMsg = new InterTerminalMessage (mcrEp.Label, interTermReqMsg.SrcEndPoints, interTermReqMsg.Payload, interTermReqMsg.ID);
						if (isReliableMode) {
							sock.BeginInquire (interTermMsg, mcrEp.EndPoint, delegate (IAsyncResult ar) {
								object res = sock.EndInquire (ar);
								if (ACK.Equals (res))
									return;
								/// TODO: 失敗したらどうする ?
								if (res == null)
									_mgr.RaiseInquiryFailedEvent (mcrEp.EndPoint);
							}, null);
						} else {
							sock.SendTo (interTermMsg, mcrEp.EndPoint);
						}
					}
				}
			}

			public void CheckTimeout ()
			{
				if (_pingRecvExpire < DateTime.Now) {
					Close ();
					return;
				}

				if (_nextPingTime < DateTime.Now) {
					Send (MCRManager.PingMessage.Instance, true);
					Console.WriteLine ("T: Send Ping...");
				}
			}

			public void Close ()
			{
				lock (_mgr._routes) {
					_mgr._routes.Remove (_prev);
					_mgr._timeoutCheckList.Remove (this);
				}
				lock (_mgr._terms) {
					_mgr._terms.Remove (Label);
				}
			}

			#endregion
		}
		class TerminalNodeReceivedEventArgs : MCRTerminalNodeReceivedEventArgs
		{
			TerminalRouteInfo _ti;

			public TerminalNodeReceivedEventArgs (TerminalRouteInfo ti, object msg)
				: base (msg)
			{
				_ti = ti;
			}

			public override void Send (object msg, bool reliableMode)
			{
				_ti.Send (msg, reliableMode);
			}
		}
		public class FailedEventArgs : EventArgs
		{
			EndPoint _ep;

			public FailedEventArgs (EndPoint ep)
			{
				_ep = ep;
			}

			public EndPoint EndPoint {
				get { return _ep; }
			}
		}
		#endregion

		#region Cipher Helper Class
		internal static class CipherUtility
		{
			static SymmetricKey ComputeSharedKey (ECKeyPair privateKey, ECKeyPair pubKey, byte[] sharedInfo, SymmetricKeyOption opt)
			{
				byte[] iv = new byte[opt.BlockBytes];
				byte[] key = new byte[opt.KeyBytes];
				ECDiffieHellman ecdh = new ECDiffieHellman (privateKey);
				if (sharedInfo != null && sharedInfo.Length > 0)
					ecdh.SharedInfo = sharedInfo;
				byte[] sharedKey = ecdh.PerformKeyAgreement (pubKey, iv.Length + key.Length);
				Buffer.BlockCopy (sharedKey, 0, iv, 0, iv.Length);
				Buffer.BlockCopy (sharedKey, iv.Length, key, 0, key.Length);
				return new SymmetricKey (opt.Algorithm, iv, key, opt.CipherMode, opt.PaddingMode, opt.EnableIVShuffle);
			}

			public static byte[] CreateEstablishMessageData (NodeHandle[] relayNodes, SymmetricKey[] relayKeys, object payload, ECDomainNames ecDomain, SymmetricKeyOption opt, int fixedMsgSize)
			{
				byte[] msg = RNG.GetBytes (fixedMsgSize);
				int msg_size = 0;

				// 終端ノードに配送するデータを設定
				// TMP    = Payload-Size[16bit]||Payload
				// Output = '0'||Hash(TMP)||TMP
				using (HashAlgorithm hashAlgo = ConstantParameters.CreateHashAlgorithm ()) {
					msg[0] = 0;
					int hashBytes = hashAlgo.HashSize / 8;
					byte[] serialized = Serializer.Instance.Serialize (payload);
					msg[hashBytes + 1] = (byte)(serialized.Length >> 8);
					msg[hashBytes + 2] = (byte)(serialized.Length & 0xFF);
					Buffer.BlockCopy (serialized, 0, msg, hashBytes + 3, serialized.Length);
					byte[] hash = hashAlgo.ComputeHash (msg, hashBytes + 1, serialized.Length + 2);
					Buffer.BlockCopy (hash, 0, msg, 1, hash.Length);
					msg_size = hashBytes + 3 + serialized.Length;
				}

				for (int i = relayKeys.Length - 1; ; i--) {
					// PaddingMode==Noneなのでメッセージサイズをブロック長の整数倍に合わせる
					if (msg_size % opt.BlockBytes != 0)
						msg_size += opt.BlockBytes - (msg_size % opt.BlockBytes);

					// Ephemeralキーと中継ノードの公開鍵とで鍵共有
					ECKeyPair privateKey = ECKeyPair.Create (ecDomain);
					byte[] pubKey = privateKey.ExportPublicKey (true);
					relayKeys[i] = ComputeSharedKey (privateKey, relayNodes[i].NodeID.ToECPublicKey (), null, opt);
					byte[] cipher = relayKeys[i].Encrypt (msg, 0, msg_size);
					if (i == 0) {
						Buffer.BlockCopy (pubKey, 0, msg, 0, pubKey.Length);
						Buffer.BlockCopy (cipher, 0, msg, pubKey.Length, cipher.Length);
						break;
					} else {
						msg[0] = 1;
						byte[] nextHop = Serializer.Instance.Serialize (relayNodes[i].EndPoint);
						Buffer.BlockCopy (nextHop, 0, msg, 1, nextHop.Length);
						Buffer.BlockCopy (pubKey, 0, msg, 1 + nextHop.Length, pubKey.Length);
						Buffer.BlockCopy (cipher, 0, msg, 1 + nextHop.Length + pubKey.Length, cipher.Length);
						msg_size = 1 + nextHop.Length + pubKey.Length + cipher.Length;
					}
				}
				return msg;
			}

			public static byte[] DecryptEstablishMessageData (ECKeyPair privateKey, byte[] msg, int pubKeyBytes, SymmetricKeyOption opt, out SymmetricKey key, out EndPoint nextHop, out object payload)
			{
				ECKeyPair pubKey = ECKeyPairExtensions.CreatePublic (msg.CopyRange (0, pubKeyBytes));
				key = ComputeSharedKey (privateKey, pubKey, null, opt);
				int len = msg.Length - pubKeyBytes;
				if (len % opt.BlockBytes != 0) len -= len % opt.BlockBytes;
				byte[] decrypted = key.Decrypt (msg, pubKeyBytes, len);
				switch (decrypted[0]) {
					case 0:
						nextHop = null;
						using (HashAlgorithm hashAlgo = ConstantParameters.CreateHashAlgorithm ()) {
							int hashBytes = hashAlgo.HashSize >> 3;
							int payload_size = (decrypted[hashBytes + 1] << 8) | decrypted[hashBytes + 2];
							if (payload_size > decrypted.Length - hashBytes - 3)
								throw new CryptographicException ();
							byte[] hash = hashAlgo.ComputeHash (decrypted, hashBytes + 1, payload_size + 2);
							if (!hash.EqualsRange (0, decrypted, 1, hash.Length))
								throw new CryptographicException ();
							payload = Serializer.Instance.Deserialize (decrypted, hashBytes + 3, payload_size);
						}
						return null;
					case 1:
						payload = null;
						byte[] ret = RNG.GetBytes (msg.Length);
						using (MemoryStream ms = new MemoryStream (decrypted, 1, decrypted.Length - 1)) {
							nextHop = (EndPoint)Serializer.Instance.Deserialize (ms);
							Buffer.BlockCopy (decrypted, 1 + (int)ms.Position, ret, 0, decrypted.Length - 1 - (int)ms.Position);
						}
						return ret;
				}
				throw new CryptographicException ();
			}

			static byte[] CreateRoutedPayload (uint seq, object obj, int fixedPayloadSize)
			{
				byte[] payload = RNG.GetBytes (fixedPayloadSize);
				using (MemoryStream ms = new MemoryStream ())
				using (HashAlgorithm hashAlgo = ConstantParameters.CreateHashAlgorithm ()) {
					ms.Write (new byte[] {(byte)(seq >> 24), (byte)(seq >> 16), (byte)(seq >> 8), (byte)seq, 0, 0}, 0, 6);
					Serializer.Instance.Serialize (ms, obj);
					ms.Close ();
					byte[] tmp = ms.ToArray ();
					int obj_size = tmp.Length - 6;
					tmp[4] = (byte)(obj_size >> 8);
					tmp[5] = (byte)(obj_size);
					byte[] hash = hashAlgo.ComputeHash (tmp);
					Buffer.BlockCopy (hash, 0, payload, 0, hash.Length);
					Buffer.BlockCopy (tmp, 0, payload, hash.Length, tmp.Length);
				}
				return payload;
			}

			public static byte[] CreateRoutedPayload (SymmetricKey key, uint seq, object obj, int fixedPayloadSize)
			{
				byte[] payload = CreateRoutedPayload (seq, obj, fixedPayloadSize);
				return key.Encrypt (payload, 0, payload.Length);
			}

			public static byte[] CreateRoutedPayload (SymmetricKey[] keys, uint seq, object obj, int fixedPayloadSize)
			{
				byte[] payload = CreateRoutedPayload (seq, obj, fixedPayloadSize);
				for (int i = keys.Length - 1; i >= 0; i --)
					payload = keys[i].Encrypt (payload, 0, payload.Length);
				return payload;
			}

			static object DecryptRoutedPayload (byte[] plain, out uint seq)
			{
				using (HashAlgorithm hashAlgo = ConstantParameters.CreateHashAlgorithm ()) {
					int hashBytes = hashAlgo.HashSize >> 3;
					int obj_size = (plain[hashBytes + 4] << 8) | plain[hashBytes + 5];
					if (obj_size > plain.Length - hashBytes - 6)
						throw new CryptographicException ();
					byte[] hash = hashAlgo.ComputeHash (plain, hashBytes, obj_size + 6);
					if (!hash.EqualsRange (0, plain, 0, hash.Length))
						throw new CryptographicException ();
					seq = ((uint)plain[hashBytes + 0] << 24) | ((uint)plain[hashBytes + 1] << 16)
						| ((uint)plain[hashBytes + 2] << 8) | (uint)plain[hashBytes + 3];
					return Serializer.Instance.Deserialize (plain, hashBytes + 6, obj_size);
				}
			}

			public static object DecryptRoutedPayload (SymmetricKey key, out uint seq, byte[] encrypted)
			{
				return DecryptRoutedPayload (key.Decrypt (encrypted, 0, encrypted.Length), out seq);
			}

			public static object DecryptRoutedPayload (SymmetricKey[] keys, out uint seq, byte[] encrypted)
			{
				for (int i = 0; i < keys.Length; i ++)
					encrypted = keys[i].Decrypt (encrypted, 0, encrypted.Length);
				return DecryptRoutedPayload (encrypted, out seq);
			}
		}

		internal class SymmetricKeyOption
		{
			public SymmetricKeyOption ()
			{
				Algorithm = SymmetricAlgorithmType.Camellia;
				BlockSize = 128;
				KeySize = 128;
				PaddingMode = PaddingMode.None;
				CipherMode = CipherModePlus.CBC;
				EnableIVShuffle = false;
			}

			public SymmetricAlgorithmType Algorithm { get; set; }
			public int BlockSize { get; set; }
			public int BlockBytes { get { return BlockSize >> 3; } }
			public int KeySize { get; set; }
			public int KeyBytes { get { return KeySize >> 3; } }
			public PaddingMode PaddingMode { get; set; }
			public CipherModePlus CipherMode { get; set; }
			public bool EnableIVShuffle { get; set; }
		}
		#endregion

		#region Messages
		[SerializableTypeId (0x400)]
		internal sealed class EstablishRouteMessage
		{
			[SerializableFieldId (0)]
			RouteLabel _label;

			[SerializableFieldId (1)]
			byte[] _msg;

			public EstablishRouteMessage (RouteLabel label, byte[] msg)
			{
				_label = label;
				_msg = msg;
			}

			public byte[] Encrypted {
				get { return _msg; }
			}

			public RouteLabel Label {
				get { return _label; }
			}
		}

		[SerializableTypeId (0x401)]
		internal sealed class EstablishedRouteMessage
		{
			[SerializableFieldId (0)]
			RouteLabel _label;

			[SerializableFieldId (1)]
			object _response;

			public EstablishedRouteMessage (RouteLabel label, object response)
			{
				_label = label;
				_response = response;
			}

			public RouteLabel Label {
				get { return _label; }
			}

			public object Response {
				get { return _response; }
			}
		}

		[SerializableTypeId (0x402)]
		internal sealed class RoutedMessage
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

		[SerializableTypeId (0x403)]
		internal sealed class InterTerminalRequestMessage
		{
			[SerializableFieldId (0)]
			MCREndPoint[] _dstEPs;

			[SerializableFieldId (1)]
			MCREndPoint[] _srcEPs;

			[SerializableFieldId (2)]
			ulong _id;

			[SerializableFieldId (3)]
			object _payload;

			public InterTerminalRequestMessage (MCREndPoint[] dstEPs, MCREndPoint[] srcEPs, object payload, ulong id)
			{
				_dstEPs = dstEPs;
				_srcEPs = srcEPs;
				_payload = payload;
				_id = id;
			}

			public MCREndPoint[] DestEndPoints {
				get { return _dstEPs; }
			}

			public MCREndPoint[] SrcEndPoints {
				get { return _srcEPs; }
			}

			public object Payload {
				get { return _payload; }
			}

			public ulong ID {
				get { return _id; }
			}
		}

		[SerializableTypeId (0x404)]
		internal sealed class InterTerminalMessage
		{
			[SerializableFieldId (0)]
			RouteLabel _label;

			[SerializableFieldId (1)]
			MCREndPoint[] _srcEPs;

			[SerializableFieldId (2)]
			ulong _id;

			[SerializableFieldId (3)]
			object _payload;

			public InterTerminalMessage (RouteLabel label, MCREndPoint[] srcEPs, object payload, ulong id)
			{
				_label = label;
				_srcEPs = srcEPs;
				_payload = payload;
				_id = id;
			}

			public RouteLabel Label {
				get { return _label; }
			}

			public MCREndPoint[] SrcEndPoints {
				get { return _srcEPs; }
			}

			public object Payload {
				get { return _payload; }
			}

			public ulong ID {
				get { return _id; }
			}
		}

		[SerializableTypeId (0x405)]
		internal sealed class InterTerminalPayload
		{
			[SerializableFieldId (0)]
			MCREndPoint[] _srcEPs;

			[SerializableFieldId (1)]
			ulong _id;

			[SerializableFieldId (2)]
			object _payload;

			public InterTerminalPayload (MCREndPoint[] srcEPs, object payload, ulong id)
			{
				_srcEPs = srcEPs;
				_payload = payload;
				_id = id;
			}

			public MCREndPoint[] SrcEndPoints {
				get { return _srcEPs; }
			}

			public object Payload {
				get { return _payload; }
			}

			public ulong ID {
				get { return _id; }
			}
		}

		[SerializableTypeId (0x406)]
		internal sealed class DisconnectMessage
		{
			[SerializableFieldId (0)]
			RouteLabel _label;

			public DisconnectMessage (RouteLabel label)
			{
				_label = label;
			}

			public RouteLabel Label {
				get { return _label; }
			}
		}

		[SerializableTypeId (0x407)]
		internal sealed class PingMessage
		{
			static PingMessage _instance = new PingMessage ();
			PingMessage () {}
			public static PingMessage Instance {
				get { return _instance; }
			}
		}
		#endregion
	}
}
