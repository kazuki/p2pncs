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

namespace p2pncs.Utility
{
	public class DuplicationChecker<T>
	{
		HashSet<T> _set;
		Queue<T> _queue;
		int _size;

		public DuplicationChecker (int historySize)
		{
			_size = historySize;
			_set = new HashSet<T> ();
			_queue = new Queue<T> (historySize);
		}

		/// <summary>
		/// 引数で指定するキーが過去にCheckの引数として指定されていないかどうか確認し、
		/// 呼び出された履歴が無い場合はtrueを、過去に呼び出されていた場合にはfalseを返します。
		/// </summary>
		public bool Check (T key)
		{
			lock (_set) {
				if (!_set.Add (key))
					return false;
				if (_queue.Count == _size)
					_set.Remove (_queue.Dequeue ());
				_queue.Enqueue (key);
			}
			return true;
		}
	}
}
