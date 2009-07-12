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
using System.Data;
using System.Data.Common;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using openCrypto.EllipticCurve;
using openCrypto.EllipticCurve.Signature;
using p2pncs.Net.Overlay.Anonymous;
using p2pncs.Net.Overlay.DHT;
using p2pncs.Security.Captcha;
using p2pncs.Security.Cryptography;
using p2pncs.Threading;
using p2pncs.Utility;

namespace p2pncs.Net.Overlay.DFS.MMLC
{
	/// <summary>Mergeable and Manageable DFS Providing low consistency using DHT</summary>
	public class MMLC : IDisposable
	{
		public const int MergeableFileHeaderMaxSize = 512;
		public const int MergeableFileRecordMaxSize = 1024 * 4;

		// SQLite Database
		string _db_connection_string;
		DbProviderFactory _db_factory = Mono.Data.Sqlite.SqliteFactory.Instance;
		ReaderWriterLockWrapper _dbParserMapLock = new ReaderWriterLockWrapper ();
		Dictionary<int, IMergeableFileDatabaseParser> _dbParserMap1 = new Dictionary<int,IMergeableFileDatabaseParser> ();
		Dictionary<Type, IMergeableFileDatabaseParser> _dbParserMap2 = new Dictionary<Type, IMergeableFileDatabaseParser> ();
		HashSet<Key> _localChangedList = new HashSet<Key> ();

		Dictionary<Key, MergeProcess> _mergingFiles = new Dictionary<Key,MergeProcess> ();

		TimeSpan _rePutInterval = TimeSpan.FromMinutes (3);
		DateTime _rePutNextTime = DateTime.Now;
		List<Key> _rePutList = new List<Key> ();
		int _revision = 0;

		IAnonymousRouter _ar;
		ISubscribeInfo _uploadSide;
		IDistributedHashTable _dht;

		IMassKeyDelivererLocalStore _mkd;
		IntervalInterrupter _int; // Re-put Interval Timer
		IntervalInterrupter _anonStrmSockInt;

		// CAPTCHA
		delegate void CaptchaSegmentReceived (CaptchaChallengeSegment seg);
		Dictionary<int, CaptchaSegmentReceived> _captcha_local = new Dictionary<int,CaptchaSegmentReceived> ();

		const int MaxDatagramSize = 500;

		public MMLC (IAnonymousRouter ar, IDistributedHashTable dht, IMassKeyDelivererLocalStore mkdStore, string db_path, IntervalInterrupter anonStrmSockInt, IntervalInterrupter reputInt)
		{
			_ar = ar;
			_mkd = mkdStore;
			_anonStrmSockInt = anonStrmSockInt;
			_int = reputInt;
			_dht = dht;

			_db_connection_string = string.Format ("Data Source={0},DateTimeFormat=Ticks,Pooling=True", db_path);
			using (IDbConnection connection = CreateDBConnection ())
			using (IDbTransaction transaction = connection.BeginTransaction (IsolationLevel.Serializable)) {
				DatabaseUtility.ExecuteNonQuery (transaction, "CREATE TABLE IF NOT EXISTS MMLC_Keys (id INTEGER PRIMARY KEY, key TEXT, type INTEGER, content_type INTEGER);");
				DatabaseUtility.ExecuteNonQuery (transaction, "CREATE TABLE IF NOT EXISTS MMLC_PrivateKeys (key TEXT PRIMARY KEY, private_key BLOB);");
				DatabaseUtility.ExecuteNonQuery (transaction, "CREATE TABLE IF NOT EXISTS MMLC_MergeableHeaders (id INTEGER PRIMARY KEY REFERENCES MMLC_Keys(id), created INTEGER, lastManaged INTEGER, authServers TEXT, sign BLOB, recordsetHash TEXT);");
				DatabaseUtility.ExecuteNonQuery (transaction, "CREATE TABLE IF NOT EXISTS MMLC_MergeableRecords (id INTEGER PRIMARY KEY, hash TEXT, header_id INTEGER REFERENCES MMLC_MergeableHeaders(id), created INTEGER, lastManaged INTEGER, publickey TEXT, sign BLOB, auth_idx INTEGER, auth BLOB);");
				DatabaseUtility.ExecuteNonQuery (transaction, "CREATE UNIQUE INDEX IF NOT EXISTS MMLC_Keys_Index ON MMLC_Keys(key);");
				DatabaseUtility.ExecuteNonQuery (transaction, "CREATE UNIQUE INDEX IF NOT EXISTS MMLC_MergeableRecords_Index ON MMLC_MergeableRecords (header_id, hash);");
				transaction.Commit ();
			}

			ECKeyPair uploadPrivateKey = ECKeyPair.Create (DefaultAlgorithm.ECDomainName);
			Key uploadSideKey = Key.Create (uploadPrivateKey);
			_uploadSide = _ar.SubscribeRecipient (uploadSideKey, uploadPrivateKey);
			_uploadSide.Accepting += new AcceptingEventHandler (AnonymousRouter_Accepting);
			_uploadSide.Accepted += new AcceptedEventHandler (AnonymousRouter_Accepted);
			_uploadSide.MessagingSocket.AddReceivedHandler (typeof (CaptchaChallengeSegment), CaptchaChallengeSegment_Received);

			dht.RegisterTypeID (typeof (DHTMergeableFileValue), 2, DHTMergeableFileValue.Merger);
			_int.AddInterruption (RePut);

			_ar.AddBoundaryNodeReceivedEventHandler (typeof (DHTLookupRequest), DHTLookupRequest_Handler);
			_ar.AddBoundaryNodeReceivedEventHandler (typeof (PublishMessage), PublishMessage_Handler);
			_ar.AddBoundaryNodeReceivedEventHandler (typeof (FastPublishMessage), FastPublishMessage_Handler);
			_ar.AddBoundaryNodeReceivedEventHandler (typeof (CaptchaContainer), CaptchaContainer_Handler);
		}

		public void Register (IMergeableFileDatabaseParser parser)
		{
			using (_dbParserMapLock.EnterWriteLock ()) {
				_dbParserMap1.Add (parser.TypeId, parser);
				_dbParserMap2.Add (parser.HeaderContentType, parser);
			}
			using (IDbConnection connection = CreateDBConnection ())
			using (IDbTransaction transaction = connection.BeginTransaction (IsolationLevel.Serializable)) {
				parser.Init (transaction);
				transaction.Commit ();
			}
		}

		#region SQL Helpers
		IDbConnection CreateDBConnection ()
		{
			IDbConnection connection = _db_factory.CreateConnection ();
			connection.ConnectionString = _db_connection_string;
			connection.Open ();
			return connection;
		}

		IMergeableFileDatabaseParser GetMergeableParser (int typeId)
		{
			IMergeableFileDatabaseParser parser;
			using (_dbParserMapLock.EnterReadLock ()) {
				if (_dbParserMap1.TryGetValue (typeId, out parser))
					return parser;
				return null;
			}
		}

		IMergeableFileDatabaseParser GetMergeableParser (Type type)
		{
			IMergeableFileDatabaseParser parser;
			using (_dbParserMapLock.EnterReadLock ()) {
				if (_dbParserMap2.TryGetValue (type, out parser))
					return parser;
				return null;
			}
		}

		void Insert (IDbTransaction transaction, Key key, ECKeyPair privateKey)
		{
			string sql = "INSERT INTO MMLC_PrivateKeys (key, private_key) VALUES (?, ?);";
			DatabaseUtility.ExecuteNonQuery (transaction, sql, key.ToBase64String (), privateKey.PrivateKey);
		}

		void Insert (IDbTransaction transaction, MergeableFileHeader header, IMergeableFileDatabaseParser parser)
		{
			long header_id;
			Insert (transaction, header, ref parser, out header_id);
		}

		void Insert (IDbTransaction transaction, MergeableFileHeader header, ref IMergeableFileDatabaseParser parser, out long header_id)
		{
			if (parser == null) {
				if ((parser = GetMergeableParser (header.Content.GetType ())) == null)
					throw new ArgumentException ();
			}

			DatabaseUtility.ExecuteNonQuery (transaction,
				"INSERT INTO MMLC_Keys (key, type, content_type) VALUES (?, 2, ?);",
				header.Key.ToBase64String (), parser.TypeId);
			header_id = DatabaseUtility.GetLastInsertRowId (transaction);
			DatabaseUtility.ExecuteNonQuery (transaction,
				"INSERT INTO MMLC_MergeableHeaders (id, created, lastManaged, authServers, sign, recordsetHash) VALUES (?, ?, ?, ?, ?, ?);",
				header_id, header.CreatedTime, header.LastManagedTime, AuthServerInfo.ToParsableString (header.AuthServers),
				header.Signature, header.RecordsetHash.ToBase64String ());
			parser.Insert (transaction, header_id, header);
		}

		void Insert (IDbTransaction transaction, MergeableFileRecord record, IMergeableFileDatabaseParser parser, long header_id)
		{
			DatabaseUtility.ExecuteNonQuery (transaction,
				"INSERT INTO MMLC_MergeableRecords (hash, header_id, created, lastManaged, publickey, sign, auth_idx, auth) VALUES (?, ?, ?, ?, ?, ?, ?, ?);",
				record.Hash.ToBase64String (), header_id, record.CreatedTime, record.LastManagedTime,
				record.PublicKey == null ? null : record.PublicKey.ToBase64String (), record.Signature, record.AuthorityIndex, record.Authentication);
			long id = DatabaseUtility.GetLastInsertRowId (transaction);
			parser.Insert (transaction, id, record);
		}

		bool Update (IDbTransaction transaction, MergeableFileHeader header, out IMergeableFileDatabaseParser parser, out long header_id)
		{
			MergeableFileHeader current = SelectHeader (transaction, header.Key, out parser, out header_id) as MergeableFileHeader;
			if (current == null)
				return false;
			return Update (transaction, header, parser, header_id, current);
		}

		bool Update (IDbTransaction transaction, MergeableFileHeader new_header, IMergeableFileDatabaseParser parser, long header_id, MergeableFileHeader current_header)
		{
			if (current_header.LastManagedTime == new_header.LastManagedTime) {
				DatabaseUtility.ExecuteNonQuery (transaction, "UPDATE MMLC_MergeableHeaders SET recordsetHash=? WHERE id=?",
					new_header.RecordsetHash.ToBase64String (), header_id);
			} else {
				DatabaseUtility.ExecuteNonQuery (transaction, "UPDATE MMLC_MergeableHeaders SET lastManaged=?, authServers=?, sign=?, recordsetHash=? WHERE id=?",
					new_header.LastManagedTime, AuthServerInfo.ToParsableString (new_header.AuthServers),
					new_header.Signature, new_header.RecordsetHash.ToBase64String (), header_id);
				parser.Update (transaction, header_id, new_header);
			}
			return true;
		}

		object SelectHeader (IDbTransaction transaction, Key key)
		{
			IMergeableFileDatabaseParser parser;
			long header_id;
			return SelectHeader (transaction, key, out parser, out header_id);
		}

		object SelectHeader (IDbTransaction transaction, Key key, out IMergeableFileDatabaseParser parser, out long header_id)
		{
			header_id = 0;
			parser = null;

			MergeableFileHeader header = null;
			using (IDataReader reader1 = DatabaseUtility.ExecuteReader (transaction, "SELECT id,type,content_type FROM MMLC_Keys WHERE key=?", key.ToBase64String ())) {
				if (!reader1.Read ())
					return null;

				header_id = reader1.GetInt64 (0);
				int type = reader1.GetInt32 (1);
				if (type == 2) {
					parser = GetMergeableParser (reader1.GetInt32 (2));
					if (parser == null)
						return null;
					string sql = string.Format ("SELECT t1.created, t1.lastManaged, t1.authServers, t1.sign, t1.recordsetHash, {0} FROM MMLC_MergeableHeaders as t1,{1} as {2} WHERE t1.id=? AND t1.id={2}.id", parser.ParseHeaderFields, parser.HeaderTableName, parser.TableAlias);
					using (IDataReader reader2 = DatabaseUtility.ExecuteReader (transaction, sql, header_id)) {
						if (!reader2.Read ())
							return null;
						try {
							header = new MergeableFileHeader (
								key, reader2.GetUtcDateTime (0), reader2.GetUtcDateTime (1), null,
								AuthServerInfo.ParseArray (reader2.GetString (2)),
								(byte[])reader2.GetValue (3),
								Key.FromBase64 (reader2.GetString (4)));
							header.Content = parser.ParseHeader (reader2, 5);
						} catch {
							return null;
						}
					}
				}
			}

			if (header == null)
				return null;

			using (IDataReader reader = DatabaseUtility.ExecuteReader (transaction, "SELECT COUNT(id), MAX(created) FROM MMLC_MergeableRecords WHERE header_id = ?", header_id)) {
				reader.Read ();
				header.NumberOfRecords = reader.GetInt32 (0);
				try {
					header.LastModifiedTime = reader.GetUtcDateTime (1);
				} catch {
					header.LastModifiedTime = DateTime.MinValue;
				}
			}
			return header;
		}

		List<MergeableFileRecord> SelectRecords (IMergeableFileDatabaseParser parser, long header_id)
		{
			using (IDbConnection connection = CreateDBConnection ())
			using (IDbTransaction transaction = connection.BeginTransaction (IsolationLevel.ReadCommitted)) {
				return SelectRecords (transaction, parser, header_id);
			}
		}

		List<MergeableFileRecord> SelectRecords (IDbTransaction transaction, IMergeableFileDatabaseParser parser, long header_id)
		{
			string sql = string.Format ("SELECT t1.hash, t1.created, t1.lastManaged, t1.publickey, t1.sign, t1.auth_idx, t1.auth, {0} FROM MMLC_MergeableRecords as t1,{1} as {2} WHERE t1.header_id=? AND t1.id={2}.id", parser.ParseRecordFields, parser.RecordTableName, parser.TableAlias);
			List<MergeableFileRecord> list = new List<MergeableFileRecord> ();
			using (IDataReader reader = DatabaseUtility.ExecuteReader (transaction, sql, header_id)) {
				while (reader.Read ()) {
					try {
						byte[] sign = (reader.IsDBNull (4) ? null : (byte[])reader.GetValue (4));
						byte[] auth = (reader.IsDBNull (6) ? null : (byte[])reader.GetValue (6));
						Key pubKey = (reader.IsDBNull (3) ? null : Key.FromBase64 (reader.GetString (3)));
						MergeableFileRecord record = new MergeableFileRecord (null, reader.GetUtcDateTime (1), reader.GetUtcDateTime (2),
							Key.FromBase64 (reader.GetString (0)), pubKey, sign, (byte)reader.GetByte (5), auth);
						record.Content = parser.ParseRecord (reader, 7);
						list.Add (record);
					} catch {}
				}
			}
			return list;
		}

		void RemoveRecords (IDbTransaction transaction, long header_id, IMergeableFileDatabaseParser parser)
		{
			string sql = string.Format ("DELETE FROM {0} WHERE EXISTS (SELECT t1.id FROM MMLC_MergeableRecords as t1 WHERE t1.header_id=? AND t1.id={0}.id);",
				parser.RecordTableName);
			DatabaseUtility.ExecuteNonQuery (transaction, sql, header_id);
			
			sql = "DELETE FROM MMLC_MergeableRecords WHERE header_id=?;";
			DatabaseUtility.ExecuteNonQuery (transaction, sql, header_id);
		}
		#endregion

		#region AnonymousRouter.BoundaryNode ReceivedEvent Handlers
		void PublishMessage_Handler (object sender, BoundaryNodeReceivedEventArgs args)
		{
			PublishMessage msg = args.Request as PublishMessage;
			for (int i = 0; i < msg.Values.Length; i ++)
				_dht.LocalPut (msg.Values[i].Key, TimeSpan.FromMinutes (5), new DHTMergeableFileValue (args.RecipientKey, msg.Values[i].Hash, msg.Revision));
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
			_dht.BeginPut (msg.Key, TimeSpan.FromMinutes (5), new DHTMergeableFileValue (args.RecipientKey, msg.Hash, msg.Revision), null, null);
		}

		void CaptchaContainer_Handler (object sender, BoundaryNodeReceivedEventArgs args)
		{
			new Thread (CaptchaContainer_Thread).Start (args);
		}

		void CaptchaChallengeSegment_Received (object sender, ReceivedEventArgs args)
		{
			CaptchaChallengeSegment seg = (CaptchaChallengeSegment)args.Message;
			CaptchaSegmentReceived func;

			lock (_captcha_local) {
				if (!_captcha_local.TryGetValue (seg.ID, out func))
					return;
			}

			Logger.Log (LogLevel.Trace, this, "Received CAPTCHA Segment {0}/{1}", seg.SegmentIndex + 1, seg.NumberOfSegments);
			func (seg);
		}

		void CaptchaContainer_Thread (object o)
		{
			BoundaryNodeReceivedEventArgs args = (BoundaryNodeReceivedEventArgs)o;
			CaptchaContainer container = (CaptchaContainer)args.Request;

			byte[] buffer;
			IPEndPoint ep = container.EndPoint as IPEndPoint;
			try {
				if (ep == null) {
					DnsEndPoint dnsEp = container.EndPoint as DnsEndPoint;
					if (dnsEp == null)
						throw new NotImplementedException ();
					IPAddress[] adrs_list = Dns.GetHostAddresses (dnsEp.DNS);
					IPAddress ipv4adrs = null;
					for (int i = 0; i < adrs_list.Length; i ++)
						if (adrs_list[i].AddressFamily == AddressFamily.InterNetwork) {
							ipv4adrs = adrs_list[i];
							break;
						}
					if (ipv4adrs == null)
						throw new NotSupportedException ();
					ep = new IPEndPoint (ipv4adrs, dnsEp.Port);
				}

				WebRequest req = HttpWebRequest.Create ("http://" + ep.ToString () + "/");
				req.Timeout = 2000;
				req.Method = "POST";
				req.ContentLength = container.Encrypted.Length;
				req.ContentType = "application/octet-stream";
				req.GetRequestStream ().Write (container.Encrypted, 0, container.Encrypted.Length);
				using (WebResponse res = req.GetResponse ()) {
					buffer = new byte[(int)res.ContentLength];
					Stream strm = res.GetResponseStream ();
					int received = 0;
					while (received < buffer.Length) {
						int ret = strm.Read (buffer, received, buffer.Length - received);
						received += ret;
					}
				}
			} catch {
				CaptchaChallengeSegment seg = new CaptchaChallengeSegment (0, 1, new byte[0], container.ID);
				args.SendMessage (seg);
				return;
			}

			int maxSegmentSize = 800;
			int div = buffer.Length / maxSegmentSize;
			int rem = buffer.Length % maxSegmentSize;
			byte total = (byte)(div + (rem == 0 ? 0 : 1));
			for (int i = 0; i < div; i ++) {
				CaptchaChallengeSegment seg = new CaptchaChallengeSegment ((byte)i, total, buffer.CopyRange (i * maxSegmentSize, maxSegmentSize), container.ID);
				args.SendMessage (seg);
			}
			if (rem != 0) {
				CaptchaChallengeSegment seg = new CaptchaChallengeSegment ((byte)div, total, buffer.CopyRange (div * maxSegmentSize, rem), container.ID);
				args.SendMessage (seg);
			}
		}
		#endregion

		#region Manipulating MergeableFile/MergeabileFileRecord
		public MergeableFileHeader CreateNew (IHashComputable headerContent, AuthServerInfo[] authServers, MergeableFileRecord[] init_records)
		{
			ECKeyPair privateKey = ECKeyPair.Create (DefaultAlgorithm.ECDomainName);
			Key publicKey = Key.Create (privateKey);
			DateTime now = DateTime.UtcNow;
			MergeableFileHeader header = new MergeableFileHeader (privateKey, now, now, headerContent, authServers);
			if (Serializer.Instance.Serialize (header).Length > MergeableFileHeaderMaxSize)
				throw new OutOfMemoryException ();
			if (init_records != null && init_records.Length > 0) {
				ECDSA ecdsa = new ECDSA (privateKey);
				for (int i = 0; i < init_records.Length; i ++) {
					init_records[i].LastManagedTime = now;
					init_records[i].AuthorityIndex = byte.MaxValue;
					init_records[i].UpdateHash ();
					init_records[i].Authentication = ecdsa.SignHash (init_records[i].Hash.GetByteArray ());
					header.RecordsetHash = header.RecordsetHash.Xor (init_records[i].Hash);
				}
			}

			using (IDbConnection connection = CreateDBConnection ())
			using (IDbTransaction transaction = connection.BeginTransaction (IsolationLevel.Serializable)) {
				IMergeableFileDatabaseParser parser = null;
				long header_id;
				Insert (transaction, publicKey, privateKey);
				Insert (transaction, header, ref parser, out header_id);
				if (init_records != null && init_records.Length > 0) {
					for (int i = 0; i < init_records.Length; i ++)
						Insert (transaction, init_records[i], parser, header_id);
				}
				transaction.Commit ();
			}

			return header;
		}

		public MergeableFileHeader GetMergeableFileHeader (Key key)
		{
			using (IDbConnection connection = CreateDBConnection ())
			using (IDbTransaction transaction = connection.BeginTransaction (IsolationLevel.ReadCommitted)) {
				return SelectHeader (transaction, key) as MergeableFileHeader;
			}
		}

		public MergeableFileHeader[] GetHeaderList ()
		{
			List<MergeableFileHeader> headers = new List<MergeableFileHeader> ();
			using (IDbConnection connection = CreateDBConnection ())
			using (IDbTransaction transaction = connection.BeginTransaction (IsolationLevel.ReadCommitted)) {
				using (IDataReader reader1 = DatabaseUtility.ExecuteReader (transaction, "SELECT key FROM MMLC_Keys WHERE type=2")) {
					while (reader1.Read ()) {
						try {
							MergeableFileHeader header = SelectHeader (transaction, Key.FromBase64 (reader1.GetString (0))) as MergeableFileHeader;
							if (header != null)
								headers.Add (header);
						} catch {}
					}
				}
			}
			return headers.ToArray ();
		}

		public List<MergeableFileRecord> GetRecords (Key key, out MergeableFileHeader header)
		{
			List<MergeableFileRecord> recordset = new List<MergeableFileRecord> ();
			using (IDbConnection connection = CreateDBConnection ())
			using (IDbTransaction transaction = connection.BeginTransaction (IsolationLevel.ReadCommitted)) {
				IMergeableFileDatabaseParser parser;
				long header_id;
				header = SelectHeader (transaction, key, out parser, out header_id) as MergeableFileHeader;
				if (header == null)
					return null;
				return SelectRecords (transaction, parser, header_id);
			}
		}

		public void AppendRecord (Key key, MergeableFileRecord record)
		{
			if (key == null || record == null || record.Content == null)
				throw new ArgumentNullException ();

			using (IDbConnection connection = CreateDBConnection ())
			using (IDbTransaction transaction = connection.BeginTransaction (IsolationLevel.Serializable)) {
				IMergeableFileDatabaseParser parser;
				long header_id;
				MergeableFileHeader current = SelectHeader (transaction, key, out parser, out header_id) as MergeableFileHeader;
				if (current == null)
					throw new KeyNotFoundException ();

				record.LastManagedTime = current.LastManagedTime;
				record.UpdateHash ();
				try {
					Insert (transaction, record, parser, header_id);
					current.RecordsetHash = current.RecordsetHash.Xor (record.Hash);
					Update (transaction, current, parser, header_id, current); // Update RecordsetHash Field
					transaction.Commit ();
				} catch {
					// Rollback
					return;
				}
			}
			lock (_localChangedList) {
				_localChangedList.Add (key);
			}
		}

		public void Manage (Key key, IHashComputable new_header_content, AuthServerInfo[] authServers, Key[] keep_records, MergeableFileRecord[] append_records)
		{
			if (key == null)
				throw new ArgumentNullException ();
			if (keep_records == null)
				keep_records = new Key[0];
			if (append_records == null)
				append_records = new MergeableFileRecord[0];

			IMergeableFileDatabaseParser parser;
			long header_id;
			MergeableFileHeader cur_header;
			ECKeyPair privateKey;

			HashSet<Key> keepSet = new HashSet<Key> (keep_records);
			List<MergeableFileRecord> new_records = new List<MergeableFileRecord> (keep_records.Length + append_records.Length);
			List<MergeableFileRecord> old_records;

			using (IDbConnection connection = CreateDBConnection ())
			using (IDbTransaction transaction = connection.BeginTransaction (IsolationLevel.ReadCommitted)) {
				cur_header = SelectHeader (transaction, key, out parser, out header_id) as MergeableFileHeader;
				if (cur_header == null)
					throw new KeyNotFoundException ();

				// 秘密鍵を選択
				privateKey = ECKeyPair.CreatePrivate (DefaultAlgorithm.ECDomainName,
					(byte[])DatabaseUtility.ExecuteScalar (transaction, "SELECT private_key FROM MMLC_PrivateKeys WHERE key=?", key.ToBase64String ()));

				old_records = SelectRecords (transaction, parser, header_id);
			}

			// ヘッダの内容を更新し、再署名
			MergeableFileHeader new_header = new MergeableFileHeader (privateKey, cur_header.CreatedTime, DateTime.UtcNow,
				new_header_content == null ? cur_header.Content : new_header_content, authServers);
			
			// 保持するレコードのみを抽出
			for (int i = 0; i < old_records.Count; i ++)
				if (keepSet.Contains (old_records[i].Hash))
					new_records.Add (old_records[i]);

			// 新しく追加するレコードを追加
			for (int i = 0; i < append_records.Length; i ++)
				new_records.Add (append_records[i]);

			// 管理更新日時を更新し、管理鍵で各レコードに署名
			ECDSA ecdsa = new ECDSA (privateKey);
			Key hash = new_header.RecordsetHash;
			for (int i = 0; i < new_records.Count; i ++) {
				new_records[i].LastManagedTime = new_header.LastManagedTime;
				new_records[i].AuthorityIndex = byte.MaxValue;
				new_records[i].UpdateHash ();
				new_records[i].Authentication = ecdsa.SignHash (new_records[i].Hash.GetByteArray ());
				hash = hash.Xor (new_records[i].Hash);
			}
			new_header.RecordsetHash = hash;

			using (IDbConnection connection = CreateDBConnection ())
			using (IDbTransaction transaction = connection.BeginTransaction (IsolationLevel.Serializable)) {
				// 既存のレコードを全て削除
				RemoveRecords (transaction, header_id, parser);

				// ヘッダを書き込み
				Update (transaction, new_header, parser, header_id, cur_header);

				// レコードを追加
				for (int i = 0; i < new_records.Count; i ++)
					Insert (transaction, new_records[i], parser, header_id);

				transaction.Commit ();
			}
		}

		public CaptchaChallengeData GetCaptchaChallengeData (AuthServerInfo info, byte[] hash)
		{
			SymmetricKey key = new SymmetricKey (SymmetricAlgorithmType.Camellia, openCrypto.RNG.GetRNGBytes (16), openCrypto.RNG.GetRNGBytes (16),
				openCrypto.CipherModePlus.CTR, System.Security.Cryptography.PaddingMode.ISO10126, false);
			
			object lock_obj = new object ();
			byte[][] segments = null;
			int total_size = 0;
			ManualResetEvent done = new ManualResetEvent (false);
			CaptchaSegmentReceived func = delegate (CaptchaChallengeSegment seg) {
				lock (lock_obj) {
					if (segments == null)
						segments = new byte[seg.NumberOfSegments][];
					segments[seg.SegmentIndex] = seg.Segment;
					total_size = 0;
					for (int i = 0; i < segments.Length; i ++) {
						if (segments[i] == null)
							return;
						total_size += segments[i].Length;
					}
					done.Set ();
				}
			};

			int id;
			lock (_captcha_local) {
				while (true) {
					id = BitConverter.ToInt32 (openCrypto.RNG.GetRNGBytes (4), 0);
					if (!_captcha_local.ContainsKey (id))
						break;
				}
				_captcha_local.Add (id, func);
			}
			try {
				CaptchaContainer container = new CaptchaContainer (info.EndPoint, id, info.PublicKey, new CaptchaChallengeRequest (hash, key.IV, key.Key));
				_uploadSide.MessagingSocket.Send (container, null);
				if (!done.WaitOne (1000 * 16)) {
					// timeout...
					return null;
				}

				byte[] encrypted = new byte[total_size];
				for (int i = 0, q = 0; i < segments.Length; i ++) {
					Buffer.BlockCopy (segments[i], 0, encrypted, q, segments[i].Length);
					q += segments[i].Length;
				}

				return Serializer.Instance.Deserialize (key.Decrypt (encrypted, 0, encrypted.Length)) as CaptchaChallengeData;
			} finally {
				lock (_captcha_local) {
					_captcha_local.Remove (id);
				}
			}
		}

		public byte[] VerifyCaptchaChallenge (AuthServerInfo info, byte[] hash, byte[] token, byte[] answer)
		{
			SymmetricKey key = new SymmetricKey (SymmetricAlgorithmType.Camellia, openCrypto.RNG.GetRNGBytes (16), openCrypto.RNG.GetRNGBytes (16),
				openCrypto.CipherModePlus.CTR, System.Security.Cryptography.PaddingMode.ISO10126, false);
			ManualResetEvent done = new ManualResetEvent (false);
			CaptchaVerifyResult result = null;
			CaptchaSegmentReceived func = delegate (CaptchaChallengeSegment seg) {
				result = Serializer.Instance.Deserialize (key.Decrypt (seg.Segment, 0, seg.Segment.Length)) as CaptchaVerifyResult;
				done.Set ();
			};

			int id;
			lock (_captcha_local) {
				while (true) {
					id = BitConverter.ToInt32 (openCrypto.RNG.GetRNGBytes (4), 0);
					if (!_captcha_local.ContainsKey (id))
						break;
				}
				_captcha_local.Add (id, func);
			}
			try {
				CaptchaContainer container = new CaptchaContainer (info.EndPoint, id, info.PublicKey, new CaptchaAnswer (hash, token, answer, key.IV, key.Key));
				_uploadSide.MessagingSocket.Send (container, null);
				if (!done.WaitOne (1000 * 16)) {
					// timeout...
					return null;
				}
				if (result == null)
					return null;
				return result.Signature;
			} finally {
				lock (_captcha_local) {
					_captcha_local.Remove (id);
				}
			}
		}

		public MergeableFileHeader[] GetOwnMergeableFiles ()
		{
			List<MergeableFileHeader> headers = new List<MergeableFileHeader> ();

			using (IDbConnection connection = CreateDBConnection ())
			using (IDbTransaction transaction = connection.BeginTransaction (IsolationLevel.ReadCommitted)) {
				List<Key> keys = new List<Key> ();
				using (IDataReader reader = DatabaseUtility.ExecuteReader (transaction, "SELECT key FROM MMLC_PrivateKeys")) {
					while (reader.Read ()) {
						try {
							keys.Add (Key.FromBase64 (reader.GetString (0)));
						} catch {}
					}
				}
				
				for (int i = 0; i < keys.Count; i ++) {
					try {
						MergeableFileHeader header = SelectHeader (transaction, keys[i]) as MergeableFileHeader;
						if (header != null)
							headers.Add (header);
					} catch {}
				}
			}

			return headers.ToArray ();
		}

		void Touch (MergeableFileHeader header)
		{
			lock (_rePutList) {
				if (_rePutList.Contains (header.Key))
					return;
				_rePutList.Add (header.Key);
			}
			FastPublishMessage msg = new FastPublishMessage (header.Key, header.RecordsetHash, (uint)Interlocked.Increment (ref _revision));
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

					MergeableFileHeader[] headers = GetHeaderList ();
					for (int i = 0; i < headers.Length; i ++)
						_rePutList.Add (headers[i].Key);
					_rePutNextTime = DateTime.Now + _rePutInterval;
					if (_rePutList.Count == 0)
						return;
				}

				int num = Math.Min (10, _rePutList.Count);
				List<PublishData> values = new List<PublishData> ();
				int count = 0;
				using (IDbConnection connection = CreateDBConnection ())
				using (IDbTransaction transaction = connection.BeginTransaction (IsolationLevel.ReadCommitted)) {
					for (int i = 0; i < _rePutList.Count && count < num; i++) {
						MergeableFileHeader header = SelectHeader (transaction, _rePutList[i]) as MergeableFileHeader;
						if (header == null)
							continue;
						values.Add (new PublishData (_rePutList[i], header.RecordsetHash));
						count++;
					}
				}
				_rePutList.RemoveRange (0, count);
				_uploadSide.MessagingSocket.BeginInquire (new PublishMessage (values.ToArray (), (uint)Interlocked.Increment (ref _revision)), null, null, null);
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
		}
		#endregion

		#region Merge

		public void StartMerge (Key key, EventHandler<MergeDoneCallbackArgs> callback, object state)
		{
			bool forseFastPut = false;
			lock (_localChangedList) {
				forseFastPut = _localChangedList.Remove (key);
			}

			MergeProcess proc = null;
			lock (_mergingFiles) {
				_mergingFiles.TryGetValue (key, out proc);
				if (proc == null) {
					proc = new MergeProcess (this, key, forseFastPut, callback, state);
					_mergingFiles.Add (key, proc);
					proc.Thread.Start ();
					return;
				}
			}
			if (!proc.AddNewCallback (callback, state)) {
				try {
					callback (null, new MergeDoneCallbackArgs (state));
				} catch {}
			}
		}

		public void MergeFinished (Key key)
		{
			lock (_mergingFiles) {
				_mergingFiles.Remove (key);
			}
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

			MergeableFileHeader header;
			long header_id;
			IMergeableFileDatabaseParser parser;
			using (IDbConnection connection = CreateDBConnection ())
			using (IDbTransaction transaction = connection.BeginTransaction (IsolationLevel.ReadCommitted)) {
				header = SelectHeader (transaction, key, out parser, out header_id) as MergeableFileHeader;
			}
			if (header == null) {
				args.Reject ();
				return;
			}
			args.Accept (header, new object[] {header, parser, header_id});
		}

		void AnonymousRouter_Accepted (object sender, AcceptedEventArgs args)
		{
			object[] set = (object[])args.State;
			MergeableFileHeader header = (MergeableFileHeader)set[0];
			IMergeableFileDatabaseParser parser = (IMergeableFileDatabaseParser)set[1];
			long header_id = (long)set[2];
			StreamSocket sock = new StreamSocket (args.Socket, AnonymousRouter.DummyEndPoint, MaxDatagramSize, _anonStrmSockInt);
			args.Socket.InitializedEventHandlers ();
			MergeProcess proc = new MergeProcess (this, sock, args.Payload as MergeableFileHeader, header, parser, header_id);
			proc.Thread.Start ();
		}

		sealed class MergeProcess
		{
			const int MAX_PARALLEL_MERGE = 5;
			MMLC _mmlc;
			Key _key;
			
			MergeableFileHeader _cur_header;
			IMergeableFileDatabaseParser _parser;
			long _header_id;

			Thread _thrd;
			List<EventHandler<MergeDoneCallbackArgs>> _callbacks;
			List<object> _states;
			bool _isInitiator, _forseFastPut = false;
			object[] _accepterSideValues;

			public MergeProcess (MMLC mmlc, Key key, bool forseFastPut, EventHandler<MergeDoneCallbackArgs> callback, object state)
			{
				_mmlc = mmlc;
				_key = key;

				using (IDbConnection connection = mmlc.CreateDBConnection ())
				using (IDbTransaction transaction = connection.BeginTransaction (IsolationLevel.ReadCommitted)) {
					_cur_header = mmlc.SelectHeader (transaction, key, out _parser, out _header_id) as MergeableFileHeader;
				}
				if (_cur_header != null)
					_key = null;
				_forseFastPut = forseFastPut;
				_isInitiator = true;
				_thrd = new Thread (Process);
				_callbacks = new List<EventHandler<MergeDoneCallbackArgs>> (1);
				_states = new List<object> (1);
				if (callback != null) {
					_callbacks.Add (callback);
					_states.Add (state);
				}
			}

			public MergeProcess (MMLC mmlc, StreamSocket sock, MergeableFileHeader other_header, MergeableFileHeader cur_header, IMergeableFileDatabaseParser parser, long header_id)
			{
				_mmlc = mmlc;
				_cur_header = cur_header;
				_parser = parser;
				_header_id = header_id;
				_accepterSideValues = new object[] {sock, other_header};
				_isInitiator = false;
				_thrd = new Thread (Process);
			}

			public bool AddNewCallback (EventHandler<MergeDoneCallbackArgs> callback, object state)
			{
				if (callback == null)
					return true;
				lock (_callbacks) {
					if (_states == null)
						return false;
					_callbacks.Add (callback);
					_states.Add (state);
					return true;
				}
			}

			public Thread Thread {
				get { return _thrd; }
			}

			void Process ()
			{
				Key beforeHash = _cur_header == null ? null : _cur_header.RecordsetHash;
				if (_isInitiator) {
					InitiatorSideProcess ();
				} else {
					AccepterSideProcess ();
				}
				if (_cur_header != null)// && (_forseFastPut || beforeHash == null || !beforeHash.Equals (_cur_header.RecordsetHash)))
					_mmlc.Touch (_cur_header);
			}

			void InitiatorSideProcess ()
			{
				try {
					InitiatorSideDHTLookupProcess ();
				} catch (Exception exception) {
					Logger.Log (LogLevel.Trace, this, "I: DHTルックアップ中に例外. {0}", exception.ToString ());
				} finally {
					_mmlc.MergeFinished (_key != null ? _key : _cur_header.Key);
					lock (_callbacks) {
						for (int i = 0; i < _callbacks.Count; i++) {
							try {
								_callbacks[i] (null, new MergeDoneCallbackArgs (_states[i]));
							} catch { }
						}
						_callbacks.Clear ();
						_states = null;
					}
				}
			}

			void InitiatorSideDHTLookupProcess ()
			{
				Logger.Log (LogLevel.Trace, this, "I: START");
				Logger.Log (LogLevel.Trace, this, "I: DHTに問い合わせ中...");
				IAsyncResult ar = _mmlc._uploadSide.MessagingSocket.BeginInquire (new DHTLookupRequest (_key != null ? _key : _cur_header.Key), null, null, null);
				DHTLookupResponse dhtres = _mmlc._uploadSide.MessagingSocket.EndInquire (ar) as DHTLookupResponse;
				Logger.Log (LogLevel.Trace, this, "I: DHTから応答受信. 結果={0}件", dhtres == null || dhtres.Values == null ? 0 : dhtres.Values.Length);
				if (dhtres == null || dhtres.Values == null || dhtres.Values.Length == 0)
					return;
				foreach (DHTMergeableFileValue value in dhtres.Values)
					Logger.Log (LogLevel.Trace, this, "I:   {0}/{1}", value.Holder.ToString ().Substring (0, 8), value.Hash.ToString ().Substring (0, 8));
				List<WaitHandle> parallelMergeThreads = new List<WaitHandle> ();
				HashSet<Key> mergingHashSet = new HashSet<Key> ();
				for (int i = 0; i < dhtres.Values.Length && parallelMergeThreads.Count < MAX_PARALLEL_MERGE; i++) {
					try {
						if (_key == null && _cur_header.RecordsetHash.Equals (dhtres.Values[i].Hash)) continue;
						if (mergingHashSet.Contains (dhtres.Values[i].Hash)) continue;
						ar = _mmlc._ar.BeginConnect (_mmlc._uploadSide.Key, dhtres.Values[i].Holder, AnonymousConnectionType.HighThroughput,
							_key != null ? (object)_key : (object)_cur_header, null, null);
						Logger.Log (LogLevel.Trace, this, "I: {0}へ接続", dhtres.Values[i].Holder.ToString ().Substring (0, 8));
						mergingHashSet.Add (dhtres.Values[i].Hash);
						ManualResetEvent done = new ManualResetEvent (false);
						parallelMergeThreads.Add (done);
						new Thread (InitiatorSideMergeProcess).Start (new object[] {done, ar});
					} catch {}
				}
				if (parallelMergeThreads.Count == 0) {
					Logger.Log (LogLevel.Trace, this, "I: 接続先が見つかりません");
					return;
				}
				WaitHandle.WaitAll (parallelMergeThreads.ToArray ());
			}

			void InitiatorSideMergeProcess (object o)
			{
				object[] args = (object[])o;
				ManualResetEvent done = args[0] as ManualResetEvent;
				StreamSocket sock = null;
				try {
					IAnonymousSocket anon_sock;
					DateTime dt = DateTime.Now;
					try {
						anon_sock = _mmlc._ar.EndConnect (args[1] as IAsyncResult);
					} catch {
						Logger.Log (LogLevel.Trace, this, "I: 接続失敗");
						return;
					}
					Logger.Log (LogLevel.Trace, this, "I: 接続OK");
					sock = new StreamSocket (anon_sock, null, MaxDatagramSize, DateTime.Now.Subtract (dt), _mmlc._anonStrmSockInt);
					anon_sock.InitializedEventHandlers ();
					InitiatorSideMergeProcess (sock, anon_sock.PayloadAtEstablishing as MergeableFileHeader);
				} catch (Exception exception) {
					Logger.Log (LogLevel.Trace, this, "I: マージ中に例外. {0}", exception.ToString ());
				} finally {
					done.Set ();
					if (sock != null) {
						try {
							sock.Shutdown ();
						} catch {}
						Logger.Log (LogLevel.Trace, this, "I: StreamSocket is shutdown");
						try {
							sock.Dispose ();
						} catch {}
					}
				}
			}

			void InitiatorSideMergeProcess (StreamSocket sock, MergeableFileHeader header)
			{
				if (header == null || !(_key == null ? _cur_header.Key : _key).Equals (header.Key) || !header.Verify ()) {
					Logger.Log (LogLevel.Trace, this, "I: 不正なヘッダだよん");
					return;
				}
				Merge (header, true);
				Logger.Log (LogLevel.Trace, this, "I: ヘッダを受信");

				if (_cur_header.RecordsetHash.Equals (header.RecordsetHash)) {
					Logger.Log (LogLevel.Trace, this, "I: 同じデータなのでマージ処理終了");
					return;
				}

				byte[] buf = new byte[4];
				Logger.Log (LogLevel.Trace, this, "I: 保持しているレコード一覧を送信中");
				List<MergeableFileRecord> db_records = _mmlc.SelectRecords (_parser, _header_id);
				List<Key> list = new List<Key> ();
				for (int i = 0; i < db_records.Count; i++)
					list.Add (db_records[i].Hash);
				SendMessage (sock, new MergeableRecordList (list.ToArray ()));

				Logger.Log (LogLevel.Trace, this, "I: レコード一覧を受信中...");
				MergeableRecordList recordList = ReceiveMessage (sock, ref buf) as MergeableRecordList;
				if (recordList == null) {
					Logger.Log (LogLevel.Error, this, "I: 受信したレコード一覧のデシリアライズに失敗");
					return;
				}

				Logger.Log (LogLevel.Trace, this, "I: レコードを送信中");
				HashSet<Key> sendSet = new HashSet<Key> (recordList.HashList);
				List<MergeableFileRecord> sendList = new List<MergeableFileRecord> ();
				for (int i = 0; i < db_records.Count; i++) {
					if (sendSet.Contains (db_records[i].Hash))
						sendList.Add (db_records[i]);
				}
				SendMessage (sock, new MergeableRecords (sendList.ToArray ()));

				Logger.Log (LogLevel.Trace, this, "I: レコードを受信中...");
				MergeableRecords records = ReceiveMessage (sock, ref buf) as MergeableRecords;
				if (recordList == null) {
					Logger.Log (LogLevel.Error, this, "I: 受信したレコードのデシリアライズに失敗");
					return;
				}

				Logger.Log (LogLevel.Trace, this, "I: マージ中");
				Merge (records);
				Logger.Log (LogLevel.Trace, this, "I: マージ完了!!!");
			}

			void AccepterSideProcess ()
			{
				StreamSocket sock = _accepterSideValues[0] as StreamSocket;
				MergeableFileHeader other_header = _accepterSideValues[1] as MergeableFileHeader;
				try {
					AccepterSideMergeProcess (sock, other_header);
				} catch (Exception exception) {
					Logger.Log (LogLevel.Trace, this, "A: マージ中に例外. {0}", exception.ToString ());
				} finally {
					try {
						sock.Shutdown ();
					} catch {}
					Logger.Log (LogLevel.Trace, this, "A: StreamSocket is shutdown");
					try {
						sock.Dispose ();
					} catch {}
				}
			}

			void AccepterSideMergeProcess (StreamSocket sock, MergeableFileHeader other_header)
			{
				Logger.Log (LogLevel.Trace, this, "A: START");
				if (other_header != null) {
					if (_cur_header.LastManagedTime < other_header.LastManagedTime && other_header.Verify ())
							Merge (other_header, false);

					if (_cur_header.RecordsetHash.Equals (other_header.RecordsetHash)) {
						Logger.Log (LogLevel.Trace, this, "A: 同じデータなのでマージ処理終了");
						return;
					}
				}

				byte[] buf = new byte[4];
				Logger.Log (LogLevel.Trace, this, "A: 保持しているレコード一覧を受信中...");
				MergeableRecordList recordList = ReceiveMessage (sock, ref buf) as MergeableRecordList;
				if (recordList == null) {
					Logger.Log (LogLevel.Error, this, "A: 受信したレコード一覧のデシリアライズに失敗");
					return;
				}

				HashSet<Key> otherSet = new HashSet<Key> (recordList.HashList);
				List<MergeableFileRecord> sendList = new List<MergeableFileRecord> ();
				Logger.Log (LogLevel.Trace, this, "A: 保持しているレコード一覧を送信中");
				List<MergeableFileRecord> db_records = _mmlc.SelectRecords (_parser, _header_id);
				for (int i = 0; i < db_records.Count; i++)
					if (!otherSet.Remove (db_records[i].Hash))
						sendList.Add (db_records[i]);
				Key[] keys = new Key[otherSet.Count];
				otherSet.CopyTo (keys);
				SendMessage (sock, new MergeableRecordList (keys));

				Logger.Log (LogLevel.Trace, this, "A: レコードを送信中");
				SendMessage (sock, new MergeableRecords (sendList.ToArray ()));

				Logger.Log (LogLevel.Trace, this, "A: レコードを受信中...");
				MergeableRecords records = ReceiveMessage (sock, ref buf) as MergeableRecords;
				if (recordList == null) {
					Logger.Log (LogLevel.Error, this, "A: 受信したレコードのデシリアライズに失敗");
					return;
				}

				Logger.Log (LogLevel.Trace, this, "A: マージ中");
				Merge (records);
				Logger.Log (LogLevel.Trace, this, "A: マージ完了!!!");
			}

			void SendMessage (StreamSocket sock, object msg)
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
				
				sock.Send (buf, 0, buf.Length);
			}

			object ReceiveMessage (StreamSocket sock, ref byte[] buf)
			{
				int size = 4, received = 0;
				while (received < size)
					received += sock.Receive (buf, received, size - received);
				size = (buf[0] << 24) | (buf[1] << 16) | (buf[2] << 8) | buf[3];
				received = 0;
				if (buf.Length < size)
					buf = new byte[size];
				while (received < size)
					received += sock.Receive (buf, received, size - received);
				return Serializer.Instance.Deserialize (buf);
			}

			void Merge (MergeableFileHeader new_header, bool isInitiator)
			{
				using (IDbConnection connection = _mmlc.CreateDBConnection ())
				using (IDbTransaction transaction = connection.BeginTransaction (IsolationLevel.Serializable)) {
					_cur_header = _mmlc.SelectHeader (transaction, (_key == null ? _cur_header.Key : _key), out _parser, out _header_id) as MergeableFileHeader;
					if (_key == null || (_key != null && _cur_header != null)) {
						if (new_header.LastManagedTime > _cur_header.LastManagedTime) {
							// 管理更新されているのでヘッダを更新し、既存のレコードを全て削除
							MergeableFileHeader new_header2 = new_header.CopyBasisInfo ();
							_mmlc.Update (transaction, new_header2, _parser, _header_id, _cur_header);
							_mmlc.RemoveRecords (transaction, _header_id, _parser);
							transaction.Commit ();
							_cur_header = new_header2;
							Logger.Log (LogLevel.Trace, this, "{0}: 管理更新を検出. ローカルのデータを削除", isInitiator ? 'I' : 'A');
						} else {
							// 管理更新されていない場合は、リモートのヘッダを無視
						}
					} else {
						// ヘッダがないので受信したヘッダを利用
						_cur_header = new_header.CopyBasisInfo ();
						_mmlc.Insert (transaction, _cur_header, ref _parser, out _header_id);
						_key = null;
						transaction.Commit ();
					}
				}
			}

			void Merge (MergeableRecords records)
			{
				using (IDbConnection connection = _mmlc.CreateDBConnection ())
				using (IDbTransaction transaction = connection.BeginTransaction (IsolationLevel.Serializable)) {
					_cur_header = _mmlc.SelectHeader (transaction, _cur_header.Key, out _parser, out _header_id) as MergeableFileHeader;
					if (_cur_header == null) {
						// えー
						throw new Exception ();
					}
					Key hash = _cur_header.RecordsetHash;
					for (int i = 0; i < records.Records.Length; i ++) {
						try {
							_mmlc.Insert (transaction, records.Records[i], _parser, _header_id);
							hash = hash.Xor (records.Records[i].Hash);
						} catch {}
					}
					_cur_header.RecordsetHash = hash;
					_mmlc.Update (transaction, _cur_header, _parser, _header_id, _cur_header);
					transaction.Commit ();
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

			[SerializableFieldId (1)]
			uint _rev;

			public PublishMessage (PublishData[] values, uint rev)
			{
				_values = values;
				_rev = rev;
			}

			public PublishData[] Values {
				get { return _values; }
			}

			public uint Revision {
				get { return _rev; }
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

			[SerializableFieldId (2)]
			uint _rev;

			public FastPublishMessage (Key key, Key hash, uint rev)
			{
				_key = key;
				_hash = hash;
				_rev = rev;
			}

			public Key Key {
				get { return _key; }
			}

			public Key Hash {
				get { return _hash; }
			}

			public uint Revision {
				get { return _rev; }
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

			[SerializableFieldId (2)]
			uint _rev;

			public DHTMergeableFileValue (Key holder, Key hash, uint rev)
			{
				_holder = holder;
				_hash = hash;
				_rev = rev;
			}

			public Key Holder {
				get { return _holder; }
			}

			public Key Hash {
				get { return _hash; }
			}

			public uint Revision {
				get { return _rev; }
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
				static TimeSpan _minRePutInterval = TimeSpan.FromSeconds (30);
				public static ValueMerger Instance = new ValueMerger ();
				ValueMerger () { }

				#region ILocalHashTableValueMerger Members

				public object Merge (object value, object new_value, TimeSpan lifeTime)
				{
					StoreValue store_value = value as StoreValue;
					if (store_value == null)
						store_value = new StoreValue ();
					store_value.Merge (new_value as DHTMergeableFileValue, lifeTime, _minRePutInterval);
					return store_value;
				}

				public object[] GetEntries (object value, int max_num)
				{
					return (value as StoreValue).GetEntries (max_num);
				}

				public void ExpirationCheck (object value)
				{
					(value as StoreValue).ExpirationCheck ();
				}

				public int GetCount (object value)
				{
					return (value as StoreValue).Count;
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

					public void Merge (DHTMergeableFileValue new_value, TimeSpan lifeTime, TimeSpan minRePutInterval)
					{
						HolderInfo info;
						List<HolderInfo> list;

						if (_dic2.TryGetValue (new_value.Holder, out info)) {
							if (info.Revision >= new_value.Revision)
								return;

							info.Extend (lifeTime, minRePutInterval);
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
							info.UnmarkSend ();
						} else {
							info = new HolderInfo (new_value, lifeTime);
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
								result.Add (tmp[i].Entry);
						}
						return result.ToArray().RandomSelection (max_num);
					}

					public void ExpirationCheck ()
					{
						List<Key> removeHash = new List<Key> ();
						foreach (KeyValuePair<Key,List<HolderInfo>> pair in _dic) {
							List<HolderInfo> list = pair.Value;
							for (int i = 0; i < list.Count; i++) {
								if (list[i].IsExpired ()) {
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
								q ++;
							}
						}
						HolderInfo[] array = result.ToArray ().RandomSelection (max_num);
						DHTEntry[] entries = new DHTEntry [array.Length];
						for (int i = 0; i < entries.Length; i ++)
							entries[i] = new DHTEntry (key, typeId, array[i].GetValueToSend (), array[i].LifeTime);
						return entries;
					}

					public void UnmarkSendFlag (DHTMergeableFileValue mark_value)
					{
						HolderInfo info;
						if (_dic2.TryGetValue (mark_value.Holder, out info))
							info.UnmarkSend ();
					}

					public int Count {
						get { return _dic2.Count; }
					}
				}

				class HolderInfo : LocalHashTableValueMerger<DHTMergeableFileValue>.HolderInfo, IEquatable<HolderInfo>
				{
					public HolderInfo (DHTMergeableFileValue value, TimeSpan lifeTime)
						: base (value, lifeTime)
					{
					}

					public Key Holder {
						get { return (_entry as DHTMergeableFileValue).Holder; }
					}

					public Key Hash {
						get { return (_entry as DHTMergeableFileValue).Hash; }
						set { (_entry as DHTMergeableFileValue)._hash = value; }
					}

					public uint Revision {
						get { return (_entry as DHTMergeableFileValue).Revision; }
					}

					public bool Equals (HolderInfo other)
					{
						return _entry.Equals (other._entry);
					}
				}
			}
		}
		#endregion
	}
}
