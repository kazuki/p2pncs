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
using p2pncs.Net.Overlay.DFS.MMLC;
using p2pncs.Security.Cryptography;
using p2pncs.Utility;

namespace p2pncs
{
	class SimpleBBSParser : IMergeableFileDatabaseParser
	{
		const string ALIAS = "sbt";
		const string HEADER_TABLE_NAME = "SimpleBBS_Headers";
		const string HEADER_FIELDS = "sbt.title";
		const string RECORD_TABLE_NAME = "SimpleBBS_Records";
		const string RECORD_FIELDS = "sbt.name, sbt.body";
		static string INSERT_HEADER_SQL = string.Format ("INSERT INTO {0} (id, title) VALUES (?, ?)", HEADER_TABLE_NAME);
		static string UPDATE_HEADER_SQL = string.Format ("UPDATE {0} SET title=? WHERE id=?", HEADER_TABLE_NAME);
		static string INSERT_RECORD_SQL = string.Format ("INSERT INTO {0} (id, name, body) VALUES (?, ?, ?)", RECORD_TABLE_NAME);

		static SimpleBBSParser _instance = new SimpleBBSParser ();
		SimpleBBSParser () {}
		public static SimpleBBSParser Instance {
			get { return _instance; }
		}

		public void Init (IDbTransaction transaction)
		{
			DatabaseUtility.ExecuteNonQuery (transaction, "CREATE TABLE IF NOT EXISTS SimpleBBS_Headers (id INTEGER PRIMARY KEY REFERENCES MMLC_MergeableHeaders(id), title TEXT);");
			DatabaseUtility.ExecuteNonQuery (transaction, "CREATE TABLE IF NOT EXISTS SimpleBBS_Records (id INTEGER PRIMARY KEY REFERENCES MMLC_MergeableRecords(id), name TEXT, body TEXT);");
		}

		public IHashComputable ParseHeader (IDataRecord record, int offset)
		{
			return new SimpleBBSHeader (record.GetString (offset + 0));
		}

		public IHashComputable ParseRecord (IDataRecord record, int offset)
		{
			return new SimpleBBSRecord (
				record.GetString (offset + 0),
				record.GetString (offset + 1));
		}

		public void Insert (IDbTransaction transaction, long id, MergeableFileHeader header)
		{
			SimpleBBSHeader h = (SimpleBBSHeader)header.Content;
			DatabaseUtility.ExecuteNonQuery (transaction, INSERT_HEADER_SQL, id, h.Title);
		}

		public void Insert (IDbTransaction transaction, long id, MergeableFileRecord record)
		{
			SimpleBBSRecord r = (SimpleBBSRecord)record.Content;
			DatabaseUtility.ExecuteNonQuery (transaction, INSERT_RECORD_SQL, id, r.Name, r.Body);
		}

		public void Update (IDbTransaction transaction, long id, MergeableFileHeader header)
		{
			SimpleBBSHeader h = (SimpleBBSHeader)header.Content;
			DatabaseUtility.ExecuteNonQuery (transaction, UPDATE_HEADER_SQL, h.Title, id);
		}

		public int TypeId {
			get { return 0; }
		}

		public Type HeaderContentType {
			get { return typeof (SimpleBBSHeader); }
		}

		public Type RecordContentType {
			get { return typeof (SimpleBBSRecord); }
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
