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

// REM TODO: 複数のストア候補ノードからの結果をマージする機能を実装する
// REM TODO: PutするオブジェクトがIPutterEndPointStoreインターフェイスを実装していた場合の動作が、IterativeなKBR専用となっているので、汎用性を持たせる
// REM TODO: 複数候補へのGet/Put時、どのタイミングで停止するかを制御する機能を実装する (現状では1つでも応答があれば他の応答を無視している)

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
		IMessagingSocket _sock;
		IKeyBasedRouter _kbr;
		ILocalHashTable _local;
		Dictionary<Type, int> _typeMap = new Dictionary<Type, int> ();
		ReaderWriterLockWrapper _typeMapLock = new ReaderWriterLockWrapper ();
		const int NumberOfReplica = 3;
		static string ACK = "ACK";

		public SimpleDHT (IMessagingSocket sock, IKeyBasedRouter kbr, ILocalHashTable localStore)
		{
			_sock = sock;
			_kbr = kbr;
			_local = localStore;
			sock.AddInquiredHandler (typeof (GetRequest), Inquired_GetRequest);
			sock.AddInquiredHandler (typeof (PutRequest), Inquired_PutRequest);
		}

		void Inquired_GetRequest (object sender, InquiredEventArgs args)
		{
			GetRequest req = (GetRequest)args.InquireMessage;
			object[] values = _local.Get (req.Key, req.TypeId, req.NumberOfValues);
			_sock.StartResponse (args, new GetResponse (values));
		}

		void Inquired_PutRequest (object sender, InquiredEventArgs args)
		{
			PutRequest req = (PutRequest)args.InquireMessage;
			_sock.StartResponse (args, ACK);
			int typeId;
			using (_typeMapLock.EnterReadLock ()) {
				typeId = _typeMap[req.Value.GetType ()];
			}
			_local.Put (req.Key, typeId, req.LifeTime, req.Value);
		}

		#region IDistributedHashTable Members

		public void RegisterType (Type type, int id)
		{
			using (_typeMapLock.EnterWriteLock ()) {
				_typeMap.Add (type, id);
			}
		}

		public IAsyncResult BeginGet (Key appId, Key key, Type type, GetOptions opts, AsyncCallback callback, object state)
		{
			int typeId;
			using (_typeMapLock.EnterReadLock ()) {
				typeId = _typeMap[type];
			}
			return new GetAsyncResult (this, appId, key, typeId, opts, callback, state);
		}

		public GetResult EndGet (IAsyncResult ar)
		{
			GetAsyncResult gar = (GetAsyncResult)ar;
			gar.AsyncWaitHandle.WaitOne ();
			return gar.Result;
		}

		public IAsyncResult BeginPut (Key appId, Key key, TimeSpan lifeTime, object value, AsyncCallback callback, object state)
		{
			int typeId;
			using (_typeMapLock.EnterReadLock ()) {
				typeId = _typeMap[value.GetType ()];
			}
			return new PutAsyncResult (this, appId, key, typeId, lifeTime, value, callback, state);
		}

		public void EndPut (IAsyncResult ar)
		{
			ar.AsyncWaitHandle.WaitOne ();
		}

		#endregion

		#region IDisposable Members

		public void Dispose ()
		{
			_sock.RemoveInquiredHandler (typeof (GetRequest), Inquired_GetRequest);
			_sock.RemoveInquiredHandler (typeof (PutRequest), Inquired_PutRequest);
		}

		#endregion

		#region Internal Class
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
					} catch {}
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
		sealed class GetAsyncResult : AsyncResultBase
		{
			GetOptions _opts;
			int _waiting = 0, _hops = -1;
			GetResult _result = null;
			List<object> _list = new List<object> ();

			public GetAsyncResult (SimpleDHT dht, Key appId, Key key, int typeId, GetOptions opts, AsyncCallback callback, object state)
				: base (dht, appId, key, typeId, callback, state)
			{
				_opts = opts;
				_dht._kbr.BeginRoute (appId, key, NumberOfReplica, null, KBR_Callback, null);
			}

			void KBR_Callback (IAsyncResult ar)
			{
				RoutingResult result = _dht._kbr.EndRoute (ar);
				GetRequest req = new GetRequest (_key, _typeId, _opts.MaxValues);
				if (result.RootCandidates.Length == 0) {
					_result = new GetResult (_key, new object[0], _hops);
					Done ();
					return;
				}
				_hops = result.Hops;
				for (int i = 0; i < result.RootCandidates.Length; i ++) {
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
							if (_list.Contains (value))
								continue;
							_list.Add (value);
						}
						if (_list.Count >= _opts.MaxValues) {
							object[] values = _list.ToArray ().CopyRange<object> (0, Math.Min (_list.Count, _opts.MaxValues));
							_result = new GetResult (_key, values, _hops);
							Done ();
							return;
						}
					}
					if (waiting == 0) {
						_result = new GetResult (_key, _list.ToArray (), _hops);
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

			public GetResult Result {
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
				_dht._kbr.BeginRoute (appId, key, NumberOfReplica, null, KBR_Callback, null);
			}

			void KBR_Callback (IAsyncResult ar)
			{
				RoutingResult result = _dht._kbr.EndRoute (ar);
				if (result.RootCandidates.Length == 0) {
					Done ();
					return;
				}
				PutRequest req = new PutRequest (_key, _value, _lifeTime);
				for (int i = 0; i < result.RootCandidates.Length; i ++) {
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

		class GetRequest
		{
			Key _key;
			int _typeId, _numOfValues;

			public GetRequest (Key key, int typeId, int numOfValues)
			{
				_key = key;
				_typeId = typeId;
				_numOfValues = numOfValues;
			}

			public Key Key {
				get { return _key; }
			}

			public int TypeId {
				get { return _typeId; }
			}

			public int NumberOfValues {
				get { return _numOfValues; }
			}
		}

		class GetResponse
		{
			object[] _values;

			public GetResponse (object[] values)
			{
				_values = values;
			}

			public object[] Values {
				get { return _values; }
			}
		}

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
		#endregion
	}
}
