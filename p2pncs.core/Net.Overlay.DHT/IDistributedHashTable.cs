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

namespace p2pncs.Net.Overlay.DHT
{
	public interface IDistributedHashTable : IDisposable
	{
		void RegisterTypeID (Type type, int id, ILocalHashTableValueMerger merger);

		IAsyncResult BeginGet (Key key, Type type, AsyncCallback callback, object state);
		GetResult EndGet (IAsyncResult ar);

		IAsyncResult BeginPut (Key key, TimeSpan lifeTime, object value, AsyncCallback callback, object state);
		void EndPut (IAsyncResult ar);

		void LocalPut (Key key, TimeSpan lifeTime, object value);

		IKeyBasedRouter KeyBasedRouter { get; }
	}
}
