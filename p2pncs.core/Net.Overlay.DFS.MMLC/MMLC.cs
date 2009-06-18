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
using System.Threading;
using openCrypto.EllipticCurve;
using p2pncs.Net.Overlay.Anonymous;
using p2pncs.Net.Overlay.DHT;
using p2pncs.Security.Cryptography;
using p2pncs.Threading;
using p2pncs.Utility;

namespace p2pncs.Net.Overlay.DFS.MMLC
{
	/// <summary>Mergeable and Manageable DFS Providing low consistency using DHT</summary>
	public class MMLC : IDisposable
	{
		// On-memory Database
		ReaderWriterLockWrapper _dbLock = new ReaderWriterLockWrapper ();
		Dictionary<Key, KeyValuePair<MergeableFileHeader, List<MergeableFileRecord>>> _db = new Dictionary<Key,KeyValuePair<MergeableFileHeader,List<MergeableFileRecord>>> ();
		HashSet<Key> _localChangedList = new HashSet<Key> ();

		TimeSpan _rePutInterval = TimeSpan.FromMinutes (3);
		DateTime _rePutNextTime = DateTime.Now;
		List<Key> _rePutList = new List<Key> ();

		IAnonymousRouter _ar;
		ISubscribeInfo _uploadSide;
		IDistributedHashTable _dht;

		IMassKeyDelivererLocalStore _mkd;
		IntervalInterrupter _int; // Re-put Interval Timer
		IntervalInterrupter _anonStrmSockInt;

		const int MaxDatagramSize = 500;

		public MMLC (IAnonymousRouter ar, IDistributedHashTable dht, IMassKeyDelivererLocalStore mkdStore, IntervalInterrupter anonStrmSockInt, IntervalInterrupter reputInt)
		{
			_ar = ar;
			_mkd = mkdStore;
			_anonStrmSockInt = anonStrmSockInt;
			_int = reputInt;
			_dht = dht;

			ECKeyPair uploadPrivateKey = ECKeyPair.Create (DefaultAlgorithm.ECDomainName);
			Key uploadSideKey = Key.Create (uploadPrivateKey);
			_uploadSide = _ar.SubscribeRecipient (uploadSideKey, uploadPrivateKey);
			_uploadSide.Accepting += new AcceptingEventHandler (AnonymousRouter_Accepting);
			_uploadSide.Accepted += new AcceptedEventHandler (AnonymousRouter_Accepted);

			dht.RegisterTypeID (typeof (DHTMergeableFileValue), 2, DHTMergeableFileValue.Merger);
			_int.AddInterruption (RePut);

			_ar.AddBoundaryNodeReceivedEventHandler (typeof (DHTLookupRequest), DHTLookupRequest_Handler);
			_ar.AddBoundaryNodeReceivedEventHandler (typeof (PublishMessage), PublishMessage_Handler);
			_ar.AddBoundaryNodeReceivedEventHandler (typeof (FastPublishMessage), FastPublishMessage_Handler);
		}

		#region AnonymousRouter.BoundaryNode ReceivedEvent Handlers
		void PublishMessage_Handler (object sender, BoundaryNodeReceivedEventArgs args)
		{
			PublishMessage msg = args.Request as PublishMessage;
			for (int i = 0; i < msg.Values.Length; i ++)
				_dht.LocalPut (msg.Values[i].Key, TimeSpan.FromMinutes (5), new DHTMergeableFileValue (args.RecipientKey, msg.Values[i].Hash));
		}

		void DHTLookupRequest_Handler (object sender, BoundaryNodeReceivedEventArgs args)
		{
			DHTLookupRequest req = args.Request as DHTLookupRequest;
			_dht.BeginGet (req.Key, typeof (DHTMergeableFileValue), delegate (IAsyncResult ar) {
				GetResult result = _dht.EndGet (ar);
				List<DHTMergeableFileValue> list = new List<DHTMergeableFileValue> ();
				if (result != null && result.Values != null) {
					for (int i = 0; i < result.Values.Length; i ++) {
						DHTMergeableFileValue key = result.Values[i] as DHTMergeableFileValue;
						if (key != null)
							list.Add (key);
					}
				}
				args.StartResponse (new DHTLookupResponse (list.ToArray ()));
			}, null);
		}

		void FastPublishMessage_Handler (object sender, BoundaryNodeReceivedEventArgs args)
		{
			FastPublishMessage msg = args.Request as FastPublishMessage;
			_dht.BeginPut (msg.Key, TimeSpan.FromMinutes (5), new DHTMergeableFileValue (args.RecipientKey, msg.Hash), null, null);
		}
		#endregion

		#region Manipulating MergeableFile/MergeabileFileRecord
		public void CreateNew (IHashComputable headerContent)
		{
			ECKeyPair privateKey = ECKeyPair.Create (DefaultAlgorithm.ECDomainName);
			Key publicKey = Key.Create (privateKey);
			MergeableFileHeader header = new MergeableFileHeader (privateKey, DateTime.Now, headerContent);
			CreateNew (header);
		}

		public void CreateNew (MergeableFileHeader header)
		{
			using (_dbLock.EnterWriteLock ()) {
				_db.Add (header.Key, new KeyValuePair<MergeableFileHeader, List<MergeableFileRecord>> (header, new List<MergeableFileRecord> ()));
			}
		}

		public MergeableFileHeader[] GetHeaderList ()
		{
			List<MergeableFileHeader> headers = new List<MergeableFileHeader> ();
			using (_dbLock.EnterReadLock ()) {
				foreach (KeyValuePair<MergeableFileHeader, List<MergeableFileRecord>> value in _db.Values) {
					headers.Add (value.Key);
				}
			}
			return headers.ToArray ();
		}

		public List<MergeableFileRecord> GetRecords (Key key, out MergeableFileHeader header)
		{
			KeyValuePair<MergeableFileHeader, List<MergeableFileRecord>> kv;
			using (_dbLock.EnterReadLock ()) {
				if (!_db.TryGetValue (key, out kv)) {
					header = null;
					return null;
				}
				header = kv.Key;
				return new List<MergeableFileRecord> (kv.Value);
			}
		}

		public void AppendRecord (Key key, MergeableFileRecord record)
		{
			if (key == null || record == null || record.Content == null)
				throw new ArgumentNullException ();

			KeyValuePair<MergeableFileHeader, List<MergeableFileRecord>> kv;
			using (_dbLock.EnterReadLock ()) {
				if (!_db.TryGetValue (key, out kv))
					throw new KeyNotFoundException ();
			}
			lock (kv.Value) {
				record.LastManagedTime = kv.Key.LastManagedTime;
				record.UpdateHash ();
				bool exists = kv.Value.Exists (delegate (MergeableFileRecord item) {
					return Key.Equals (record.Hash, item.Hash);
				});
				if (exists)
					throw new ArgumentException ();
				kv.Value.Add (record);
				kv.Key.RecordsetHash = kv.Key.RecordsetHash.Xor (record.Hash);
			}
			lock (_localChangedList) {
				_localChangedList.Add (kv.Key.Key);
			}
		}

		void Touch (MergeableFileHeader header)
		{
			lock (_rePutList) {
				if (_rePutList.Contains (header.Key))
					return;
				_rePutList.Add (header.Key);
			}
			FastPublishMessage msg = new FastPublishMessage (header.Key, header.RecordsetHash);
			_uploadSide.MessagingSocket.BeginInquire (msg, null, null, null);
		}
		#endregion

		#region Misc
		void RePut ()
		{
			lock (_rePutList) {
				if (_rePutList.Count == 0) {
					if (_rePutNextTime > DateTime.Now)
						return;

					using (_dbLock.EnterReadLock ()) {
						foreach (KeyValuePair<MergeableFileHeader, List<MergeableFileRecord>> value in _db.Values) {
							_rePutList.Add (value.Key.Key);
						}
					}
					_rePutNextTime = DateTime.Now + _rePutInterval;
					if (_rePutList.Count == 0)
						return;
				}

				int num = Math.Min (10, _rePutList.Count);
				List<PublishData> values = new List<PublishData> ();
				int count = 0;
				using (_dbLock.EnterReadLock ()) {
					for (int i = 0; i < _rePutList.Count && count < num; i++) {
						KeyValuePair<MergeableFileHeader, List<MergeableFileRecord>> value;
						if (!_db.TryGetValue (_rePutList[i], out value))
							continue;
						values.Add (new PublishData (_rePutList[i], value.Key.RecordsetHash));
						count++;
					}
				}
				_rePutList.RemoveRange (0, count);
				_uploadSide.MessagingSocket.BeginInquire (new PublishMessage (values.ToArray ()), null, null, null);
			}
		}

		public void Dispose ()
		{
			_ar.RemoveBoundaryNodeReceivedEventHandler (typeof (DHTLookupRequest));
			_ar.RemoveBoundaryNodeReceivedEventHandler (typeof (PublishMessage));
			try {
				_ar.UnsubscribeRecipient (_uploadSide.Key);
			} catch { }
			_int.RemoveInterruption (RePut);
			_dbLock.Dispose ();
		}
		#endregion

		#region Merge

		public void StartMerge (Key key, EventHandler<MergeDoneCallbackArgs> callback, object state)
		{
			bool forseFastPut = false;
			lock (_localChangedList) {
				forseFastPut = _localChangedList.Remove (key);
			}
			MergeProcess proc = new MergeProcess (this, key, forseFastPut, callback, state);
			proc.Thread.Start ();
		}

		void AnonymousRouter_Accepting (object sender, AcceptingEventArgs args)
		{
			MergeableFileHeader other_header = args.Payload as MergeableFileHeader;
			Key key;
			if (other_header == null) {
				key = args.Payload as Key;
				if (key == null) {
					args.Reject ();
					return;
				}
			} else {
				key = other_header.Key;
			}

			KeyValuePair<MergeableFileHeader, List<MergeableFileRecord>> kvPair;
			using (_dbLock.EnterReadLock ()) {
				if (!_db.TryGetValue (key, out kvPair)) {
					args.Reject ();
					return;
				}
			}

			args.Accept (kvPair.Key, kvPair);
		}

		void AnonymousRouter_Accepted (object sender, AcceptedEventArgs args)
		{
			StreamSocket sock = new StreamSocket (args.Socket, AnonymousRouter.DummyEndPoint, MaxDatagramSize, _anonStrmSockInt);
			args.Socket.InitializedEventHandlers ();
			MergeProcess proc = new MergeProcess (this, sock, args.State, args.Payload as MergeableFileHeader);
			proc.Thread.Start ();
		}

		sealed class MergeProcess
		{
			MMLC _mmlc;
			Key _key;
			StreamSocket _sock;
			KeyValuePair<MergeableFileHeader, List<MergeableFileRecord>> _kvPair;
			Thread _thrd;
			EventHandler<MergeDoneCallbackArgs> _callback;
			MergeableFileHeader _other_header = null;
			object _state;
			bool _isInitiator, _forseFastPut = false;

			public MergeProcess (MMLC mmlc, Key key, bool forseFastPut, EventHandler<MergeDoneCallbackArgs> callback, object state)
			{
				_mmlc = mmlc;
				_key = key;
				using (_mmlc._dbLock.EnterReadLock ()) {
					if (_mmlc._db.TryGetValue (key, out _kvPair))
						_key = null;
				}
				_forseFastPut = forseFastPut;
				_isInitiator = true;
				_thrd = new Thread (Process);
				_callback = callback;
				_state = state;
			}

			public MergeProcess (MMLC mmlc, StreamSocket sock, object state, MergeableFileHeader other_header)
			{
				_mmlc = mmlc;
				_sock = sock;
				_kvPair = (KeyValuePair<MergeableFileHeader, List<MergeableFileRecord>>)state;
				_other_header = other_header;
				_isInitiator = false;
				_thrd = new Thread (Process);
			}

			public Thread Thread {
				get { return _thrd; }
			}

			void Process ()
			{
				try {
					Key beforeHash = _kvPair.Key == null ? null : _kvPair.Key.RecordsetHash;
					if (_isInitiator) {
						InitiatorSideProcess ();
					} else {
						AccepterSideProcess ();
					}
					if (_kvPair.Key != null && (_forseFastPut || beforeHash == null || !beforeHash.Equals (_kvPair.Key.RecordsetHash)))
						_mmlc.Touch (_kvPair.Key);
				} catch (Exception exception) {
					Console.WriteLine ("{0}: マージ中に例外. {1}", _isInitiator ? "I" : "A", exception.ToString ());
				} finally {
					if (_callback != null) {
						try {
							_callback (null, new MergeDoneCallbackArgs (_state));
						} catch {}
					}
					if (_sock != null) {
						try {
							_sock.Shutdown ();
						} catch {}
						Console.WriteLine ("StreamSocket is shutdown");
						try {
							_sock.Dispose ();
						} catch {}
					}
				}
			}

			void InitiatorSideProcess ()
			{
				Console.WriteLine ("I: START");
				Console.WriteLine ("I: DHTに問い合わせ中...");
				IAsyncResult ar = _mmlc._uploadSide.MessagingSocket.BeginInquire (new DHTLookupRequest (_key != null ? _key : _kvPair.Key.Key), null, null, null);
				DHTLookupResponse dhtres = _mmlc._uploadSide.MessagingSocket.EndInquire (ar) as DHTLookupResponse;
				Console.WriteLine ("I: DHTから応答受信. 結果={0}件", dhtres == null || dhtres.Values == null ? 0 : dhtres.Values.Length);
				if (dhtres == null || dhtres.Values == null || dhtres.Values.Length == 0)
					return;
				foreach (DHTMergeableFileValue value in dhtres.Values)
					Console.WriteLine ("I:   {0}/{1}", value.Holder.ToString ().Substring (0, 8), value.Hash.ToString ().Substring (0, 8));
				bool connecting = false;
				for (int i = 0; i < dhtres.Values.Length; i++) {
					try {
						if (_key == null && _kvPair.Key.RecordsetHash.Equals (dhtres.Values[i].Hash))
							continue;
						ar = _mmlc._ar.BeginConnect (_mmlc._uploadSide.Key, dhtres.Values[i].Holder, AnonymousConnectionType.HighThroughput,
							_key != null ? (object)_key : (object)_kvPair.Key, null, null);
						Console.WriteLine ("I: {0}へ接続", dhtres.Values[i].Holder.ToString ().Substring (0, 8));
						connecting = true;
						break;
					} catch {}
				}
				if (!connecting) {
					Console.WriteLine ("I: 接続先が見つかりません");
					return;
				}
				IAnonymousSocket sock;
				DateTime dt = DateTime.Now;
				try {
					sock = _mmlc._ar.EndConnect (ar);
				} catch {
					Console.WriteLine ("I: 接続失敗");
					return;
				}
				Console.WriteLine ("I: 接続OK");
				_sock = new StreamSocket (sock, null, MaxDatagramSize, DateTime.Now.Subtract (dt), _mmlc._anonStrmSockInt);
				sock.InitializedEventHandlers ();
				Console.WriteLine ("I: StreamSocket...OK");
				MergeableFileHeader header = sock.PayloadAtEstablishing as MergeableFileHeader;
				if (header == null || !(_key == null ? _kvPair.Key.Key : _key).Equals (header.Key) || !header.Verify ()) {
					Console.WriteLine ("I: 不正なヘッダだよん");
					return;
				}
				while (true) {
					if (_key == null) {
						// ヘッダを更新する必要があるか
						// 管理更新に対応していないので、現時点では何もしない...
						break;
					} else {
						// ヘッダがないので受信したヘッダを利用
						using (_mmlc._dbLock.EnterWriteLock ()) {
							if (_mmlc._db.TryGetValue (_key, out _kvPair)) {
								// なんかヘッダが入っているので、更新する必要があるかチェック
								_key = null;
								continue;
							}
							_kvPair = new KeyValuePair<MergeableFileHeader, List<MergeableFileRecord>> (header.CopyBasisInfo (), new List<MergeableFileRecord> ());
							_mmlc._db.Add (_key, _kvPair);
							_key = null;
							break;
						}
					}
				}
				Console.WriteLine ("I: ヘッダを受信");

				if (_kvPair.Key.RecordsetHash.Equals (header.RecordsetHash)) {
					Console.WriteLine ("I: 同じデータなのでマージ処理終了");
					return;
				}

				byte[] buf = new byte[4];
				Console.WriteLine ("I: 保持しているレコード一覧を送信中");
				List<Key> list = new List<Key> ();
				lock (_kvPair.Value) {
					for (int i = 0; i < _kvPair.Value.Count; i++)
						list.Add (_kvPair.Value[i].Hash);
				}
				SendMessage (new MergeableRecordList (list.ToArray ()));

				Console.WriteLine ("I: レコード一覧を受信中...");
				MergeableRecordList recordList = ReceiveMessage (ref buf) as MergeableRecordList;
				if (recordList == null) {
					Console.WriteLine ("I: デシリアライズ失敗");
					return;
				}

				Console.WriteLine ("I: レコードを送信中");
				HashSet<Key> sendSet = new HashSet<Key> (recordList.HashList);
				List<MergeableFileRecord> sendList = new List<MergeableFileRecord> ();
				lock (_kvPair.Value) {
					for (int i = 0; i < _kvPair.Value.Count; i++) {
						if (sendSet.Contains (_kvPair.Value[i].Hash))
							sendList.Add (_kvPair.Value[i]);
					}
				}
				SendMessage (new MergeableRecords (sendList.ToArray ()));

				Console.WriteLine ("I: レコードを受信中...");
				MergeableRecords records = ReceiveMessage (ref buf) as MergeableRecords;
				if (recordList == null) {
					Console.WriteLine ("I: デシリアライズ失敗");
					return;
				}

				Console.WriteLine ("I: マージ中");
				Merge (records);
				Console.WriteLine ("I: マージ完了!!!");
			}

			void AccepterSideProcess ()
			{
				Console.WriteLine ("A: START");
				if (_other_header != null && _kvPair.Key.RecordsetHash.Equals (_other_header.RecordsetHash)) {
					Console.WriteLine ("A: 同じデータなのでマージ処理終了");
					return;
				}

				byte[] buf = new byte[4];
				Console.WriteLine ("A: 保持しているレコード一覧を受信中...");
				MergeableRecordList recordList = ReceiveMessage (ref buf) as MergeableRecordList;
				if (recordList == null) {
					Console.WriteLine ("A: デシリアライズ失敗");
					return;
				}

				HashSet<Key> otherSet = new HashSet<Key> (recordList.HashList);
				List<MergeableFileRecord> sendList = new List<MergeableFileRecord> ();
				Console.WriteLine ("A: 保持しているレコード一覧を送信中");
				lock (_kvPair.Value) {
					for (int i = 0; i < _kvPair.Value.Count; i++)
						if (!otherSet.Remove (_kvPair.Value[i].Hash))
							sendList.Add (_kvPair.Value[i]);
				}
				Key[] keys = new Key[otherSet.Count];
				otherSet.CopyTo (keys);
				SendMessage (new MergeableRecordList (keys));

				Console.WriteLine ("A: レコードを送信中");
				SendMessage (new MergeableRecords (sendList.ToArray ()));

				Console.WriteLine ("A: レコードを受信中...");
				MergeableRecords records = ReceiveMessage (ref buf) as MergeableRecords;
				if (recordList == null) {
					Console.WriteLine ("A: デシリアライズ失敗");
					return;
				}

				Console.WriteLine ("A: マージ中");
				Merge (records);
				Console.WriteLine ("A: マージ完了!!!");
			}

			void SendMessage (object msg)
			{
				byte[] buf;
				using (MemoryStream ms = new MemoryStream ()) {
					ms.Seek (4, SeekOrigin.Begin);
					Serializer.Instance.Serialize (ms, msg);
					ms.Close ();
					buf = ms.ToArray ();
				}

				int size = buf.Length - 4;
				buf[0] = (byte)(size >> 24);
				buf[1] = (byte)(size >> 16);
				buf[2] = (byte)(size >>  8);
				buf[3] = (byte)(size);
				
				_sock.Send (buf, 0, buf.Length);
			}

			object ReceiveMessage (ref byte[] buf)
			{
				int size = 4, received = 0;
				while (received < size)
					received += _sock.Receive (buf, received, size - received);
				size = (buf[0] << 24) | (buf[1] << 16) | (buf[2] << 8) | buf[3];
				received = 0;
				if (buf.Length < size)
					buf = new byte[size];
				while (received < size)
					received += _sock.Receive (buf, received, size - received);
				return Serializer.Instance.Deserialize (buf);
			}

			void Merge (MergeableRecords records)
			{
				lock (_kvPair.Value) {
					Key hash = _kvPair.Key.RecordsetHash;
					HashSet<Key> set = new HashSet<Key> ();
					for (int i = 0; i < _kvPair.Value.Count; i++)
						set.Add (_kvPair.Value[i].Hash);
					for (int i = 0; i < records.Records.Length; i++) {
						if (set.Add (records.Records[i].Hash)) {
							_kvPair.Value.Add (records.Records[i]);
							hash = hash.Xor (records.Records[i].Hash);
						}
					}
					_kvPair.Key.RecordsetHash = hash;
				}
			}
		}
		#endregion

		#region Messages
		[SerializableTypeId (0x402)]
		sealed class PublishData
		{
			[SerializableFieldId (0)]
			Key _key;

			[SerializableFieldId (1)]
			Key _hash;

			public PublishData (Key key, Key hash)
			{
				_key = key;
				_hash = hash;
			}

			public Key Key {
				get { return _key; }
			}

			public Key Hash {
				get { return _hash; }
			}
		}

		[SerializableTypeId (0x403)]
		sealed class PublishMessage
		{
			[SerializableFieldId (0)]
			PublishData[] _values;

			public PublishMessage (PublishData[] values)
			{
				_values = values;
			}

			public PublishData[] Values {
				get { return _values; }
			}
		}

		[SerializableTypeId (0x404)]
		sealed class DHTLookupRequest
		{
			[SerializableFieldId (0)]
			Key _key;

			public DHTLookupRequest (Key key)
			{
				_key = key;
			}

			public Key Key {
				get { return _key; }
			}
		}

		[SerializableTypeId (0x405)]
		sealed class DHTLookupResponse
		{
			[SerializableFieldId (0)]
			DHTMergeableFileValue[] _values;

			public DHTLookupResponse (DHTMergeableFileValue[] values)
			{
				_values = values;
			}

			public DHTMergeableFileValue[] Values {
				get { return _values; }
			}
		}

		[SerializableTypeId (0x406)]
		sealed class MergeableRecordList
		{
			[SerializableFieldId (0)]
			Key[] _hashList;

			public MergeableRecordList (Key[] hashList)
			{
				_hashList = hashList;
			}

			public Key[] HashList {
				get { return _hashList; }
			}
		}

		[SerializableTypeId (0x407)]
		sealed class MergeableRecords
		{
			[SerializableFieldId (0)]
			MergeableFileRecord[] _records;

			public MergeableRecords (MergeableFileRecord[] records)
			{
				_records = records;
			}

			public MergeableFileRecord[] Records {
				get { return _records; }
			}
		}

		[SerializableTypeId (0x409)]
		sealed class FastPublishMessage
		{
			[SerializableFieldId (0)]
			Key _key;

			[SerializableFieldId (1)]
			Key _hash;

			public FastPublishMessage (Key key, Key hash)
			{
				_key = key;
				_hash = hash;
			}

			public Key Key {
				get { return _key; }
			}

			public Key Hash {
				get { return _hash; }
			}
		}
		#endregion

		#region Internal Class
		[SerializableTypeId (0x408)]
		sealed class DHTMergeableFileValue : IEquatable<DHTMergeableFileValue>
		{
			[SerializableFieldId (0)]
			Key _holder;

			[SerializableFieldId (1)]
			Key _hash;

			public DHTMergeableFileValue (Key holder, Key hash)
			{
				_holder = holder;
				_hash = hash;
			}

			public Key Holder {
				get { return _holder; }
			}

			public Key Hash {
				get { return _hash; }
			}

			public override int GetHashCode ()
			{
				return _holder.GetHashCode ();
			}
			
			public bool Equals (DHTMergeableFileValue other)
			{
				return _holder.Equals (other._holder);
			}

			public static ILocalHashTableValueMerger Merger {
				get { return ValueMerger.Instance; }
			}

			sealed class ValueMerger : ILocalHashTableValueMerger, IMassKeyDelivererValueGetter
			{
				public static ValueMerger Instance = new ValueMerger ();
				ValueMerger () { }

				#region ILocalHashTableValueMerger Members

				public object Merge (object value, object new_value, DateTime expirationDate)
				{
					StoreValue store_value = value as StoreValue;
					if (store_value == null)
						store_value = new StoreValue ();
					lock (store_value) {
						store_value.Merge (new_value as DHTMergeableFileValue, expirationDate);
					}
					return store_value;
				}

				public object[] GetEntries (object value, int max_num)
				{
					lock (value) {
						return (value as StoreValue).GetEntries (max_num);
					}
				}

				public void ExpirationCheck (object value)
				{
					lock (value) {
						(value as StoreValue).ExpirationCheck ();
					}
				}

				#endregion

				#region IMassKeyDelivererValueGetter Members

				public DHTEntry[] GetSendEntries (Key key, int typeId, object value, int max_num)
				{
					lock (value) {
						return (value as StoreValue).GetSendEntries (key, typeId, max_num);
					}
				}

				public void UnmarkSendFlag (object value, object mark_value)
				{
					lock (value) {
						(value as StoreValue).UnmarkSendFlag (mark_value as DHTMergeableFileValue);
					}
				}

				#endregion

				class StoreValue
				{
					Dictionary<Key, List<HolderInfo>> _dic = new Dictionary<Key, List<HolderInfo>> (); // Key is Hash
					Dictionary<Key, HolderInfo> _dic2 = new Dictionary<Key,HolderInfo> (); // Key is Holder

					public StoreValue ()
					{
					}

					public void Merge (DHTMergeableFileValue new_value, DateTime expirationDate)
					{
						HolderInfo info;
						List<HolderInfo> list;

						if (_dic2.TryGetValue (new_value.Holder, out info)) {
							if (info.ExpirationDate >= expirationDate)
								return;

							info.Extend (expirationDate);
							if (info.Hash.Equals (new_value.Hash))
								return;
							
							list = _dic[info.Hash];
							for (int i = 0; i < list.Count; i ++) {
								if (list[i].ExpirationDate <= DateTime.Now) {
									_dic2.Remove (list[i].Holder);
									list.RemoveAt (i --);
								} else if (info.Equals (list[i])) {
									list.RemoveAt (i--);
								}
							}
							if (list.Count == 0)
								_dic.Remove (info.Hash);
							info.Hash = new_value.Hash;
						} else {
							info = new HolderInfo (new_value, expirationDate);
							_dic2.Add (info.Holder, info);
						}

						if (!_dic.TryGetValue (info.Hash, out list)) {
							list = new List<HolderInfo> ();
							_dic.Add (info.Hash, list);
						}
						list.Add (info);
					}

					public object[] GetEntries (int max_num)
					{
						List<object> result = new List<object> ();
						int num = Math.Max (1, max_num / _dic.Count);
						Random rnd = new Random ();
						foreach (List<HolderInfo> list in _dic.Values) {
							HolderInfo[] tmp = list.ToArray ().RandomSelection (num);
							for (int i = 0; i < tmp.Length; i ++)
								result.Add (tmp[i].Value);
						}
						return result.ToArray().RandomSelection (max_num);
					}

					public void ExpirationCheck ()
					{
						List<Key> removeHash = new List<Key> ();
						foreach (KeyValuePair<Key,List<HolderInfo>> pair in _dic) {
							List<HolderInfo> list = pair.Value;
							for (int i = 0; i < list.Count; i++) {
								if (list[i].ExpirationDate <= DateTime.Now) {
									_dic2.Remove (list[i].Holder);
									list.RemoveAt (i--);
								}
							}
							if (list.Count == 0)
								removeHash.Add (pair.Key);
						}
						for (int i = 0; i < removeHash.Count; i ++)
							_dic.Remove (removeHash[i]);
					}

					public DHTEntry[] GetSendEntries (Key key, int typeId, int max_num)
					{
						List<HolderInfo> result = new List<HolderInfo> ();
						int num = Math.Max (1, max_num / _dic.Count);
						Random rnd = new Random ();
						foreach (List<HolderInfo> list in _dic.Values) {
							for (int i = 0, q = 0; i < list.Count && q < num; i ++) {
								if (list[i].ExpirationDate <= DateTime.Now) {
									_dic2.Remove (list[i].Holder);
									list.RemoveAt (i --);
									continue;
								}
								if (list[i].SendFlag) continue;
								result.Add (list[i]);
								list[i].SendFlag = true;
								q ++;
							}
						}
						HolderInfo[] array = result.ToArray ().RandomSelection (max_num);
						DHTEntry[] entries = new DHTEntry [array.Length];
						for (int i = 0; i < entries.Length; i ++)
							entries[i] = new DHTEntry (key, typeId, array[i].Value, array[i].ExpirationDate);
						return entries;
					}

					public void UnmarkSendFlag (DHTMergeableFileValue mark_value)
					{
						HolderInfo info;
						if (_dic2.TryGetValue (mark_value.Holder, out info))
							info.SendFlag = false;
					}
				}

				class HolderInfo : IEquatable<HolderInfo>
				{
					DHTMergeableFileValue _value;
					DateTime _expiration;
					bool _sendFlag;

					public HolderInfo (DHTMergeableFileValue value, DateTime expiration)
					{
						_value = value;
						_expiration = expiration;
						_sendFlag = false;
					}

					public Key Holder {
						get { return _value.Holder; }
					}

					public Key Hash {
						get { return _value.Hash; }
						set { _value._hash = value;}
					}

					public DHTMergeableFileValue Value {
						get { return _value; }
					}

					public DateTime ExpirationDate {
						get { return _expiration; }
					}

					public bool SendFlag {
						get { return _sendFlag; }
						set { _sendFlag = value;}
					}

					public void Extend (DateTime new_expiration)
					{
						if (_expiration < new_expiration) {
							_expiration = new_expiration;
							_sendFlag = false;
						}
					}

					public override int GetHashCode ()
					{
						return _value.GetHashCode ();
					}

					public bool Equals (HolderInfo other)
					{
						return _value.Equals (other._value);
					}
				}
			}
		}
		#endregion
	}
}
