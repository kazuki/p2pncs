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
		int _maxRetries, _retryListSize;
		protected IDatagramEventSocket _sock;
		bool _ownSocket, _active = true;
		List<InquiredAsyncResultBase> _retryList = new List<InquiredAsyncResultBase> ();
		IntervalInterrupter _interrupter;
		KeyValueCache<MessageIdentity, object> _inquiryDupCheckCache;
		HashSet<Type> _inquiryDupCheckTypeSet = new HashSet<Type> ();
		EventHandlers<Type, InquiredEventArgs> _inquiredHandlers = new EventHandlers<Type, InquiredEventArgs> ();
		EventHandlers<Type, ReceivedEventArgs> _receivedHandlers = new EventHandlers<Type, ReceivedEventArgs> ();
		long _numInq = 0, _numReinq = 0;

		protected IRTOAlgorithm _rtoAlgo;

		public event EventHandler<InquiredEventArgs> InquirySuccess;
		public event EventHandler<InquiredEventArgs> InquiryFailure;

		protected MessagingSocketBase (IDatagramEventSocket sock, bool ownSocket, IntervalInterrupter interrupter,
			IRTOAlgorithm rtoAlgo, int defaultMaxRetries, int retryListSize, int inquiryDupCheckSize)
		{
			_sock = sock;
			_ownSocket = ownSocket;
			_interrupter = interrupter;
			_rtoAlgo = rtoAlgo;
			_maxRetries = defaultMaxRetries;
			_retryListSize = retryListSize;
			_inquiryDupCheckCache = new KeyValueCache<MessageIdentity,object> (inquiryDupCheckSize);

			interrupter.AddInterruption (TimeoutCheck);
		}

		#region IMessagingSocket Members

		public abstract void Send (object obj, EndPoint remoteEP);

		public IAsyncResult BeginInquire (object obj, EndPoint remoteEP, AsyncCallback callback, object state)
		{
			return BeginInquire (obj, remoteEP, _rtoAlgo.GetRTO (remoteEP), _maxRetries, callback, state);
		}

		protected virtual IAsyncResult BeginInquire (object obj, EndPoint remoteEP, TimeSpan timeout, int maxRetry, AsyncCallback callback, object state)
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
				ThreadPool.QueueUserWorkItem (delegate (object o) {
					overflow.Fail ();
					InvokeInquiryFailure (this, new InquiredEventArgs (overflow.Request, overflow.RemoteEndPoint));
				});
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

		public void AddInquiryDuplicationCheckType (Type type)
		{
			lock (_inquiryDupCheckTypeSet) {
				_inquiryDupCheckTypeSet.Add (type);
			}
		}

		public void RemoveInquiryDuplicationCheckType (Type type)
		{
			lock (_inquiryDupCheckTypeSet) {
				_inquiryDupCheckTypeSet.Remove (type);
			}
		}

		public EventHandlers<Type, ReceivedEventArgs> ReceivedHandlers {
			get { return _receivedHandlers; }
		}

		public EventHandlers<Type, InquiredEventArgs> InquiredHandlers {
			get { return _inquiredHandlers; }
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
			_inquiredHandlers.Clear ();
			_receivedHandlers.Clear ();
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
			lock (_inquiryDupCheckTypeSet) {
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

			try {
				if (e.InquireMessage == null)
					_inquiredHandlers.UnknownKeyHandler (sender, e);
				else
					_inquiredHandlers.Invoke (e.InquireMessage.GetType (), sender, e);
			} catch {}
		}

		protected void InvokeInquirySuccess (object sender, InquiredEventArgs e)
		{
			if (e.Retries == 0)
				_rtoAlgo.AddSample (e.EndPoint, e.RTT);
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
			try {
				if (e.Message == null)
					_receivedHandlers.UnknownKeyHandler (sender, e);
				else
					_receivedHandlers.Invoke (e.Message.GetType (), sender, e);
			} catch {}
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
