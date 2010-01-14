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

namespace p2pncs.Net.Overlay.DHT
{
	/// <summary>IEquatableインターフェイスを利用して複数の値のマージを行うクラス</summary>
	/// <remarks>既に値を保持している場合は，有効期限を延ばす</remarks>
	/// <typeparam name="T"></typeparam>
	public class EqualityValueMerger<T> : IValueMerger where T : class, IEquatable<T>
	{
		public TimeSpan MinRePutInterval = TimeSpan.FromSeconds (30);

		#region IValueMerger Members
		public object Merge (object value, object new_value, TimeSpan lifeTime)
		{
			List<HolderInfo> list = value as List<HolderInfo>;
			T entry = (T)new_value;
			if (list == null)
				list = new List<HolderInfo> ();
			bool found = false;
			for (int i = 0; i < list.Count; i++) {
				if (list[i].ExpirationDate <= DateTime.Now) {
					list.RemoveAt (i--);
					continue;
				}
				if (entry.Equals (list[i].Entry)) {
					list[i].Extend (lifeTime, MinRePutInterval);
					found = true;
					break;
				}
			}
			if (!found) {
				list.Add (new HolderInfo (entry, lifeTime));
				list.Sort (delegate (HolderInfo x, HolderInfo y) {
					return y.ExpirationDate.CompareTo (x.ExpirationDate);
				});
			}
			return list;
		}

		public object[] GetEntries (object value, int max_num)
		{
			List<HolderInfo> list = value as List<HolderInfo>;
			if (list == null || list.Count == 0)
				return new object[0];
			T[] result = new T[Math.Min (list.Count, max_num)];
			for (int i = 0; i < result.Length; i++)
				result[i] = list[i].Entry;
			return (object[])result;
		}

		public void CheckExpiration (object value)
		{
			List<HolderInfo> list = value as List<HolderInfo>;
			for (int i = 0; i < list.Count; i++) {
				if (list[i].IsExpired ())
					list.RemoveAt (i--);
			}
		}

		public int GetCount (object value)
		{
			return (value as List<HolderInfo>).Count;
		}
#endregion

#if false
		public DHTEntry[] GetSendEntries (Key key, int typeId, object value, int max_num)
		{
			List<HolderInfo> list = value as List<HolderInfo>;
			if (list == null || list.Count == 0)
				return new DHTEntry[0];
			DHTEntry[] result = new DHTEntry[Math.Min (list.Count, max_num)];
			int count = 0;
			for (int i = 0; i < list.Count && count < result.Length; i ++) {
				if (list[i].SendFlag) continue;
				result[count++] = new DHTEntry (key, typeId, list[i].GetValueToSend (), list[i].LifeTime);
			}
			if (result.Length != count)
				Array.Resize<DHTEntry> (ref result, count);
			return result;
		}

		public void UnmarkSendFlag (object value, object mark_value)
		{
			List<HolderInfo> list = value as List<HolderInfo>;
			if (list == null || list.Count == 0)
				return;
			for (int i = 0; i < list.Count; i ++)
				if (mark_value.Equals (list[i])) {
					list[i].UnmarkSend ();
					return;
				}
		}
#endif

		public class HolderInfo : IEquatable<HolderInfo>
		{
			protected T _entry;
			protected TimeSpan _lifeTime;
			protected DateTime _expiration;
			protected bool _sendFlag;

			public HolderInfo (T entry, TimeSpan lifetime)
			{
				_entry = entry;
				_lifeTime = lifetime;
				_expiration = DateTime.Now + lifetime;
				_sendFlag = false;
			}

			public void Extend (TimeSpan lifetime, TimeSpan minRePutInterval)
			{
				if ((_expiration - _lifeTime) + minRePutInterval <= DateTime.Now)
					_sendFlag = false;
				_lifeTime = lifetime;
				_expiration = DateTime.Now + lifetime;
			}

			public bool IsExpired ()
			{
				if (_expiration <= DateTime.Now)
					return true;
				return false;
			}

			public T GetValueToSend ()
			{
				_sendFlag = true;
				return _entry;
			}

			public void UnmarkSend ()
			{
				_sendFlag = false;
			}

			public T Entry {
				get { return _entry; }
			}

			public TimeSpan LifeTime {
				get { return _lifeTime; }
			}

			public bool SendFlag {
				get { return _sendFlag; }
			}

			public DateTime ExpirationDate {
				get { return _expiration; }
			}

			public override int GetHashCode ()
			{
				return _entry.GetHashCode ();
			}

			public bool Equals (HolderInfo other)
			{
				return _entry.Equals (other._entry);
			}
		}
	}
}
