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
using p2pncs.Utility;
using MsgLabel = System.UInt32;

namespace p2pncs.Net
{
	public abstract class MessagingSocketBase : IMessagingSocket
	{
		TimeSpan _inquiryTimeout;
		int _maxRetries, _retryListSize;
		protected IDatagramEventSocket _sock;
		bool _ownSocket, _active = true;
		List<InquiredAsyncResultBase> _retryList = new List<InquiredAsyncResultBase> ();
		IntervalInterrupter _interrupter;
		KeyValueCache<MessageIdentity, object> _inquiryDupCheckCache;
		HashSet<Type> _inquiryDupCheckTypeSet = new HashSet<Type> ();
		ReaderWriterLockWrapper _inquiryDupCheckTypeSetLock = new ReaderWriterLockWrapper ();

		ReaderWriterLockWrapper _inquiredHandlersLock = new ReaderWriterLockWrapper ();
		Dictionary<Type, InquiredEventHandler> _inquiredHandlers = new Dictionary<Type, InquiredEventHandler> ();
		long _numInq = 0, _numReinq = 0;

		ReaderWriterLockWrapper _receivedHandlersLock = new ReaderWriterLockWrapper ();
		Dictionary<Type, ReceivedEventHandler> _receivedHandlers = new Dictionary<Type, ReceivedEventHandler> ();

		public event ReceivedEventHandler ReceivedUnknownMessage;
		public event InquiredEventHandler InquiredUnknownMessage;
		public event InquiredEventHandler InquirySuccess;
		public event InquiredEventHandler InquiryFailure;

		protected MessagingSocketBase (IDatagramEventSocket sock, bool ownSocket, IntervalInterrupter interrupter,
			TimeSpan defaultInquiryTimeout, int defaultMaxRetries, int retryListSize, int inquiryDupCheckSize)
		{
			_sock = sock;
			_ownSocket = ownSocket;
			_interrupter = interrupter;
			_inquiryTimeout = defaultInquiryTimeout;
			_maxRetries = defaultMaxRetries;
			_retryListSize = retryListSize;
			_inquiryDupCheckCache = new KeyValueCache<MessageIdentity,object> (inquiryDupCheckSize);

			interrupter.AddInterruption (TimeoutCheck);
		}

		#region IMessagingSocket Members

		public abstract void Send (object obj, EndPoint remoteEP);

		public IAsyncResult BeginInquire (object obj, EndPoint remoteEP, AsyncCallback callback, object state)
		{
			return BeginInquire (obj, remoteEP, _inquiryTimeout, _maxRetries, callback, state);
		}

		public virtual IAsyncResult BeginInquire (object obj, EndPoint remoteEP, TimeSpan timeout, int maxRetry, AsyncCallback callback, object state)
		{
			MsgLabel id = CreateMessageID ();
			InquiredAsyncResultBase ar = CreateInquiredAsyncResult (id, obj, remoteEP, timeout, maxRetry, callback, state);
			ar.Transmit (_sock);
			Interlocked.Increment (ref _numInq);

			InquiredAsyncResultBase overflow = null;
			lock (_retryList) {
				if (_retryList.Count > _retryListSize) {
					overflow = _retryList[0];
					_retryList.RemoveAt (0);
				}
				_retryList.Add (ar);
			}
			if (overflow != null) {
				Logger.Log (LogLevel.Error, this, "Overflow Retry Buffer");
				overflow.Fail ();
				InvokeInquiryFailure (this, new InquiredEventArgs (overflow.Request, overflow.RemoteEndPoint));
			}
			return ar;
		}

		public virtual object EndInquire (IAsyncResult iar)
		{
			InquiredAsyncResultBase ar = iar as InquiredAsyncResultBase;
			if (ar == null)
				throw new ArgumentException ();
			ar.AsyncWaitHandle.WaitOne ();
			ar.AsyncWaitHandle.Close ();
			return ar.Response;
		}

		public void StartResponse (InquiredEventArgs args, object response)
		{
			InquiredResponseState state = args as InquiredResponseState;
			if (state == null)
				throw new ArgumentException ();
			if (state.SentFlag)
				throw new ApplicationException ();
			state.SentFlag = true;
			if (IsDuplicateCheckType (args.InquireMessage))
				_inquiryDupCheckCache.SetValue (state.MessageIdentity, response);
			StartResponse_Internal (state, response);
		}
		protected abstract void StartResponse_Internal (InquiredResponseState state, object response);

		public void AddReceivedHandler (Type msgType, ReceivedEventHandler handler)
		{
			using (_receivedHandlersLock.EnterWriteLock ()) {
				ReceivedEventHandler value;
				if (_receivedHandlers.TryGetValue (msgType, out value)) {
					value += handler;
				} else {
					_receivedHandlers.Add (msgType, handler);
				}
			}
		}

		public void RemoveReceivedHandler (Type msgType, ReceivedEventHandler handler)
		{
			using (_receivedHandlersLock.EnterWriteLock ()) {
				ReceivedEventHandler value;
				if (_receivedHandlers.TryGetValue (msgType, out value)) {
					value -= handler;
					if (value == null)
						_receivedHandlers.Remove (msgType);
				}
			}
		}

		public void AddInquiredHandler (Type inquiryMessageType, InquiredEventHandler handler)
		{
			using (_inquiredHandlersLock.EnterWriteLock ()) {
				InquiredEventHandler value;
				if (_inquiredHandlers.TryGetValue (inquiryMessageType, out value)) {
					value += handler;
				} else {
					_inquiredHandlers.Add (inquiryMessageType, handler);
				}
			}
		}

		public void RemoveInquiredHandler (Type inquiryMessageType, InquiredEventHandler handler)
		{
			using (_inquiredHandlersLock.EnterWriteLock ()) {
				InquiredEventHandler value;
				if (_inquiredHandlers.TryGetValue (inquiryMessageType, out value)) {
					value -= handler;
					if (value == null)
						_inquiredHandlers.Remove (inquiryMessageType);
				}
			}
		}

		public void AddInquiryDuplicationCheckType (Type type)
		{
			using (_inquiryDupCheckTypeSetLock.EnterWriteLock ()) {
				_inquiryDupCheckTypeSet.Add (type);
			}
		}

		public void RemoveInquiryDuplicationCheckType (Type type)
		{
			using (_inquiryDupCheckTypeSetLock.EnterWriteLock ()) {
				_inquiryDupCheckTypeSet.Remove (type);
			}
		}

		public IDatagramEventSocket BaseSocket {
			get { return _sock; }
		}

		public abstract int MaxMessageSize { get; }

		public void Close ()
		{
			if (!_active)
				return;
			_active = false;
			_interrupter.RemoveInterruption (TimeoutCheck);
			if (_ownSocket && _sock != null) {
				_sock.Close ();
			}
			lock (_retryList) {
				for (int i = 0; i < _retryList.Count; i++)
					_retryList[i].Fail ();
				_retryList.Clear ();
			}
			using (_inquiredHandlersLock.EnterWriteLock ()) {
				_inquiredHandlers.Clear ();
			}
			_inquiredHandlersLock.Dispose ();
		}

		public long NumberOfInquiries {
			get { return _numInq; }
		}

		public long NumberOfReinquiries {
			get { return _numReinq; }
		}

		#endregion

		#region Timeout Check
		void TimeoutCheck ()
		{
			List<InquiredAsyncResultBase> timeoutList = new List<InquiredAsyncResultBase> ();
			DateTime now = DateTime.Now;
			lock (_retryList) {
				for (int i = 0; i < _retryList.Count; i++) {
					if (_retryList[i].Expiry <= now) {
						timeoutList.Add (_retryList[i]);
						if (_retryList[i].RetryCount >= _retryList[i].MaxRetry) {
							_retryList.RemoveAt (i);
							i--;
						}
					}
				}
			}
			for (int i = 0; i < timeoutList.Count; i++) {
				InquiredAsyncResultBase iar = timeoutList[i];
				if (iar.RetryCount >= iar.MaxRetry) {
					iar.Fail ();
					InvokeInquiryFailure (this, new InquiredEventArgs (iar.Request, iar.RemoteEndPoint));
				} else {
					iar.Retry (_sock);
				}
			}
		}
		#endregion

		#region Abstract Members
		protected abstract InquiredAsyncResultBase CreateInquiredAsyncResult (MsgLabel id, object obj, EndPoint remoteEP, TimeSpan timeout, int maxRetry, AsyncCallback callback, object state);
		#endregion

		#region Misc
		protected virtual MsgLabel CreateMessageID ()
		{
			return BitConverter.ToUInt32 (openCrypto.RNG.GetRNGBytes (4), 0);
		}

		protected InquiredAsyncResultBase RemoveFromRetryList (MsgLabel id, EndPoint ep)
		{
			lock (_retryList) {
				for (int i = 0; i < _retryList.Count; i++) {
					InquiredAsyncResultBase ar = _retryList[i];
					if (ar.ID == id && ep.Equals (ar.RemoteEndPoint)) {
						_retryList.RemoveAt (i);
						return ar;
					}
				}
			}
			return null;
		}

		protected bool IsActive {
			get { return _active; }
		}

		bool IsDuplicateCheckType (object msg)
		{
			if (msg == null)
				return false;
			using (_inquiryDupCheckTypeSetLock.EnterReadLock ()) {
				return _inquiryDupCheckTypeSet.Contains (msg.GetType ());
			}
		}

		protected void InvokeInquired (object sender, InquiredEventArgs e)
		{
			object value;
			if (IsDuplicateCheckType (e.InquireMessage) && !_inquiryDupCheckCache.CheckAndReserve ((e as InquiredResponseState).MessageIdentity, out value)) {
				Logger.Log (LogLevel.Trace, this, "Responsed {0} from Cache to {1}", value == null ? "null" : value.GetType ().ToString (), e.EndPoint);
				StartResponse (e, value);
				return;
			}

			InquiredEventHandler handler;
			using (_inquiredHandlersLock.EnterReadLock ()) {
				if (e.InquireMessage == null || !_inquiredHandlers.TryGetValue (e.InquireMessage.GetType (), out handler))
					handler = InquiredUnknownMessage;
			}
			if (handler != null) {
				try {
					handler (sender, e);
				} catch {}
			}
		}

		protected void InvokeInquirySuccess (object sender, InquiredEventArgs e)
		{
			if (InquirySuccess != null) {
				try {
					InquirySuccess (sender, e);
				} catch {}
			}
		}

		protected void InvokeInquiryFailure (object sender, InquiredEventArgs e)
		{
			if (InquiryFailure != null) {
				try {
					InquiryFailure (sender, e);
				} catch {}
			}
		}

		protected void InvokeReceived (object sender, ReceivedEventArgs e)
		{
			ReceivedEventHandler handler;
			using (_receivedHandlersLock.EnterReadLock ()) {
				if (e.Message == null || !_receivedHandlers.TryGetValue (e.Message.GetType (), out handler))
					handler = ReceivedUnknownMessage;
			}
			if (handler != null) {
				try {
					handler (this, e);
				} catch {}
			}
		}
		#endregion

		#region IDisposable Members

		public void Dispose ()
		{
			Close ();
		}

		#endregion

		#region Internal Classes
		protected abstract class InquiredAsyncResultBase : IAsyncResult
		{
			protected EndPoint _remoteEP;
			object _state;
			AsyncCallback _callback;
			ManualResetEvent _waitHandle = new ManualResetEvent (false);
			protected object _req;
			object _response = null;
			bool _isCompleted = false;
			DateTime _dt = DateTime.MinValue, _expiry;
			TimeSpan _timeout;
			protected MsgLabel _id;
			int _retries = 0, _maxRetry;

			public InquiredAsyncResultBase (object req, EndPoint remoteEP, MsgLabel id, TimeSpan timeout, int maxRetry, AsyncCallback callback, object state)
			{
				_req = req;
				_remoteEP = remoteEP;
				_callback = callback;
				_state = state;
				_id = id;
				_timeout = timeout;
				_maxRetry = maxRetry;
			}

			public void Transmit (IDatagramEventSocket sock)
			{
				if (_dt == DateTime.MinValue)
					_dt = DateTime.Now;
				_expiry = DateTime.Now + _timeout;
				Transmit_Internal (sock);
			}

			protected abstract void Transmit_Internal (IDatagramEventSocket sock);

			public void Retry (IDatagramEventSocket sock)
			{
				_retries++;
				Transmit (sock);
				Logger.Log (LogLevel.Trace, sock, "Retry {0} to {1}", _req.GetType (), _remoteEP);
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

			public DateTime Expiry {
				get { return _expiry; }
			}

			public MsgLabel ID {
				get { return _id; }
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

			public int MaxRetry {
				get { return _maxRetry; }
			}

			public void Complete (object obj, MessagingSocketBase ms)
			{
				if (_retries > 0 && ms != null)
					Interlocked.Add (ref ms._numReinq, _retries);
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
				Complete (null, null);
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
		protected class InquiredResponseState : InquiredEventArgs
		{
			MsgLabel _id;
			bool _sentFlag = false;
			MessageIdentity _mid;

			public InquiredResponseState (object inq, EndPoint ep, MsgLabel id) : base (inq, ep)
			{
				_id = id;
				_mid = new MessageIdentity (ep, id);
			}

			public MsgLabel ID {
				get { return _id; }
			}

			public bool SentFlag {
				get { return _sentFlag; }
				set { _sentFlag = value;}
			}

			 public MessageIdentity MessageIdentity {
				get { return _mid; }
			}
		}
		protected class MessageIdentity : IEquatable<MessageIdentity>
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

			public override int GetHashCode()
			{
				return _hash;
			}

			#region IEquatable<MessageIdentity> Members

			public bool Equals (MessageIdentity other)
			{
				return this._msgId == other._msgId && this._sender.Equals (other._sender);
			}

			#endregion
		}
		#endregion
	}
}
