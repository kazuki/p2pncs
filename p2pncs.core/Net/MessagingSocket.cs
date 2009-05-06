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
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using p2pncs.Security.Cryptography;
using p2pncs.Threading;

namespace p2pncs.Net
{
	public class MessagingSocket : MessagingSocketBase
	{
		SymmetricKey _key;
		IFormatter _formatter;
		object _nullObject;

		public MessagingSocket (IDatagramEventSocket sock, bool ownSocket, SymmetricKey key,
			IFormatter formatter, object nullObject, IntervalInterrupter interrupter,
			TimeSpan timeout, int maxRetry, int retryBufferSize)
			: base (sock, ownSocket, interrupter, timeout, maxRetry, retryBufferSize)
		{
			_key = (key != null ? key : SymmetricKey.NoneKey);
			_formatter = formatter;
			_nullObject = nullObject != null ? nullObject : NullObject.Instance;
			sock.Received += Socket_Received;
		}

		void Socket_Received (object sender, DatagramReceiveEventArgs e)
		{
			if (!IsActive)
				return;

			MessageType type;
			object obj;
			int tmp;
			ushort id = 0;
			try {
				byte[] msg = _key.Decrypt (e.Buffer, 0, e.Size);
				using (MemoryStream strm = new MemoryStream (msg, 0, msg.Length, false)) {
					if ((tmp = strm.ReadByte ()) < 0)
						goto MessageError;
					type = (MessageType)tmp;
					if (type != MessageType.OneWay) {
						for (int i = 0; i < 16; i += 8) {
							if ((tmp = strm.ReadByte ()) < 0)
								goto MessageError;
							id |= (ushort)(tmp << i);
						}
					}
					obj = _formatter.Deserialize (strm);
					if (obj == null)
						goto MessageError;
				}
			} catch {
				goto MessageError;
			}

			switch (type) {
				case MessageType.Request:
					InquiredEventArgs args = new InquiredResponseState (obj, e.RemoteEndPoint, id);
					InvokeInquired (this, args);
					break;
				case MessageType.Response:
					InquiredAsyncResultBase ar = RemoveFromRetryList (id, e.RemoteEndPoint);
					if (ar == null)
						return;
					ar.Complete (obj);
					InvokeInquirySuccess (this, new InquiredEventArgs (ar.Request, obj, e.RemoteEndPoint));
					break;
				case MessageType.OneWay:
					InvokeReceived (this, new ReceivedEventArgs (obj, e.RemoteEndPoint));
					break;
				default:
					goto MessageError;
			}
			return;

MessageError:
			return;
		}

		#region IMessagingSocket/MessagingSocketBase Members

		public override void Send (object obj, EndPoint remoteEP)
		{
			if (obj == null || remoteEP == null)
				throw new ArgumentNullException ();

			byte[] raw = SerializeTransmitData (MessageType.OneWay, 0, obj);
			_sock.SendTo (raw, remoteEP);
		}

		protected override void StartResponse_Internal (MessagingSocketBase.InquiredResponseState state, object response)
		{
			if (response == null)
				response = _nullObject;
			byte[] raw = SerializeTransmitData (MessageType.Response, state.ID, response);
			_sock.SendTo (raw, state.EndPoint);
		}

		protected override InquiredAsyncResultBase CreateInquiredAsyncResult (ushort id, object obj, EndPoint remoteEP, TimeSpan timeout, int maxRetry, AsyncCallback callback, object state)
		{
			byte[] msg = SerializeTransmitData (MessageType.Request, id, obj);
			return new InquiredAsyncResult (obj, msg, remoteEP, id, timeout, maxRetry, callback, state);
		}

		#endregion

		#region Misc
		byte[] SerializeTransmitData (MessageType type, ushort id, object obj)
		{
			using (MemoryStream strm = new MemoryStream ()) {
				strm.WriteByte ((byte)type);
				if (type != MessageType.OneWay) {
					strm.WriteByte ((byte)(id));
					strm.WriteByte ((byte)(id >> 8));
				}
				_formatter.Serialize (strm, obj);
				strm.Close ();
				byte[] raw = strm.ToArray ();
				return _key.Encrypt (raw, 0, raw.Length);
			}
		}
		#endregion

		#region internal classes
		enum MessageType : byte
		{
			Request = 0,
			Response = 1,
			OneWay = 2
		}

		class InquiredAsyncResult : InquiredAsyncResultBase
		{
			byte[] _dgram;

			public InquiredAsyncResult (object req, byte[] dgram, EndPoint remoteEP, ushort id, TimeSpan timeout, int maxRetry, AsyncCallback callback, object state)
				: base (req, remoteEP, id, timeout, maxRetry, callback, state)
			{
				_dgram = dgram;
			}

			protected override void Transmit_Internal (IDatagramEventSocket sock)
			{
				sock.SendTo (_dgram, 0, _dgram.Length, _remoteEP);
			}
		}

		[Serializable]
		[SerializableTypeId (0x120)]
		class NullObject
		{
			static NullObject _instance = new NullObject ();
			public static NullObject Instance {
				get { return _instance; }
			}
			NullObject () {}
		}
		#endregion
	}
}
