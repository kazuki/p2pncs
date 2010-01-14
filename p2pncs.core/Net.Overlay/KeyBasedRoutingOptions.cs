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

using System.Net;

namespace p2pncs.Net.Overlay
{
	public class KeyBasedRoutingOptions
	{
		public KeyBasedRoutingOptions ()
		{
			FirstHops = null;
			NumberOfSimultaneous = -1;
			RoutingFinishedMatchBits = -1;
		}

		/// <summary>最初にメッセージを送信するノード. Nullまたは要素数が0の場合はルーティングテーブルより選出する</summary>
		public EndPoint[] FirstHops { get; set; }

		/// <summary>同時問い合わせ数. 負値の場合や大きすぎる場合は既定値を利用する</summary>
		public int NumberOfSimultaneous { get; set; }

		/// <summary>目的に到達しなくてもルーティングを終了するビット一致長. 負値や大きすぎる場合は目的に到達するまでルーティングを継続する</summary>
		public int RoutingFinishedMatchBits { get; set; }
	}
}
