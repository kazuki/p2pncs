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
using System.Threading;
using p2pncs.Threading;

namespace p2pncs.Utility
{
	public class KeyValueCache<TKey,TValue>
	{
		Dictionary<TKey,Slot> _dic, _waiting;
		Queue<TKey> _queue;
		int _size;

		public KeyValueCache (int historySize)
		{
			_size = historySize;
			_dic = new Dictionary<TKey,Slot> ();
			_waiting = new Dictionary<TKey,Slot> ();
			_queue = new Queue<TKey> (historySize);
		}

		/// <summary>
		/// keyに対応する値が既に設定されているかどうかチェックし、
		/// 設定されていない場合は、SetValueメソッド用に領域を確保します。
		/// </summary>
		/// <returns>既に設定されている場合はfalse。設定されていない場合はtrue。</returns>
		public bool CheckAndReserve (TKey key, out TValue value)
		{
			Slot slot;
			lock (_dic) {
				if (_dic.TryGetValue (key, out slot)) {
					if (slot.Empty)
						goto BusyWaiting;
					value = slot.Value;
					return false;
				}
				if (_queue.Count == _size)
					_dic.Remove (_queue.Dequeue ());
				value = default(TValue);
				_queue.Enqueue (key);
				_dic.Add (key, new Slot (value));
			}
			return true;

BusyWaiting:
			lock (_waiting) {
				_waiting[key] = slot;
			}
			do {
				Thread.Sleep (1);
				Thread.MemoryBarrier ();
			} while (slot.Empty);
			value = slot.Value;
			lock (_waiting) {
				_waiting.Remove (key);
			}
			return false;
		}

		/// <summary>
		/// CheckAndReserveで確保した領域に値を設定します
		/// </summary>
		public void SetValue (TKey key, TValue value)
		{
			lock (_dic) {
				Slot slot;
				if (_dic.TryGetValue (key, out slot)) {
					slot.Set (value);
					return;
				}
			}
			lock (_waiting) {
				Slot slot;
				if (_waiting.TryGetValue (key, out slot)) {
					slot.Set (value);
					return;
				}
			}
		}

		class Slot
		{
			public bool Empty = true;
			public TValue Value;

			public Slot (TValue value)
			{
				Value = value;
			}

			public void Set (TValue value)
			{
				Empty = false;
				Value = value;
			}
		}
	}
}
