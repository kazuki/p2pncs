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
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using openCrypto;
using openCrypto.EllipticCurve;
using p2pncs.Security.Cryptography;
using p2pncs.Utility;
using ECDiffieHellman = openCrypto.EllipticCurve.KeyAgreement.ECDiffieHellman;
using RouteLabel = System.UInt32;

namespace p2pncs.Net.Overlay.Anonymous
{
	public partial class AnonymousRouter
	{
		partial class AnonymousEndPoint {
			class MCRStartInfo : IRouteInfo
			{
				AnonymousEndPoint _aep;
				NodeHandle[] _relayNodes;
				SymmetricKey[] _relayKeys;
				RouteLabel _label;
				DateTime _nextPingTime;
				int _seq = 0;
				AntiReplayWindow _recvWin = new AntiReplayWindow (1024);
				bool _active = true;

				public MCRStartInfo (AnonymousEndPoint aep, NodeHandle[] relayNodes, SymmetricKey[] relayKeys, RouteLabel label)
				{
					_aep = aep;
					_relayNodes = relayNodes;
					_relayKeys = relayKeys;
					_label = label;
					StartTime = DateTime.Now;
					Established = false;
					_nextPingTime = DateTime.Now + MCR_PingInterval;
				}

				public RouteLabel Label {
					get { return _label; }
				}

				public NodeHandle[] RelayNodes {
					get { return _relayNodes; }
				}

				public bool Established { get; set; }
				public DateTime StartTime { get; set; }

				public void Send (object msg)
				{
					if (!_active) return;
					IMessagingSocket sock = _aep._router._sock;
					byte[] payload = MCRCipherUtility.CreateRoutedPayload (_relayKeys, (uint)Interlocked.Increment (ref _seq), msg, RoutedPayloadSize);
					sock.BeginInquire (new RoutedMessage (_label, payload), _relayNodes[0].EndPoint, delegate (IAsyncResult ar) {
						sock.EndInquire (ar);
					}, null);
					_nextPingTime = DateTime.Now + MCR_PingInterval;
				}

				public void Inquired (AnonymousRouter router, RoutedMessage msg, MCREndPoint sender)
				{
					if (!_active) return;
					uint seq;
					object payload = MCRCipherUtility.DecryptRoutedPayload (_relayKeys, out seq, msg.Payload);
					if (!_recvWin.Check (seq)) {
						Logger.Log (LogLevel.Trace, router, "MCR: StartPoint Detected Replay (seq={0})", seq);
						return;
					}
					Logger.Log (LogLevel.Trace, router, "MCR: StartPoint Received {0} (seq={1})", payload, seq);
					_aep.Received (this, payload);
				}

				public bool Check ()
				{
					if (!_active) return false;
					if (!Established) {
						if (StartTime + MCR_EstablishTimeout <= DateTime.Now) {
							Logger.Log (LogLevel.Trace, _aep._router, "MCR: Establish Timeout");
							return false;
						}
						return true;
					}

					if (_nextPingTime <= DateTime.Now)
						Send (PingMessage.Instance);
					return true;
				}

				public void Close ()
				{
					_active = false;
				}

				void IRouteInfo.Close ()
				{
					Close ();
					_aep.Closed (this);
				}
			}
		}

		class MCRRelayInfo : IRouteInfo
		{
			MCREndPoint _prev, _next;
			SymmetricKey _key;
			DateTime _prevExpiration, _nextExpiration;
			bool _active = true;

			public MCRRelayInfo (MCREndPoint prev, MCREndPoint next, SymmetricKey key)
			{
				_prev = prev;
				_next = next;
				_key = key;
				_prevExpiration = _nextExpiration = DateTime.Now + MCR_Timeout;
			}

			public MCREndPoint PrevEndPoint {
				get { return _prev; }
			}

			public MCREndPoint NextEndPoint {
				get { return _next; }
			}

			public void Inquired (AnonymousRouter router, RoutedMessage msg, MCREndPoint sender)
			{
				if (!_active) return;
				Logger.Log (LogLevel.Trace, router, "MCR: Relay");
				DateTime newExpiration = DateTime.Now + MCR_Timeout;
				if (_prev.Equals (sender)) {
					_prevExpiration = newExpiration;
					msg = new RoutedMessage (_next.Label, _key.Decrypt (msg.Payload, 0, msg.Payload.Length));
					router._sock.BeginInquire (msg, _next.EndPoint, delegate (IAsyncResult ar) {
						router._sock.EndInquire (ar);
					}, null);
				} else {
					_nextExpiration = newExpiration;
					msg = new RoutedMessage (_prev.Label, _key.Encrypt (msg.Payload, 0, msg.Payload.Length));
					router._sock.BeginInquire (msg, _prev.EndPoint, delegate (IAsyncResult ar) {
						router._sock.EndInquire (ar);
					}, null);
				}
			}

			public bool Check ()
			{
				if (!_active) return false;
				return _prevExpiration > DateTime.Now && _nextExpiration > DateTime.Now;
			}

			public void Close ()
			{
				_active = false;
			}
		}

		class MCRBoundaryInfo : IRouteInfo
		{
			MCREndPoint _prev;
			SymmetricKey _key;
			RouteLabel _label;
			object _payload;
			int _seq = 0;
			DateTime _timeout;
			AntiReplayWindow _recvWin = new AntiReplayWindow (1024);
			bool _active = true;

			public MCRBoundaryInfo (MCREndPoint prev, SymmetricKey key, RouteLabel label, object payload)
			{
				_prev = prev;
				_key = key;
				_label = label;
				_payload = payload;
				_timeout = DateTime.Now + MCR_Timeout;
			}

			public MCREndPoint PrevEndPoint {
				get { return _prev; }
			}

			public RouteLabel Label {
				get { return _label; }
			}

			public object Payload {
				get { return _payload; }
			}

			public void Send (IMessagingSocket sock, object msg)
			{
				if (!_active) return;
				byte[] payload = MCRCipherUtility.CreateRoutedPayload (_key, (uint)Interlocked.Increment (ref _seq), msg, RoutedPayloadSize);
				sock.BeginInquire (new RoutedMessage (_prev.Label, payload), _prev.EndPoint, delegate (IAsyncResult ar) {
					sock.EndInquire (ar);
				}, null);
			}

			public void Inquired (AnonymousRouter router, RoutedMessage msg, MCREndPoint sender)
			{
				if (!_active) return;
				uint seq;
				object payload = MCRCipherUtility.DecryptRoutedPayload (_key, out seq, msg.Payload);
				if (!_recvWin.Check (seq)) {
					Logger.Log (LogLevel.Trace, router, "MCR: Boundary Detected Replay (seq={0})", seq);
					return;
				}
				Logger.Log (LogLevel.Trace, router, "MCR: Boundary Received {0} (seq={1})", payload, seq);
				_timeout = DateTime.Now + MCR_Timeout;
				if (payload is PingMessage)
					Send (router._sock, PingMessage.Instance);
			}

			public bool Check ()
			{
				if (!_active) return false;
				return _timeout > DateTime.Now;
			}

			public void Close ()
			{
				_active = false;
			}
		}

		static class MCRCipherUtility
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

			static HashAlgorithm CreateHashAlgorithm ()
			{
				return new SHA1Managed ();
			}

			public static byte[] CreateEstablishMessageData (NodeHandle[] relayNodes, SymmetricKey[] relayKeys, object payload, ECDomainNames ecDomain, SymmetricKeyOption opt, int fixedMsgSize)
			{
				byte[] msg = RNG.GetBytes (fixedMsgSize);
				int msg_size = 0;

				// 終端ノードに配送するデータを設定
				// TMP    = Payload-Size[16bit]||Payload
				// Output = '0'||Hash(TMP)||TMP
				using (HashAlgorithm hashAlgo = CreateHashAlgorithm ()) {
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
						using (HashAlgorithm hashAlgo = MCRCipherUtility.CreateHashAlgorithm ()) {
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
				using (HashAlgorithm hashAlgo = CreateHashAlgorithm ()) {
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
				using (HashAlgorithm hashAlgo = CreateHashAlgorithm ()) {
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

		class SymmetricKeyOption
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

		class MCREndPoint : EndPoint, IEquatable<MCREndPoint>
		{
			EndPoint _ep;
			RouteLabel _label;

			public MCREndPoint (EndPoint ep, RouteLabel label)
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

			public override int GetHashCode ()
			{
				return _ep.GetHashCode () ^ _label.GetHashCode ();
			}

			public override bool Equals (object obj)
			{
				return Equals ((MCREndPoint)obj);
			}

			public bool Equals (MCREndPoint other)
			{
				return _label == other._label && _ep.Equals (other._ep);
			}

			public override string ToString ()
			{
				return _ep.ToString () + "#" + _label.ToString ("x");
			}
		}
	}
}
