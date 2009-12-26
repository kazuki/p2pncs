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
	public static class ArrayExtensions
	{
		public static void Shuffle<T> (this T[] array)
		{
			Shuffle<T> (array, array.Length / 2);
		}

		public static void Shuffle<T> (this T[] array, int iterations)
		{
			for (int i = 0; i < iterations; i ++) {
				int x = ThreadSafeRandom.Next (array.Length);
				int y = ThreadSafeRandom.Next (array.Length);
				T tmp = array[x];
				array[x] = array[y];
				array[y] = tmp;
			}
		}

		public static T[] RandomSelection<T> (this T[] array, int maxLength)
		{
			return array.RandomSelection<T> (maxLength, null);
		}

		public static T[] RandomSelection<T> (this T[] array, int maxLength, int excludeIndex)
		{
			return array.RandomSelection<T> (maxLength, new int[] {excludeIndex});
		}

		public static T[] RandomSelection<T> (this T[] array, int maxLength, int[] excludeIndexes)
		{
			HashSet<int> excludes = (excludeIndexes == null ? new HashSet<int> () : new HashSet<int> (excludeIndexes));
			if (array.Length <= maxLength)
				return array;
			List<T> list = new List<T> (array.Length);
			for (int i = 0; i < array.Length; i ++)
				if (!excludes.Contains (i))
					list.Add (array[i]);
			
			if (list.Count - maxLength < maxLength * 2) {
				while (list.Count > maxLength)
					list.RemoveAt (ThreadSafeRandom.Next (list.Count));
				return list.ToArray ();
			} else {
				List<T> list2 = new List<T> (maxLength);
				for (int i = 0; i < maxLength; i ++) {
					int idx = ThreadSafeRandom.Next (list.Count);
					list2.Add (list[idx]);
					list.RemoveAt (idx);
				}
				return list2.ToArray ();
			}
		}

		public static byte[] Join (this byte[] array1, byte[] array2)
		{
			byte[] joinArray = new byte[array1.Length + array2.Length];
			Buffer.BlockCopy (array1, 0, joinArray, 0, array1.Length);
			Buffer.BlockCopy (array2, 0, joinArray, array1.Length, array2.Length);
			return joinArray;
		}

		public static byte[] Join (this byte[] array1, params byte[][] arrays)
		{
			int size = array1.Length;
			for (int i = 0; i < arrays.Length; i ++)
				size += arrays[i].Length;

			byte[] joinArray = new byte[size];
			Buffer.BlockCopy (array1, 0, joinArray, 0, array1.Length);
			for (int i = 0, q = array1.Length; i < arrays.Length; i++) {
				Buffer.BlockCopy (arrays[i], 0, joinArray, q, arrays[i].Length);
				q += arrays[i].Length;
			}
			return joinArray;
		}

		public static T[] Join<T> (this T[] array1, T[] array2)
		{
			T[] joinArray = new T[array1.Length + array2.Length];
			int j = 0;
			for (int i = 0; i < array1.Length; i ++)
				joinArray[j++] = array1[i];
			for (int i = 0; i < array2.Length; i++)
				joinArray[j++] = array2[i];
			return joinArray;
		}

		public static T[] Join<T> (this T[] array1, params T[][] arrays)
		{
			int size = array1.Length;
			for (int i = 0; i < arrays.Length; i++)
				size += arrays[i].Length;

			T[] joinArray = new T[size];
			for (int i = 0; i < array1.Length; i ++)
				joinArray[i] = array1[i];
			for (int i = 0, q = array1.Length; i < arrays.Length; i++) {
				for (int k = 0; k < arrays[i].Length; k++)
					joinArray[q + k] = arrays[i][k];
				q += arrays[i].Length;
			}
			return joinArray;
		}

		public static byte[] CopyRange (this byte[] array, int offset, int size)
		{
			byte[] ret = new byte[size];
			Buffer.BlockCopy (array, offset, ret, 0, size);
			return ret;
		}

		public static T[] CopyRange<T> (this T[] array, int offset, int size)
		{
			T[] ret = new T[size];
			for (int i = 0; i < size; i ++)
				ret[i] = array[offset + i];
			return ret;
		}

		public static bool EqualsRange (this byte[] x, int x_idx, byte[] y, int y_idx, int size)
		{
			if (x_idx + size > x.Length || y_idx + size > y.Length)
				throw new ArgumentOutOfRangeException ();
			for (int i = 0; i < size; i++)
				if (x[x_idx + i] != y[y_idx + i])
					return false;
			return true;
		}
	}
}
