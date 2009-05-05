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
	public interface ILocalHashTable : IDisposable
	{
		void Put (Key key, int typeId, DateTime expires, object value);

		object[] Get (Key key, int typeId);

		/// <param name="value">削除する値. nullの場合はkeyとtypeIdが一致するすべての値を削除</param>
		void Remove (Key key, int typeId, object value);

		void Clear ();
	}
}
