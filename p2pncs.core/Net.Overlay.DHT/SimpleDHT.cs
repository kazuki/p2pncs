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
using System.Linq;
using System.Text;
using System.Threading;

namespace p2pncs.Net.Overlay.DHT
{
	public class SimpleDHT : IDistributedHashTable
	{
		IKeyBasedRouter _kbr;
		IMessagingSocket _sock;
		ILocalHashTable _lht;
		int _numOfReplicas = 3, _numOfSimultaneous = 3;

		public SimpleDHT (IKeyBasedRouter kbr, IMessagingSocket sock, ILocalHashTable lht)
		{
			_kbr = kbr;
			_sock = sock;
			_lht = lht;
			sock.Inquired += new InquiredEventHandler (MessagingSocket_Inquired);
		}

		void MessagingSocket_Inquired (object sender, InquiredEventArgs e)
		{
			GetRequest getReq = e.InquireMessage as GetRequest;
			PutRequest putReq = e.InquireMessage as PutRequest;
			if (getReq == null && putReq == null)
				return;

			if (getReq != null) {
				_sock.StartResponse (e, new GetResponse (_lht.Get (getReq.Key)));
			} else {
				_lht.Put (putReq.Key, DateTime.Now + putReq.LifeTime, putReq.Value);
			}
		}

		#region IDistributedHashTable Members

		public IAsyncResult BeginGet (Key key, AsyncCallback callback, object state)
		{
			AsyncResult ar = new AsyncResult (key, this, callback, state);
			return ar;
		}

		public GetResult EndGet (IAsyncResult ar)
		{
			ar.AsyncWaitHandle.WaitOne ();
			return ((AsyncResult)ar).Result;
		}

		public IAsyncResult BeginPut (Key key, TimeSpan lifeTime, object value, AsyncCallback callback, object state)
		{
			return new AsyncResult (key, value, lifeTime, this, callback, state);
		}

		public void EndPut (IAsyncResult ar)
		{
			ar.AsyncWaitHandle.WaitOne ();
		}

		public IKeyBasedRouter KeyBasedRouter {
			get { return _kbr; }
		}

		#endregion

		#region Properties
		public int NumberOfReplicas {
			get { return _numOfReplicas; }
			set { _numOfReplicas = value;}
		}

		public int NumberOfSimultaneous {
			get { return _numOfSimultaneous; }
			set { _numOfSimultaneous = value;}
		}

		public IMessagingSocket MessagingSocket {
			get { return _sock; }
		}

		public ILocalHashTable LocalHashTable {
			get { return _lht; }
		}
		#endregion

		#region Internal Classes
		class AsyncResult : IAsyncResult
		{
			bool _getMethod;
			SimpleDHT _dht;
			IAsyncResult _kbrAR;
			Key _key;
			AsyncCallback _callback;
			object _state;
			ManualResetEvent _done = new ManualResetEvent (false);
			bool _completed = false;
			GetResult _result = null;
			DateTime _start;
			PutRequest _putReq = null;
			int _hops, _getInquires = 0;
			object _getLock = null;

			public AsyncResult (Key key, SimpleDHT dht, AsyncCallback callback, object state)
			{
				_callback = callback;
				_state = state;
				_dht = dht;
				_key = key;
				_start = DateTime.Now;
				_getMethod = true;
				_getLock = new object ();
				_kbrAR = dht.KeyBasedRouter.BeginRoute (key, null, dht.NumberOfReplicas, dht.NumberOfSimultaneous, KBR_Callback, null);
			}

			public AsyncResult (Key key, object value, TimeSpan lifetime, SimpleDHT dht, AsyncCallback callback, object state)
			{
				_callback = callback;
				_state = state;
				_dht = dht;
				_key = key;
				_start = DateTime.Now;
				_getMethod = false;
				_putReq = new PutRequest (key, value, lifetime);
				_kbrAR = dht.KeyBasedRouter.BeginRoute (key, null, dht.NumberOfReplicas, dht.NumberOfSimultaneous, KBR_Callback, null);
			}

			void KBR_Callback (IAsyncResult ar)
			{
				RoutingResult rr = _dht.KeyBasedRouter.EndRoute (ar);
				if (rr.RootCandidates == null || rr.RootCandidates.Length == 0) {
					//TimeSpan time = DateTime.Now.Subtract (_start);
					_result = new GetResult (_key, null, rr.Hops);
					Done ();
					return;
				}
				_hops = rr.Hops;

				int count;
				object msg;
				AsyncCallback callback;
				if (_getMethod) {
					count = Math.Min (_dht.NumberOfReplicas, rr.RootCandidates.Length);
					msg = new GetRequest (_key);
					callback = GetRequest_Callback;
				} else {
					count = Math.Min (_dht.NumberOfReplicas, rr.RootCandidates.Length);
					msg = _putReq;
					callback = PutRequest_Callback;
				}

				_getInquires = count;
				for (int i = 0; i < count; i ++) {
					if (rr.RootCandidates[i].EndPoint == null) {
						if (_getMethod) {
							lock (_getLock) {
								_getInquires --;
								object ret = _dht.LocalHashTable.Get (_key);
								if (ret != null || _getInquires == 0) {
									_result = new GetResult (_key, ret, rr.Hops);
									Done ();
									return;
								} else {
								}
							}
						} else {
							_dht.LocalHashTable.Put (_putReq.Key, DateTime.Now + _putReq.LifeTime, _putReq.Value);
						}
					} else {
						_dht.MessagingSocket.BeginInquire (msg, rr.RootCandidates[i].EndPoint, callback, null);
					}
				}
			}

			void GetRequest_Callback (IAsyncResult ar)
			{
				GetResponse res = _dht.MessagingSocket.EndInquire (ar) as GetResponse;
				lock (_getLock) {
					if (_result != null)
						return;
					_getInquires --;
					if (res == null || res.Value == null) {
						if (_getInquires != 0)
							return;
						_result = new GetResult (_key, null, _hops);
					} else {
						_result = new GetResult (_key, res.Value, _hops);
					}
				}
				Done ();
			}

			void PutRequest_Callback (IAsyncResult ar)
			{
				_dht.MessagingSocket.EndInquire (ar);
				_result = null;
				Done ();
			}

			void Done ()
			{
				_completed = true;
				_done.Set ();
				if (_callback != null) {
					try {
						_callback (this);
					} catch {}
				}
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

			public GetResult Result {
				get { return _result; }
			}
		}

		[Serializable]
		class GetRequest
		{
			Key _key;

			public GetRequest (Key key)
			{
				_key = key;
			}

			public Key Key {
				get { return _key; }
			}
		}

		[Serializable]
		class GetResponse
		{
			object _value;

			public GetResponse (object value)
			{
				_value = value;
			}

			public object Value {
				get { return _value; }
			}
		}

		[Serializable]
		class PutRequest
		{
			Key _key;
			object _value;
			TimeSpan _lifetime;

			public PutRequest (Key key, object value, TimeSpan lifetime)
			{
				_key = key;
				_value = value;
				_lifetime = lifetime;
			}

			public Key Key {
				get { return _key; }
			}

			public object Value {
				get { return _value; }
			}

			public TimeSpan LifeTime {
				get { return _lifetime; }
			}
		}

		[Serializable]
		class PutResponse
		{
			public PutResponse ()
			{
			}
		}
		#endregion
	}
}
