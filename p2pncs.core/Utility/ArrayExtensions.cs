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

using System;
using System.Collections.Generic;

namespace p2pncs.Utility
{
	static class ArrayExtensions
	{
		public static void Shuffle<T> (this T[] array)
		{
			Shuffle<T> (array, array.Length / 2);
		}

		public static void Shuffle<T> (this T[] array, int iterations)
		{
			Random rnd = new Random ();
			for (int i = 0; i < iterations; i ++) {
				int x = rnd.Next (array.Length);
				int y = rnd.Next (array.Length);
				T tmp = array[x];
				array[x] = array[y];
				array[y] = tmp;
			}
		}

		public static T[] RandomSelection<T> (this T[] array, int maxLength)
		{
			if (array.Length <= maxLength)
				return array;
			List<T> list = new List<T> (array);
			Random rnd = new Random ();
			while (list.Count > maxLength) {
				list.RemoveAt (rnd.Next (list.Count));
			}
			return list.ToArray ();
		}
	}
}