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

using System.Data;

namespace p2pncs.Utility
{
	public static class DatabaseUtility
	{
		public static IDbCommand CreateCommand (IDbTransaction transaction, string sql, params object[] args)
		{
			IDbCommand cmd = transaction.Connection.CreateCommand ();
			cmd.CommandText = sql;
			for (int i = 0; i < args.Length; i++)
				AddParameter (cmd, args[i]);
			return cmd;
		}

		public static int ExecuteNonQuery (IDbTransaction transaction, string sql, params object[] args)
		{
			using (IDbCommand cmd = CreateCommand (transaction, sql, args)) {
				return cmd.ExecuteNonQuery ();
			}
		}

		public static object ExecuteScalar (IDbTransaction transaction, string sql, params object[] args)
		{
			using (IDbCommand cmd = CreateCommand (transaction, sql, args)) {
				return cmd.ExecuteScalar ();
			}
		}

		public static IDataReader ExecuteReader (IDbTransaction transaction, string sql, params object[] args)
		{
			using (IDbCommand cmd = CreateCommand (transaction, sql, args)) {
				return cmd.ExecuteReader ();
			}
		}

		public static void AddParameter (IDbCommand cmd, object value)
		{
			IDataParameter p = cmd.CreateParameter ();
			p.Value = value;
			cmd.Parameters.Add (p);
		}

		public static void AddParameter (IDbCommand cmd, string name, object value)
		{
			IDataParameter p = cmd.CreateParameter ();
			p.ParameterName = name;
			p.Value = value;
			cmd.Parameters.Add (p);
		}

		public static long GetLastInsertRowId (IDbTransaction transaction)
		{
			return (long)ExecuteScalar (transaction, "SELECT last_insert_rowid()");
		}
	}
}
