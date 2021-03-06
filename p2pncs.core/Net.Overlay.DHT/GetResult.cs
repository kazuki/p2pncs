﻿/*
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

namespace p2pncs.Net.Overlay.DHT
{
	public class GetResult
	{
		Key _key;
		object[] _values;
		int _hops;

		internal GetResult (Key key, object[] values, int hops)
		{
			_key = key;
			_values = values;
			_hops = hops;
		}

		public Key Key {
			get { return _key; }
		}

		/// <remarks>要素がIPutterEndPointStoreを実装していて、かつEndPointがnullだった場合はその要素のEndPointは自ノードを指している</remarks>
		public object[] Values {
			get { return _values; }
		}

		public int Hops {
			get { return _hops; }
		}
	}
}
