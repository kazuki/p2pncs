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

		const int MAX_HEADER_SIZE = 5;
		int _maxMsgSize;

		public MessagingSocket (IDatagramEventSocket sock, bool ownSocket, SymmetricKey key,
			IFormatter formatter, object nullObject, IntervalInterrupter interrupter,
			IRTOAlgorithm rtoAlgo, int maxRetry, int retryBufferSize, int inquiryDupCheckSize)
			: base (sock, ownSocket, interrupter, rtoAlgo, maxRetry, retryBufferSize, inquiryDupCheckSize)
		{
			_key = (key != null ? key : SymmetricKey.NoneKey);
			_formatter = formatter;
			_nullObject = nullObject != null ? nullObject : NullObject.Instance;
			sock.Received += Socket_Received;
			_maxMsgSize = sock.MaxDatagramSize;
			if (_key.AlgorithmType != SymmetricAlgorithmType.None && key.IV != null) {
				_maxMsgSize -= _maxMsgSize % key.IV.Length;
				if (key.Padding != System.Security.Cryptography.PaddingMode.None)
					_maxMsgSize --;
				if (key.EnableIVShuffle)
					_maxMsgSize -= key.IV.Length;
			}
			_maxMsgSize -= MAX_HEADER_SIZE;
		}

		void Socket_Received (object sender, DatagramReceiveEventArgs e)
		{
			if (!IsActive)
				return;

			MessageType type;
			object obj;
			int tmp;
			uint id = 0;
			try {
				byte[] msg = _key.Decrypt (e.Buffer, 0, e.Size);
				using (MemoryStream strm = new MemoryStream (msg, 0, msg.Length, false)) {
					if ((tmp = strm.ReadByte ()) < 0)
						goto MessageError;
					type = (MessageType)tmp;
					if (type != MessageType.OneWay) {
						for (int i = 0; i < 32; i += 8) {
							if ((tmp = strm.ReadByte ()) < 0)
								goto MessageError;
							id |= (uint)(tmp << i);
						}
					}
					obj = _formatter.Deserialize (strm);
					if (obj == null)
						goto MessageError;
				}
			} catch {
				goto MessageError;
			}

			if (_nullObject.Equals (obj))
				obj = null;
			string strMsgType = (obj == null ? "null msg" : obj.GetType ().Name);

			switch (type) {
				case MessageType.Request:
					ThreadTracer.AppendThreadName (" (req: " + strMsgType + ")");
					InquiredEventArgs args = new InquiredResponseState (obj, e.RemoteEndPoint, id);
					InvokeInquired (this, args);
					break;
				case MessageType.Response:
					InquiredAsyncResultBase ar = RemoveFromRetryList (id, e.RemoteEndPoint);
					if (ar == null)
						return;
					ThreadTracer.AppendThreadName (" (res: " + strMsgType + ", req: "
						+ (ar.Request == null ? "null msg" : ar.Request.GetType ().Name) + ")");
					ar.Complete (obj, this);
					InvokeInquirySuccess (this, new InquiredEventArgs (ar.Request, obj, e.RemoteEndPoint, DateTime.Now - ar.TransmitTime, ar.RetryCount));
					break;
				case MessageType.OneWay:
					ThreadTracer.AppendThreadName (" (ow: " + strMsgType + ")");
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
			if (remoteEP == null)
				throw new ArgumentNullException ();
			if (obj == null)
				obj = _nullObject;

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

		protected override InquiredAsyncResultBase CreateInquiredAsyncResult (uint id, object obj, EndPoint remoteEP, TimeSpan timeout, int maxRetry, AsyncCallback callback, object state)
		{
			if (remoteEP == null)
				throw new ArgumentNullException ();
			if (obj == null)
				obj = _nullObject;
			byte[] msg = SerializeTransmitData (MessageType.Request, id, obj);
			return new InquiredAsyncResult (obj, msg, remoteEP, id, timeout, maxRetry, callback, state);
		}

		public override int MaxMessageSize {
			get { return _maxMsgSize; }
		}

		#endregion

		#region Misc
		byte[] SerializeTransmitData (MessageType type, uint id, object obj)
		{
			using (MemoryStream strm = new MemoryStream ()) {
				strm.WriteByte ((byte)type);
				if (type != MessageType.OneWay) {
					strm.WriteByte ((byte)(id));
					strm.WriteByte ((byte)(id >> 8));
					strm.WriteByte ((byte)(id >> 16));
					strm.WriteByte ((byte)(id >> 24));
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

			public InquiredAsyncResult (object req, byte[] dgram, EndPoint remoteEP, uint id, TimeSpan timeout, int maxRetry, AsyncCallback callback, object state)
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

			public override bool Equals (object obj)
			{
				return (obj is NullObject);
			}

			public override int GetHashCode ()
			{
				return 0;
			}
		}
		#endregion
	}
}
