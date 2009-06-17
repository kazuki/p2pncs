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

namespace p2pncs.Net.Overlay.DHT
{
	public class LocalHashTableValueMerger<T> : ILocalHashTableValueMerger, IMassKeyDelivererValueGetter where T : IEquatable<T>
	{
		public object Merge (object value, object new_value, DateTime expirationDate)
		{
			List<Pack> list = value as List<Pack>;
			IEquatable<T> entry = new_value as IEquatable<T>;
			if (list == null)
				list = new List<Pack> ();
			bool found = false;
			for (int i = 0; i < list.Count; i++) {
				if (list[i].ExpirationDate <= DateTime.Now) {
					list.RemoveAt (i--);
					continue;
				}
				if (entry.Equals (list[i].Entry)) {
					list[i].Extend (expirationDate);
					found = true;
					break;
				}
			}
			if (!found) {
				list.Add (new Pack (entry, expirationDate));
				list.Sort (delegate (Pack x, Pack y) {
					return y.ExpirationDate.CompareTo (x.ExpirationDate);
				});
			}
			return list;
		}

		public object[] GetEntries (object value, int max_num)
		{
			List<Pack> list = value as List<Pack>;
			if (list == null || list.Count == 0)
				return new object[0];
			object[] result = new object[Math.Min (list.Count, max_num)];
			for (int i = 0; i < result.Length; i++)
				result[i] = list[i].Entry;
			return result;
		}

		public void ExpirationCheck (object value)
		{
			List<Pack> list = value as List<Pack>;
			for (int i = 0; i < list.Count; i++) {
				if (list[i].ExpirationDate <= DateTime.Now)
					list.RemoveAt (i--);
			}
		}

		public DHTEntry[] GetSendEntries (Key key, int typeId, object value, int max_num)
		{
			List<Pack> list = value as List<Pack>;
			if (list == null || list.Count == 0)
				return new DHTEntry[0];
			DHTEntry[] result = new DHTEntry[Math.Min (list.Count, max_num)];
			int count = 0;
			for (int i = 0; i < list.Count && count < result.Length; i ++) {
				if (list[i].SendFlag) continue;
				result[count++] = new DHTEntry (key, typeId, list[i].GetValueToSend (), list[i].ExpirationDate);
			}
			if (result.Length != count)
				Array.Resize<DHTEntry> (ref result, count);
			return result;
		}

		public void UnmarkSendFlag (object value, object mark_value)
		{
			List<Pack> list = value as List<Pack>;
			if (list == null || list.Count == 0)
				return;
			for (int i = 0; i < list.Count; i ++)
				if (mark_value.Equals (list[i])) {
					list[i].UnmarkSend ();
					return;
				}
		}

		class Pack : IEquatable<Pack>
		{
			public IEquatable<T> Entry;
			public DateTime ExpirationDate;
			public bool SendFlag;

			public Pack (IEquatable<T> entry, DateTime expiration)
			{
				Entry = entry;
				ExpirationDate = expiration;
				SendFlag = false;
			}

			public void Extend (DateTime new_expiration)
			{
				if (ExpirationDate < new_expiration) {
					ExpirationDate = new_expiration;
					SendFlag = false;
				}
			}

			public IEquatable<T> GetValueToSend ()
			{
				SendFlag = true;
				return Entry;
			}

			public void UnmarkSend ()
			{
				SendFlag = false;
			}

			public override int GetHashCode ()
			{
				return Entry.GetHashCode ();
			}

			public bool Equals (Pack other)
			{
				return Entry.Equals (other.Entry);
			}
		}
	}
}
