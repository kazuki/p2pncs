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
using System.Net;
using System.Collections.Generic;
using System.Threading;
using p2pncs.Threading;
using p2pncs.Net;
using openCrypto;

namespace p2pncs.Simulation.VirtualNet
{
	public class VirtualMessagingSocket : IMessagingSocket
	{
		TimeSpan _inquiryTimeout;
		int _maxRetries, _retryListSize;
		List<InquiredAsyncResult> _retryList = new List<InquiredAsyncResult> ();
		IntervalInterrupter _interrupter;
		VirtualNetwork _vnet;
		IPEndPoint _srcEP;
		VirtualDatagramEventSocket _sock;
		bool _ownSocket, _active = true;

		public VirtualMessagingSocket (VirtualNetwork vnet,
			VirtualDatagramEventSocket baseSock, bool ownSocket,
			IntervalInterrupter interrupter,
			TimeSpan timeout, int maxRetry, int retryBufferSize)
		{
			_vnet = vnet;
			_sock = baseSock;
			_srcEP = baseSock.BindEndPoint;
			_ownSocket = ownSocket;
			_interrupter = interrupter;
			_inquiryTimeout = timeout;
			_maxRetries = maxRetry;
			_retryListSize = retryBufferSize;
			vnet.AddVirtualMessagingSocketToVirtualNode (baseSock, this);
			interrupter.AddInterruption (TimeoutCheck);
		}

		internal void Received (IPEndPoint remoteEP, object obj)
		{
			if (!_active)
				return;

			if (obj is RequestWrapper) {
				RequestWrapper req = (RequestWrapper)obj;
				InquiredEventArgs args = new InquiredResponseState (req.Message, remoteEP, req.ID);
				if (Inquired != null) {
					try {
						Inquired (this, args);
					} catch {}
				}
				if (!(args as InquiredResponseState).SentFlag)
					throw new ApplicationException ();
			} else if (obj is ResponseWrapper) {
				ResponseWrapper res = (ResponseWrapper)obj;
				InquiredAsyncResult ar = RemoveFromRetryList (res.ID, remoteEP.Address);
				if (ar == null)
					return;
				ar.Complete (res.Message);
				if (InquirySuccess != null) {
					InquirySuccess (this, new InquiredEventArgs (ar.Request, res.Message, remoteEP));
				}
			}
		}

		public void StartResponse (InquiredEventArgs args, object response, bool throwWhenAlreadySent)
		{
			InquiredResponseState state = args as InquiredResponseState;
			if (state == null)
				throw new ArgumentException ();
			if (state.SentFlag) {
				if (throwWhenAlreadySent)
					throw new ApplicationException ();
				return;
			}
			state.SentFlag = true;
			_vnet.AddSendQueue (_srcEP, (IPEndPoint)state.EndPoint, new ResponseWrapper (response, state.ID));
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
					iar.Retry (_vnet, _inquiryTimeout, _srcEP);
				}
			}
		}

		#region IMessagingSocket Members

		public IAsyncResult BeginInquire (object obj, EndPoint remoteEP, AsyncCallback callback, object state)
		{
			if (obj == null || remoteEP == null)
				throw new ArgumentNullException ();

			ushort id = CreateMessageID ();
			InquiredAsyncResult ar = new InquiredAsyncResult (obj, (IPEndPoint)remoteEP, id, callback, state);
			ar.Transmit (_vnet, _inquiryTimeout, _srcEP);

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

		public event InquiredEventHandler Inquired;

		public event InquiredEventHandler InquiryFailure;

		public event InquiredEventHandler InquirySuccess;

		public IDatagramEventSocket BaseSocket {
			get { return _sock; }
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

		#endregion

		#region Misc
		ushort CreateMessageID ()
		{
			return BitConverter.ToUInt16 (RNG.GetRNGBytes (4), 0);
		}

		InquiredAsyncResult RemoveFromRetryList (ushort id, IPAddress adrs)
		{
			lock (_retryList) {
				for (int i = 0; i < _retryList.Count; i++) {
					InquiredAsyncResult ar = _retryList[i];
					if (ar.ID == id && ar.RemoteEndPoint.Address.Equals (adrs)) {
						_retryList.RemoveAt (i);
						return ar;
					}
				}
			}
			return null;
		}
		#endregion

		#region Internal Class
		class RequestWrapper
		{
			object _msg;
			ushort _id;

			public RequestWrapper (object msg, ushort id)
			{
				_msg = msg;
				_id = id;
			}

			public object Message {
				get { return _msg; }
			}

			public ushort ID {
				get { return _id; }
			}
		}
		class ResponseWrapper
		{
			object _msg;
			ushort _id;

			public ResponseWrapper (object msg, ushort id)
			{
				_msg = msg;
				_id = id;
			}

			public object Message {
				get { return _msg; }
			}

			public ushort ID {
				get { return _id; }
			}
		}
		class InquiredAsyncResult : IAsyncResult
		{
			IPEndPoint _remoteEP;
			object _state;
			AsyncCallback _callback;
			ManualResetEvent _waitHandle = new ManualResetEvent (false);
			object _req, _response = null;
			bool _isCompleted = false;
			DateTime _dt, _timeout;
			ushort _id;
			int _retries = 0;

			public InquiredAsyncResult (object req, IPEndPoint remoteEP, ushort id, AsyncCallback callback, object state)
			{
				_req = req;
				_remoteEP = remoteEP;
				_callback = callback;
				_state = state;
				_id = id;
			}

			public void Transmit (VirtualNetwork vnet, TimeSpan timeout, IPEndPoint srcEP)
			{
				_dt = DateTime.Now;
				_timeout = _dt + timeout;
				vnet.AddSendQueue (srcEP, _remoteEP, new RequestWrapper (_req, _id));
			}

			public void Retry (VirtualNetwork vnet, TimeSpan timeout, IPEndPoint srcEP)
			{
				_retries++;
				Transmit (vnet, timeout, srcEP);
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

			public IPEndPoint RemoteEndPoint {
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

			public InquiredResponseState (object inq, IPEndPoint ep, ushort id) : base (inq, ep)
			{
				_id = id;
			}

			public ushort ID {
				get { return _id; }
			}

			public bool SentFlag {
				get { return _sentFlag; }
				set { _sentFlag = value;}
			}
		}
		#endregion
	}
}
