﻿/*
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

namespace p2pncs.Net.Overlay.DHT
{
	public class GetResult<T>
	{
		Key _key;
		T[] _values;
		int _hops;

		public GetResult (Key key, T[] values, int hops)
		{
			_key = key;
			_values = values;
			_hops = hops;
		}

		public Key Key {
			get { return _key; }
		}

		public T[] Values {
			get { return _values; }
		}

		public int Hops {
			get { return _hops; }
		}
	}
}
