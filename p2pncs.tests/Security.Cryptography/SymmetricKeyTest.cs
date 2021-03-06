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
using p2pncs.Security.Cryptography;
using NUnit.Framework;
using openCrypto;

namespace p2pncs.tests.Security.Cryptography
{
	[TestFixture]
	public class SymmetricKeyTest
	{
		[Test]
		public void Test1 ()
		{
			SymmetricAlgorithmType[] types = new SymmetricAlgorithmType[] {
				SymmetricAlgorithmType.None,
				SymmetricAlgorithmType.Camellia,
				SymmetricAlgorithmType.Rijndael
			};
			byte[][][] key_iv_list = new byte[][][] {
				new byte[][] {
					null, null
				},
				new byte[][] {
					RNG.GetRNGBytes (16), RNG.GetRNGBytes (16),
					RNG.GetRNGBytes (24), RNG.GetRNGBytes (16),
					RNG.GetRNGBytes (32), RNG.GetRNGBytes (16)
				},
				new byte[][] {
					RNG.GetRNGBytes (16), RNG.GetRNGBytes (16),
					RNG.GetRNGBytes (24), RNG.GetRNGBytes (16),
					RNG.GetRNGBytes (32), RNG.GetRNGBytes (16)
				},
			};
			int[] data_length_list = new int[] {
				1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 64, 128, 129, 130
			};
			bool[] shuffle_list = new bool[] {true, false};
			for (int i = 0; i < types.Length; i ++) {
				byte[][] key_ivs = key_iv_list[i];
				for (int k = 0; k < key_ivs.Length - 1; k += 2) {
					int[] ivShuffleSize = new int[data_length_list.Length];
					foreach (bool enableShuffle in shuffle_list) {
						SymmetricKey key = new SymmetricKey (types[i], key_ivs[k + 1], key_ivs[k], CipherModePlus.CBC, System.Security.Cryptography.PaddingMode.ISO10126, enableShuffle);
						for (int idx = 0; idx < data_length_list.Length; idx ++) {
							byte[] data = RNG.GetRNGBytes (data_length_list[idx]);
							byte[] e1 = key.Encrypt (data, 0, data.Length);
							byte[] p1 = key.Decrypt (e1, 0, e1.Length);
							Assert.AreEqual (data, p1);
							if (key.IV != null) {
								if (enableShuffle)
									ivShuffleSize[idx] = e1.Length;
								else
									Assert.AreEqual (ivShuffleSize[idx] - key.IV.Length, e1.Length);
							}
						}
					}
				}
			}
		}

		[Test]
		public void Test2 ()
		{
			SymmetricKey key = new SymmetricKey (SymmetricAlgorithmType.Camellia, RNG.GetRNGBytes (16), RNG.GetRNGBytes (16), CipherModePlus.CBC, System.Security.Cryptography.PaddingMode.None, true);
			byte[] plain = RNG.GetRNGBytes (16);
			byte[] ciper = key.Encrypt (plain, 0, plain.Length);
			byte[] plain2 = key.Decrypt (ciper, 0, ciper.Length);
			Assert.AreEqual (plain, plain2);
		}
	}
}
