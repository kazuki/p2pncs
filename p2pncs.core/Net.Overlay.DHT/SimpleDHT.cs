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

// TODO: 複数のストア候補ノードからの結果をマージする機能を実装する
// TODO: PutするオブジェクトがIPutterEndPointStoreインターフェイスを実装していた場合の動作が、IterativeなKBR専用となっているので、汎用性を持たせる
// TODO: 複数候補へのGet/Put時、どのタイミングで停止するかを制御する機能を実装する (現状では1つでも応答があれば他の応答を無視している)

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace p2pncs.Net.Overlay.DHT
{
	public class SimpleDHT : IDistributedHashTable
	{
		IKeyBasedRouter _kbr;
		IMessagingSocket _sock;
		ILocalHashTable _lht;
		int _numOfReplicas = 3, _numOfSimultaneous = 3;
		Dictionary<Type, int> _typeMap = new Dictionary<Type, int> ();

		public SimpleDHT (IKeyBasedRouter kbr, IMessagingSocket sock, ILocalHashTable lht)
		{
			_kbr = kbr;
			_sock = sock;
			_lht = lht;
			sock.AddInquiredHandler (typeof (GetRequest), MessagingSocket_Inquired_GetRequest);
			sock.AddInquiredHandler (typeof (PutRequest), MessagingSocket_Inquired_PutRequest);
		}

		void MessagingSocket_Inquired_GetRequest (object sender, InquiredEventArgs e)
		{
			GetRequest getReq = (GetRequest)e.InquireMessage;
			_sock.StartResponse (e, new GetResponse (_lht.Get (getReq.Key, getReq.TypeID)));
		}
		void MessagingSocket_Inquired_PutRequest (object sender, InquiredEventArgs e)
		{
			PutRequest putReq = (PutRequest)e.InquireMessage;
			LocalPut (putReq.Key, putReq.LifeTime, putReq.Value, e.EndPoint);
			_sock.StartResponse (e, new PutResponse ());
		}
		void LocalPut (Key key, TimeSpan lifeTime, object value, EndPoint remoteEP)
		{
			IPutterEndPointStore epStore = value as IPutterEndPointStore;
			if (epStore != null)
				epStore.EndPoint = remoteEP;

			int typeId;
			if (_typeMap.TryGetValue (value.GetType (), out typeId)) {
				_lht.Put (key, typeId, DateTime.Now + lifeTime, value);
			}
		}

		#region IDistributedHashTable Members

		public void RegisterTypeID (Type type, int id)
		{
			_typeMap.Add (type, id);
		}

		public IAsyncResult BeginGet (Key key, int typeId, AsyncCallback callback, object state)
		{
			AsyncResult ar = new AsyncResult (key, typeId, this, callback, state);
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

		#region IDisposable Members

		public void Dispose ()
		{
			_sock.RemoveInquiredHandler (typeof (GetRequest), MessagingSocket_Inquired_GetRequest);
			_sock.RemoveInquiredHandler (typeof (PutRequest), MessagingSocket_Inquired_PutRequest);
			_lht.Dispose ();
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
			int _typeId;
			AsyncCallback _callback;
			object _state;
			ManualResetEvent _done = new ManualResetEvent (false);
			bool _completed = false;
			GetResult _result = null;
			DateTime _start;
			PutRequest _putReq = null;
			int _hops, _getInquires = 0;
			object _getLock = null;

			public AsyncResult (Key key, int typeId, SimpleDHT dht, AsyncCallback callback, object state)
			{
				_callback = callback;
				_state = state;
				_dht = dht;
				_key = key;
				_typeId = typeId;
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
					msg = new GetRequest (_key, _typeId);
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
								object[] ret = _dht.LocalHashTable.Get (_key, _typeId);
								if (ret != null || _getInquires == 0) {
									_result = new GetResult (_key, ret, rr.Hops);
									Done ();
									return;
								} else {
								}
							}
						} else {
							_dht.LocalPut (_putReq.Key, _putReq.LifeTime, _putReq.Value, null);
							Done ();
						}
					} else {
						_dht.MessagingSocket.BeginInquire (msg, rr.RootCandidates[i].EndPoint, callback, rr.RootCandidates[i].EndPoint);
					}
				}
			}

			void GetRequest_Callback (IAsyncResult ar)
			{
				EndPoint remoteEP = (EndPoint)ar.AsyncState;
				GetResponse res = _dht.MessagingSocket.EndInquire (ar) as GetResponse;
				lock (_getLock) {
					if (_result != null)
						return;
					_getInquires --;
					if (res == null || res.Values == null) {
						if (_getInquires != 0)
							return;
						_result = new GetResult (_key, null, _hops);
					} else {
						for (int i = 0; i < res.Values.Length; i ++) {
							IPutterEndPointStore epStore = res.Values[i] as IPutterEndPointStore;
							if (epStore != null && epStore.EndPoint == null)
								epStore.EndPoint = remoteEP;
						}
						_result = new GetResult (_key, res.Values, _hops);
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
		[SerializableTypeId (0x300)]
		class GetRequest
		{
			[SerializableFieldId (0)]
			Key _key;

			[SerializableFieldId (1)]
			int _typeId;

			public GetRequest (Key key, int typeId)
			{
				_key = key;
				_typeId = typeId;
			}

			public Key Key {
				get { return _key; }
			}

			public int TypeID {
				get { return _typeId; }
			}
		}

		[Serializable]
		[SerializableTypeId (0x301)]
		class GetResponse
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
		class PutRequest
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
		class PutResponse
		{
			public PutResponse ()
			{
			}
		}
		#endregion
	}
}
