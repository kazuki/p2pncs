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
using RNG = NPack.MersenneTwister;

namespace p2pncs.Utility
{
	public static class ThreadSafeRandom
	{
		static RNG _rng = new RNG ();

		public static uint NextUInt32 ()
		{
			lock (_rng) {
				return _rng.NextUInt32 ();
			}
		}

		public static int Next ()
		{
			lock (_rng) {
				return _rng.Next ();
			}
		}

		public static int Next (int maxValue)
		{
			lock (_rng) {
				return _rng.Next (maxValue);
			}
		}

		public static int Next (int minValue, int maxValue)
		{
			lock (_rng) {
				return _rng.Next (minValue, maxValue);
			}
		}

		public static double NextDouble ()
		{
			lock (_rng) {
				return _rng.NextDouble ();
			}
		}

		public static void NextBytes (byte[] buffer)
		{
			lock (_rng) {
				_rng.NextBytes (buffer);
			}
		}
	}
}
