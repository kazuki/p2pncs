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

/// TODO: エントリに有効期限を設ける

using System;
using System.Collections.Generic;
using p2pncs.Threading;

namespace p2pncs.Net.Overlay.DHT
{
	public class OnMemoryLocalHashTable : ILocalHashTable
	{
		Dictionary<Key, object> _dic = new Dictionary<Key, object> ();

		public OnMemoryLocalHashTable ()
		{
		}

		#region ILocalHashTable Members

		public void Put (Key key, DateTime expires, object value)
		{
			lock (_dic) {
				_dic[key] = value;
			}
		}

		public object Get (Key key)
		{
			object value;
			lock (_dic) {
				if (_dic.TryGetValue (key, out value))
					return value;
			}
			return null;
		}

		public void Remove (Key key)
		{
			lock (_dic) {
				_dic.Remove (key);
			}
		}

		public void Clear ()
		{
			lock (_dic) {
				_dic.Clear ();
			}
		}

		#endregion

		#region IDisposable Members

		public void Dispose ()
		{
			Clear ();
		}

		#endregion
	}
}
