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
using System.Data;
using System.Text;
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.DFS.MMLC;
using p2pncs.Security.Cryptography;
using p2pncs.Utility;

namespace p2pncs.Wiki
{
	class WikiParser : IMergeableFileDatabaseParser
	{
		const string ALIAS = "w";
		const string HEADER_TABLE_NAME = "Wiki_Headers";
		const string HEADER_FIELDS = "w.title, w.freeze";
		const string RECORD_TABLE_NAME = "Wiki_Records";
		const string RECORD_FIELDS = "w.page_name, w.parent, w.name, w.body, w.raw_body, w.markup, w.compress, w.diff";

		static string INSERT_HEADER_SQL = string.Format ("INSERT INTO {0} (id, title, freeze) VALUES (?, ?, ?)", HEADER_TABLE_NAME);
		static string UPDATE_HEADER_SQL = string.Format ("UPDATE {0} SET title=?, freeze=? WHERE id=?", HEADER_TABLE_NAME);
		static string INSERT_RECORD_SQL = string.Format ("INSERT INTO {0} (id, page_name, parent, name, body, raw_body, markup, compress, diff) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)", RECORD_TABLE_NAME);

		static WikiParser _instance = new WikiParser ();
		WikiParser () {}
		public static WikiParser Instance {
			get { return _instance; }
		}

		public void Init (IDbTransaction transaction)
		{
			DatabaseUtility.ExecuteNonQuery (transaction, "CREATE TABLE IF NOT EXISTS Wiki_Headers (id INTEGER PRIMARY KEY REFERENCES MMLC_MergeableHeaders(id), title TEXT, freeze INTEGER);");
			DatabaseUtility.ExecuteNonQuery (transaction, "CREATE TABLE IF NOT EXISTS Wiki_Records (id INTEGER PRIMARY KEY REFERENCES MMLC_MergeableRecords(id), page_name TEXT, parent TEXT, name TEXT, body TEXT, raw_body BLOB, markup INTEGER, compress INTEGER, diff INTEGER);");
		}

		public IHashComputable ParseHeader (IDataRecord record, int offset)
		{
			return new WikiHeader (record.GetString (offset + 0), record.GetBoolean (offset + 1));
		}

		public IHashComputable ParseRecord (IDataRecord record, int offset)
		{
			return new WikiRecord (record.GetString (offset + 0),
				record.IsDBNull (offset + 1) ? null : Key.FromBase64 (record.GetString (offset + 1)),
				record.GetString (offset + 2), (WikiMarkupType)record.GetInt32 (offset + 5),
				record.GetString (offset + 3), (byte[])record.GetValue (offset + 4),
				(WikiCompressType)record.GetInt32 (offset + 6), (WikiDiffType)record.GetInt32 (offset + 7));
		}

		public void Insert (IDbTransaction transaction, long id, MergeableFileHeader header)
		{
			WikiHeader h = header.Content as WikiHeader;
			DatabaseUtility.ExecuteNonQuery (transaction, INSERT_HEADER_SQL, id, h.Title, h.IsFreeze);
		}

		public void Insert (IDbTransaction transaction, long id, MergeableFileRecord record)
		{
			WikiRecord r = record.Content as WikiRecord;
			r.SyncBodyAndRawBody ();
			DatabaseUtility.ExecuteNonQuery (transaction, INSERT_RECORD_SQL, id, r.PageName,
				r.ParentHash == null ? null : r.ParentHash.ToBase64String (), r.Name,
				r.Body, r.RawBody, (int)r.MarkupType, (int)r.CompressType, (int)r.DiffType);
		}

		public void Update (IDbTransaction transaction, long id, MergeableFileHeader header)
		{
			WikiHeader h = header.Content as WikiHeader;
			DatabaseUtility.ExecuteNonQuery (transaction, UPDATE_HEADER_SQL, h.Title, h.IsFreeze, id);
		}

		public int TypeId {
			get { return 1; }
		}

		public Type HeaderContentType {
			get { return typeof (WikiHeader); }
		}

		public Type RecordContentType {
			get { return typeof (WikiRecord); }
		}

		public string HeaderTableName {
			get { return HEADER_TABLE_NAME; }
		}

		public string RecordTableName {
			get { return RECORD_TABLE_NAME; }
		}

		public string TableAlias {
			get { return ALIAS; }
		}

		public string ParseHeaderFields {
			get { return HEADER_FIELDS; }
		}

		public string ParseRecordFields {
			get { return RECORD_FIELDS; }
		}
	}
}
