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
using System.Runtime.Serialization;
using System.Threading;
using p2pncs.Security.Cryptography;
using p2pncs.Threading;
using openCrypto;

namespace p2pncs.Net
{
	public class MessagingSocket : IMessagingSocket
	{
		TimeSpan _inquiryTimeout;
		int _maxRetries, _retryListSize;

		IDatagramEventSocket _sock;
		SymmetricKey _key;
		bool _active = true, _ownSocket;
		IFormatter _formatter;
		object _nullObject;
		List<InquiredAsyncResult> _retryList = new List<InquiredAsyncResult> ();
		IntervalInterrupter _interrupter;

		public event InquiredEventHandler Inquired;
		public event InquiredEventHandler InquiryFailure;
		public event InquiredEventHandler InquirySuccess;

		public MessagingSocket (IDatagramEventSocket sock, bool ownSocket, SymmetricKey key,
			IFormatter formatter, object nullObject, IntervalInterrupter interrupter,
			TimeSpan timeout, int maxRetry, int retryBufferSize)
		{
			_sock = sock;
			_ownSocket = ownSocket;
			_key = (key != null ? key : SymmetricKey.NoneKey);
			_formatter = formatter;
			_nullObject = nullObject != null ? nullObject : NullObject.Instance;
			_interrupter = interrupter;
			_inquiryTimeout = timeout;
			_maxRetries = maxRetry;
			_retryListSize = retryBufferSize;

			interrupter.AddInterruption (TimeoutCheck);
			_sock.Received += new DatagramReceiveEventHandler (Socket_Received);
		}

		void Socket_Received (object sender, DatagramReceiveEventArgs e)
		{
			if (!_active)
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
					for (int i = 0; i < 16; i += 8) {
						if ((tmp = strm.ReadByte ()) < 0)
							goto MessageError;
						id |= (ushort)(tmp << i);
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
					if (Inquired != null) {
						try {
							Inquired (this, args);
						} catch {}
					}
					break;
				case MessageType.Response:
					InquiredAsyncResult ar = RemoveFromRetryList (id, e.RemoteEndPoint);
					if (ar == null)
						return;
					ar.Complete (obj);
					if (InquirySuccess != null) {
						InquirySuccess (this, new InquiredEventArgs (ar.Request, obj, e.RemoteEndPoint));
					}
					break;
				default:
					goto MessageError;
			}
			return;

MessageError:
			return;
		}

		public void StartResponse (InquiredEventArgs args, object response, bool throwWhenAlreadySent)
		{
			InquiredResponseState state = args as InquiredResponseState;
			if (state == null)
				throw new ArgumentException ();
			if (response == null)
				response = _nullObject;
			byte[] raw = SerializeTransmitData (MessageType.Response, state.ID, response);
			state.Send (_sock, raw, throwWhenAlreadySent);
		}

		void TimeoutCheck ()
		{
			List<InquiredAsyncResult> timeoutList = new List<InquiredAsyncResult> ();
			DateTime now = DateTime.Now;
			lock (_retryList) {
				for (int i = 0; i < _retryList.Count; i ++) {
					if (_retryList[i].Timeout <= now) {
						timeoutList.Add (_retryList[i]);
						if (_retryList[i].RetryCount >= _maxRetries) {
							_retryList.RemoveAt (i);
							i --;
						}
					}
				}
			}
			for (int i = 0; i < timeoutList.Count; i ++) {
				InquiredAsyncResult iar = timeoutList[i];
				if (iar.RetryCount >= _maxRetries) {
					iar.Fail ();
					if (InquiryFailure != null)
						InquiryFailure (this, new InquiredEventArgs (iar.Request, iar.Response, iar.RemoteEndPoint));
				} else {
					iar.Retry (_sock, _inquiryTimeout);
				}
			}
		}

		#region IMessagingSocket Members

		public IAsyncResult BeginInquire (object obj, EndPoint remoteEP, AsyncCallback callback, object state)
		{
			if (obj == null || remoteEP == null)
				throw new ArgumentNullException ();

			ushort id = CreateMessageID ();
			byte[] msg = SerializeTransmitData (MessageType.Request, id, obj);
			InquiredAsyncResult ar = new InquiredAsyncResult (obj, msg, remoteEP, id, callback, state);
			ar.Transmit (_sock, _inquiryTimeout);

			InquiredAsyncResult overflow = null;
			lock (_retryList) {
				if (_retryList.Count > _retryListSize) {
					overflow = _retryList[0];
					_retryList.RemoveAt (0);
				}
				_retryList.Add (ar);
			}
			if (overflow != null) {
				overflow.Fail ();
				if (InquiryFailure != null)
					InquiryFailure (this, new InquiredEventArgs (overflow.Request, overflow.Response, overflow.RemoteEndPoint));
			}
			return ar;
		}

		public object EndInquire (IAsyncResult iar)
		{
			InquiredAsyncResult ar = iar as InquiredAsyncResult;
			if (ar == null)
				throw new ArgumentException ();
			ar.AsyncWaitHandle.WaitOne ();
			ar.AsyncWaitHandle.Close ();
			return ar.Response;
		}

		public void Dispose ()
		{
			Close ();
		}

		public void Close ()
		{
			if (!_active)
				return;
			_active = false;
			_interrupter.RemoveInterruption (TimeoutCheck);
			if (_ownSocket) {
				_sock.Close ();
			}
			lock (_retryList) {
				for (int i = 0; i < _retryList.Count; i ++)
					_retryList[i].Fail ();
				_retryList.Clear ();
			}
		}

		public IDatagramEventSocket BaseSocket {
			get { return _sock; }
		}

		#endregion

		#region Misc
		ushort CreateMessageID ()
		{
			return BitConverter.ToUInt16 (RNG.GetRNGBytes (4), 0);
		}

		byte[] SerializeTransmitData (MessageType type, ushort id, object obj)
		{
			using (MemoryStream strm = new MemoryStream ()) {
				strm.WriteByte ((byte)type);
				strm.WriteByte ((byte)(id));
				strm.WriteByte ((byte)(id >> 8));
				_formatter.Serialize (strm, obj);
				strm.Close ();
				byte[] raw = strm.ToArray ();
				return _key.Encrypt (raw, 0, raw.Length);
			}
		}

		InquiredAsyncResult RemoveFromRetryList (ushort id, EndPoint ep)
		{
			lock (_retryList) {
				for (int i = 0; i < _retryList.Count; i++) {
					InquiredAsyncResult ar = _retryList[i];
					if (ar.ID == id && ep.Equals (ar.RemoteEndPoint)) {
						_retryList.RemoveAt (i);
						return ar;
					}
				}
			}
			return null;
		}
		#endregion

		#region internal classes
		enum MessageType : byte
		{
			Request = 0,
			Response = 1
		}

		class InquiredAsyncResult : IAsyncResult
		{
			byte[] _dgram;
			EndPoint _remoteEP;
			object _state;
			AsyncCallback _callback;
			ManualResetEvent _waitHandle = new ManualResetEvent (false);
			object _req, _response = null;
			bool _isCompleted = false;
			DateTime _dt, _timeout;
			ushort _id;
			int _retries = 0;

			public InquiredAsyncResult (object req, byte[] dgram, EndPoint remoteEP, ushort id, AsyncCallback callback, object state)
			{
				_req = req;
				_dgram = dgram;
				_remoteEP = remoteEP;
				_callback = callback;
				_state = state;
				_id = id;
			}

			public void Transmit (IDatagramEventSocket sock, TimeSpan timeout)
			{
				_dt = DateTime.Now;
				_timeout = _dt + timeout;
				sock.SendTo (_dgram, 0, _dgram.Length, _remoteEP);
			}

			public void Retry (IDatagramEventSocket sock, TimeSpan timeout)
			{
				_retries++;
				Transmit (sock, timeout);
			}

			public object Request {
				get { return _req; }
			}

			public object Response {
				get { return _response; }
			}

			public DateTime TransmitTime {
				get { return _dt; }
			}

			public DateTime Timeout {
				get { return _timeout; }
			}

			public ushort ID {
				get { return _id; }
			}

			public int RetryCount {
				get { return _retries; }
			}

			public EndPoint RemoteEndPoint {
				get { return _remoteEP; }
			}

			public void Complete (object obj)
			{
				_response = obj;
				_isCompleted = true;
				_waitHandle.Set ();
				if (_callback != null) {
					try {
						_callback (this);
					} catch {}
				}
			}

			public void Fail ()
			{
				Complete (null);
			}

			public object AsyncState {
				get { return _state; }
			}

			public WaitHandle AsyncWaitHandle {
				get { return _waitHandle; }
			}

			public bool CompletedSynchronously {
				get { return false; }
			}

			public bool IsCompleted {
				get { return _isCompleted; }
			}
		}

		class InquiredResponseState : InquiredEventArgs
		{
			ushort _id;
			bool _sentFlag = false;

			public InquiredResponseState (object inq, EndPoint ep, ushort id) : base (inq, ep)
			{
				_id = id;
			}

			public void Send (IDatagramEventSocket sock, byte[] raw, bool throwWhenAlreadySent)
			{
				if (_sentFlag) {
					if (throwWhenAlreadySent)
						throw new ApplicationException ();
					return;
				}
				sock.SendTo (raw, 0, raw.Length, this.EndPoint);
				_sentFlag = true;
			}

			public ushort ID {
				get { return _id; }
			}
		}

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
