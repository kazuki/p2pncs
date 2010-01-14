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
using System.Collections.Generic;
using System.Net;
using System.Threading;
using p2pncs.Threading;
using p2pncs.Utility;

namespace p2pncs.Net.Overlay.DHT
{
	public class SimpleDHT : IDistributedHashTable
	{
		IInquirySocket _sock;
		IKeyBasedRouter _kbr;
		ILocalHashTable _local;
		ValueTypeRegister _typeReg;
		int _numOfReplicas = 3, _numOfSimultaneous = 3;

		public SimpleDHT (IInquirySocket sock, IKeyBasedRouter kbr, ILocalHashTable localStore, ValueTypeRegister typeReg)
		{
			_sock = sock;
			_kbr = kbr;
			_local = localStore;
			_typeReg = typeReg;
			sock.Inquired.Add (typeof (GetRequest), Inquired_GetRequest);
			sock.Inquired.Add (typeof (PutRequest), Inquired_PutRequest);
		}

		#region IDistributedHashTable Members

		public IAsyncResult BeginGet<T> (Key appId, Key key, GetOptions opts, AsyncCallback callback, object state)
		{
			return new GetAsyncResult<T> (this, appId, key, _typeReg[typeof (T)].ID, opts, callback, state);
		}

		public GetResult<T> EndGet<T> (IAsyncResult ar)
		{
			GetAsyncResult<T> gar = (GetAsyncResult<T>)ar;
			gar.AsyncWaitHandle.WaitOne ();
			return gar.Result;
		}

		public IAsyncResult BeginPut (Key appId, Key key, TimeSpan lifeTime, object value, AsyncCallback callback, object state)
		{
			return new PutAsyncResult (this, appId, key, _typeReg[value.GetType()].ID, lifeTime, value, callback, state);
		}

		public void EndPut (IAsyncResult ar)
		{
			ar.AsyncWaitHandle.WaitOne ();
		}

		#endregion

		#region IDisposable Members

		public void Dispose ()
		{
			_sock.Inquired.Remove (typeof (GetRequest), Inquired_GetRequest);
			_sock.Inquired.Remove (typeof (PutRequest), Inquired_PutRequest);
		}

		#endregion

		#region Properties
		public int NumberOfReplica {
			get { return _numOfReplicas; }
			set { _numOfReplicas = value;}
		}

		public int NumberOfSimultaneous {
			get { return _numOfSimultaneous; }
			set { _numOfSimultaneous = value;}
		}
		#endregion

		#region AsyncResult
		abstract class AsyncResultBase : IAsyncResult
		{
			protected SimpleDHT _dht;
			protected Key _appId, _key;
			protected int _typeId;
			protected AsyncCallback _callback;
			protected object _state;
			protected EventWaitHandle _done = new ManualResetEvent (false);
			protected bool _completed = false;
			protected DateTime _start;

			protected AsyncResultBase (SimpleDHT dht, Key appId, Key key, int typeId, AsyncCallback callback, object state)
			{
				_dht = dht;
				_appId = appId;
				_key = key;
				_typeId = typeId;
				_callback = callback;
				_state = state;
				_start = DateTime.Now;
			}

			public void Done ()
			{
				lock (_done) {
					if (_completed)
						return;
					_completed = true;
				}
				_done.Set ();
				if (_callback != null) {
					try {
						_callback (this);
					} catch { }
				}
			}

			#region IAsyncResult Members

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

			#endregion
		}
		sealed class GetAsyncResult<T> : AsyncResultBase
		{
			GetOptions _opts;
			int _waiting = 0, _hops = -1;
			GetResult<T> _result = null;
			List<T> _list = new List<T> ();

			public GetAsyncResult (SimpleDHT dht, Key appId, Key key, int typeId, GetOptions opts, AsyncCallback callback, object state)
				: base (dht, appId, key, typeId, callback, state)
			{
				_opts = (opts == null ? new GetOptions () : opts);
				_dht._kbr.BeginRoute (appId, key, _dht.NumberOfReplica, new KeyBasedRoutingOptions {NumberOfSimultaneous = dht.NumberOfSimultaneous}, KBR_Callback, null);
			}

			void KBR_Callback (IAsyncResult ar)
			{
				RoutingResult result = _dht._kbr.EndRoute (ar);
				GetRequest req = new GetRequest (_key, _typeId, _opts.MaxValues);
				if (result.RootCandidates.Length == 0) {
					_result = new GetResult<T> (_key, new T[0], _hops);
					Done ();
					return;
				}
				_hops = result.Hops;
				for (int i = 0; i < result.RootCandidates.Length; i++) {
					Interlocked.Increment (ref _waiting);
					_dht._sock.BeginInquire (req, result.RootCandidates[i].EndPoint, GetReqCallback, result.RootCandidates[i].EndPoint);
				}
			}

			void GetReqCallback (IAsyncResult ar)
			{
				EndPoint ep = (EndPoint)ar.AsyncState;
				GetResponse res = _dht._sock.EndInquire (ar) as GetResponse;
				try {
					int waiting = Interlocked.Decrement (ref _waiting);
					lock (_list) {
						if (_result != null)
							return;
						foreach (object value in res.Values) {
							try {
								T casted_value = (T)value;
								if (_list.Contains (casted_value))
									continue;
								_list.Add (casted_value);
							} catch {}
						}
						if (_list.Count >= _opts.MaxValues) {
							T[] values = _list.ToArray ().CopyRange<T> (0, Math.Min (_list.Count, _opts.MaxValues));
							_result = new GetResult<T> (_key, values, _hops);
							Done ();
							return;
						}
					}
					if (waiting == 0) {
						_result = new GetResult<T> (_key, _list.ToArray (), _hops);
						Done ();
						return;
					}
				} finally {
					if (res == null)
						_dht._kbr.RoutingAlgorithm.Fail (ep);
					else
						_dht._kbr.RoutingAlgorithm.Touch (ep);
				}
			}

			public GetResult<T> Result
			{
				get { return _result; }
			}
		}
		sealed class PutAsyncResult : AsyncResultBase
		{
			TimeSpan _lifeTime;
			object _value;
			int _waiting = 0;

			public PutAsyncResult (SimpleDHT dht, Key appId, Key key, int typeId, TimeSpan lifeTime, object value, AsyncCallback callback, object state)
				: base (dht, appId, key, typeId, callback, state)
			{
				_lifeTime = lifeTime;
				_value = value;
				_dht._kbr.BeginRoute (appId, key, _dht.NumberOfReplica, new KeyBasedRoutingOptions {NumberOfSimultaneous = dht.NumberOfSimultaneous}, KBR_Callback, null);
			}

			void KBR_Callback (IAsyncResult ar)
			{
				RoutingResult result = _dht._kbr.EndRoute (ar);
				if (result.RootCandidates.Length == 0) {
					Done ();
					return;
				}
				PutRequest req = new PutRequest (_key, _value, _lifeTime);
				for (int i = 0; i < result.RootCandidates.Length; i++) {
					Interlocked.Increment (ref _waiting);
					_dht._sock.BeginInquire (req, result.RootCandidates[i].EndPoint, PutReqCallback, result.RootCandidates[i].EndPoint);
				}
			}

			void PutReqCallback (IAsyncResult ar)
			{
				EndPoint ep = (EndPoint)ar.AsyncState;
				object ret = _dht._sock.EndInquire (ar);
				if (Interlocked.Decrement (ref _waiting) == 0)
					Done ();
				if (ret == null)
					_dht._kbr.RoutingAlgorithm.Fail (ep);
				else
					_dht._kbr.RoutingAlgorithm.Touch (ep);
			}
		}
		#endregion

		#region Message Handlers
		void Inquired_GetRequest (object sender, InquiredEventArgs e)
		{
			GetRequest getReq = (GetRequest)e.InquireMessage;
			object[] ret = _local.Get (getReq.Key, _typeReg[getReq.TypeID].Type, getReq.NumberOfValues);
			_sock.RespondToInquiry (e, new GetResponse (ret));
		}

		void Inquired_PutRequest (object sender, InquiredEventArgs e)
		{
			PutRequest putReq = (PutRequest)e.InquireMessage;
			_sock.RespondToInquiry (e, PutResponse.Instance);
			_local.Put (putReq.Key, putReq.LifeTime, putReq.Value);
		}
		#endregion

		#region Messages
		[Serializable]
		[SerializableTypeId (0x300)]
		sealed class GetRequest
		{
			[SerializableFieldId (0)]
			Key _key;

			[SerializableFieldId (1)]
			int _typeId;

			[SerializableFieldId (2)]
			int _numOfValues;

			public GetRequest (Key key, int typeId, int numOfValues)
			{
				_key = key;
				_typeId = typeId;
				_numOfValues = numOfValues;
			}

			public Key Key {
				get { return _key; }
			}

			public int TypeID {
				get { return _typeId; }
			}

			public int NumberOfValues {
				get { return _numOfValues; }
			}
		}

		[Serializable]
		[SerializableTypeId (0x301)]
		sealed class GetResponse
		{
			[SerializableFieldId (0)]
			object[] _values;

			public GetResponse (object[] values)
			{
				_values = values;
			}

			public object[] Values {
				get { return _values; }
			}
		}

		[Serializable]
		[SerializableTypeId (0x302)]
		sealed class PutRequest
		{
			[SerializableFieldId (0)]
			Key _key;

			[SerializableFieldId (1)]
			object _value;

			[SerializableFieldId (2)]
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
		[SerializableTypeId (0x303)]
		sealed class PutResponse
		{
			static PutResponse _instance = new PutResponse ();
			PutResponse ()
			{
			}

			public static PutResponse Instance {
				get { return _instance; }
			}
		}
		#endregion
	}
}
