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
using System.Threading;
using p2pncs.Threading;
using p2pncs.Utility;
using MsgLabel = System.UInt32;

namespace p2pncs.Net
{
	public class InquirySocket : IInquirySocket
	{
		ISocket _sock;
		bool _ownSocket, _active = true;
		IntervalInterrupter _int;
		IRTOAlgorithm _rto;
		int _maxRetries, _retryListSize;
		EventHandlers<Type, InquiredEventArgs> _inquired = new EventHandlers<Type,InquiredEventArgs> ();
		List<InquiredAsyncResult> _retryList = new List<InquiredAsyncResult> ();
		long _numInquiries = 0, _numReInquiries = 0;

		public InquirySocket (ISocket baseSocket, bool ownSocket, IntervalInterrupter interrupter,
			IRTOAlgorithm defaultRTO, int defaultRetries, int retryBufferSize)
		{
			_sock = baseSocket;
			_ownSocket = ownSocket;
			_int = interrupter;
			_rto = defaultRTO;
			_maxRetries = defaultRetries;
			_retryListSize = retryBufferSize;

			_sock.Received.Add (typeof (InquiryRequest), Received_InquiryRequest);
			_sock.Received.Add (typeof (InquiryResponse), Received_InquiryResponse);

			interrupter.AddInterruption (CheckTimeout);
		}

		void Received_InquiryRequest (object sender, ReceivedEventArgs e)
		{
			InquiryRequest req = (InquiryRequest)e.Message;
			Inquired.Invoke (req.Request.GetType (), this, new InquiredResponseState (req.Request, e.RemoteEndPoint, req.MessageId));
		}

		void Received_InquiryResponse (object sender, ReceivedEventArgs e)
		{
			InquiryResponse res = (InquiryResponse)e.Message;
			InquiredAsyncResult ar = RemoveFromRetryList (res.MessageId, e.RemoteEndPoint);
			if (ar == null)
				return;
			ar.Complete (res.Response, this);
			if (InquirySuccess == null)
				return;
			try {
				InquirySuccess (this, new InquiredEventArgs (ar.Request, res.Response, e.RemoteEndPoint, DateTime.Now - ar.TransmitTime, ar.RetryCount));
			} catch {}
		}

		InquiredAsyncResult RemoveFromRetryList (MsgLabel id, EndPoint ep)
		{
			MessageIdentity mid = new MessageIdentity (ep, id);
			lock (_retryList) {
				for (int i = 0; i < _retryList.Count; i++) {
					InquiredAsyncResult ar = _retryList[i];
					if (mid.Equals (ar.MessageId)) {
						_retryList.RemoveAt (i);
						return ar;
					}
				}
			}
			return null;
		}

		#region Timeout Check
		void CheckTimeout ()
		{
			List<InquiredAsyncResult> timeoutList = new List<InquiredAsyncResult> ();
			DateTime now = DateTime.Now;
			lock (_retryList) {
				for (int i = 0; i < _retryList.Count; i++) {
					if (_retryList[i].Expiry <= now) {
						timeoutList.Add (_retryList[i]);
						if (_retryList[i].RetryCount >= _retryList[i].MaxRetries) {
							_retryList.RemoveAt (i);
							i--;
						}
					}
				}
			}
			for (int i = 0; i < timeoutList.Count; i++) {
				InquiredAsyncResult iar = timeoutList[i];
				if (iar.RetryCount >= iar.MaxRetries) {
					iar.Fail ();
					if (InquiryFailure != null) {
						try {
							InquiryFailure (this, new InquiredEventArgs (iar.Request, iar.RemoteEndPoint));
						} catch {}
					}
				} else {
					iar.Retry (_sock);
				}
			}
		}
		#endregion

		#region ISocket Members
		public ISocket Accept ()
		{
			throw new NotSupportedException ();
		}

		public void Bind (EndPoint localEP)
		{
			throw new NotSupportedException ();
		}

		public void Connect (EndPoint remoteEP)
		{
			throw new NotSupportedException ();
		}

		public void Send (object message)
		{
			_sock.Send (message);
		}

		public void SendTo (object message, EndPoint remoteEP)
		{
			_sock.SendTo (message, remoteEP);
		}

		public EventHandlers<Type, ReceivedEventArgs> Received {
			get { return _sock.Received; }
		}

		public void Close ()
		{
			lock (this) {
				if (!_active)
					return;
				_active = false;
			}
			if (_ownSocket)
				_sock.Close ();
			_inquired.Clear ();
			_retryList.Clear ();
			_sock.Received.Remove (typeof (InquiryRequest), Received_InquiryRequest);
			_sock.Received.Remove (typeof (InquiryResponse), Received_InquiryResponse);
			_int.RemoveInterruption (CheckTimeout);
		}

		public EndPoint LocalEndPoint {
			get { return _sock.LocalEndPoint; }
		}

		public EndPoint RemoteEndPoint {
			get { return _sock.RemoteEndPoint; }
		}

		#endregion

		#region IInquirySocket Members

		public IAsyncResult BeginInquire (object obj, EndPoint remoteEP, AsyncCallback callback, object state)
		{
			return BeginInquire (obj, remoteEP, _rto, _maxRetries, callback, state);
		}

		public IAsyncResult BeginInquire (object obj, EndPoint remoteEP, IRTOAlgorithm rto, int retries, AsyncCallback callback, object state)
		{
			InquiryRequest req = new InquiryRequest (ThreadSafeRandom.NextUInt32 (), obj);
			InquiredAsyncResult ar = new InquiredAsyncResult (req, remoteEP, rto.GetRTO (remoteEP), retries, callback, state);
			ar.Transmit (_sock);
			Interlocked.Increment (ref _numInquiries);

			InquiredAsyncResult overflow = null;
			lock (_retryList) {
				if (_retryList.Count > _retryListSize) {
					overflow = _retryList[0];
					_retryList.RemoveAt (0);
				}
				_retryList.Add (ar);
			}
			if (overflow != null) {
				Logger.Log (LogLevel.Error, this, "Overflow Retry Buffer");

				/// TODO: Thread ?
				overflow.Fail ();
				if (InquiryFailure != null) {
					try {
						InquiryFailure (this, new InquiredEventArgs (overflow.Request, overflow.RemoteEndPoint));
					} catch {}
				}
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

		public void RespondToInquiry (InquiredEventArgs e, object response)
		{
			InquiredResponseState state = e as InquiredResponseState;
			if (state == null)
				throw new ArgumentException ();
			InquiryResponse res = new InquiryResponse (state.MessageIdentity.MessageLabel, response);
			if (e.EndPoint == null)
				_sock.Send (res);
			else
				_sock.SendTo (res, e.EndPoint);
		}

		public EventHandlers<Type, InquiredEventArgs> Inquired {
			get { return _inquired; }
		}

		public event EventHandler<InquiredEventArgs> InquiryFailure;

		public event EventHandler<InquiredEventArgs> InquirySuccess;

		#endregion

		#region IDisposable Members

		public void Dispose ()
		{
			Close ();
		}

		#endregion

		#region Internal Classes
		[SerializableTypeId (0x100)]
		sealed class InquiryRequest
		{
			[SerializableFieldId (0)]
			MsgLabel _id;

			[SerializableFieldId (1)]
			object _req;

			public InquiryRequest (MsgLabel id, object request)
			{
				_id = id;
				_req = request;
			}

			public MsgLabel MessageId {
				get { return _id; }
			}

			public object Request {
				get { return _req; }
			}
		}

		[SerializableTypeId (0x101)]
		sealed class InquiryResponse
		{
			[SerializableFieldId (0)]
			MsgLabel _id;

			[SerializableFieldId (1)]
			object _res;

			public InquiryResponse (MsgLabel id, object response)
			{
				_id = id;
				_res = response;
			}

			public MsgLabel MessageId {
				get { return _id; }
			}

			public object Response {
				get { return _res; }
			}
		}
		sealed class InquiredAsyncResult : IAsyncResult
		{
			InquiryRequest _req;
			object _res;
			EndPoint _remoteEP;
			TimeSpan _timeout;
			int _retries = 0, _maxRetries;
			AsyncCallback _callback;
			object _state;
			bool _isCompleted = false;
			ManualResetEvent _waitHandle = new ManualResetEvent (false);
			DateTime _dt = DateTime.MinValue, _expiry;
			MessageIdentity _mid;

			public InquiredAsyncResult (InquiryRequest req, EndPoint remoteEP, TimeSpan timeout, int maxRetry, AsyncCallback callback, object state)
			{
				_req = req;
				_remoteEP = remoteEP;
				_callback = callback;
				_state = state;
				_timeout = timeout;
				_maxRetries = maxRetry;
				_mid = new MessageIdentity (remoteEP, req.MessageId);
			}

			public void Transmit (ISocket sock)
			{
				if (_dt == DateTime.MinValue)
					_dt = DateTime.Now;
				_expiry = DateTime.Now + _timeout;
				if (_remoteEP == null) {
					sock.Send (_req);
				} else {
					sock.SendTo (_req, _remoteEP);
				}
			}

			public void Retry (ISocket sock)
			{
				_retries++;
				Transmit (sock);
				Logger.Log (LogLevel.Trace, sock, "Retry {0} to {1}", _req.GetType (), _remoteEP);
			}

			public void Complete (object res, InquirySocket sock)
			{
				if (_retries > 0 && sock != null)
					Interlocked.Add (ref sock._numReInquiries, _retries);
				_res = res;
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
				Complete (null, null);
			}

			#region Properties
			public InquiryRequest Request {
				get { return _req; }
			}

			public object Response {
				get { return _res; }
			}

			public DateTime TransmitTime {
				get { return _dt; }
			}

			public DateTime Expiry {
				get { return _expiry; }
			}

			public int RetryCount {
				get { return _retries; }
			}

			public EndPoint RemoteEndPoint {
				get { return _remoteEP; }
			}

			public TimeSpan Timeout {
				get { return _timeout; }
			}

			public int MaxRetries {
				get { return _maxRetries; }
			}

			public MessageIdentity MessageId {
				get { return _mid; }
			}
			#endregion

			#region IAsyncResult Members

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

			#endregion
		}
		sealed class InquiredResponseState : InquiredEventArgs
		{
			bool _sentFlag = false;
			MessageIdentity _mid;

			public InquiredResponseState (object inq, EndPoint ep, MsgLabel id) : base (inq, ep)
			{
				_mid = new MessageIdentity (ep, id);
			}

			public bool SentFlag {
				get { return _sentFlag; }
				set { _sentFlag = value;}
			}

			 public MessageIdentity MessageIdentity {
				get { return _mid; }
			}
		}
		sealed class MessageIdentity : IEquatable<MessageIdentity>
		{
			EndPoint _sender;
			MsgLabel _msgId;
			int _hash;

			public MessageIdentity (EndPoint sender, MsgLabel msgId)
			{
				_sender = sender;
				_msgId = msgId;
				_hash = _sender.GetHashCode () ^ _msgId.GetHashCode ();
			}

			public MsgLabel MessageLabel {
				get { return _msgId; }
			}

			public override int GetHashCode()
			{
				return _hash;
			}

			public bool Equals (MessageIdentity other)
			{
				return this._msgId == other._msgId && this._sender.Equals (other._sender);
			}
		}
		#endregion
	}
}
