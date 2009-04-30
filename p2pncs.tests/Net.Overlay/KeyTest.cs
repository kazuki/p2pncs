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
using p2pncs.Net.Overlay;
using NUnit.Framework;
using openCrypto.EllipticCurve;

namespace p2pncs.tests.Net.Overlay
{
	[TestFixture]
	public class KeyTest
	{
		[Test]
		public void EC_RoundtripTest ()
		{
			Array domains = Enum.GetValues (typeof (ECDomainNames));
			foreach (object domain in domains) {
				ECDomainNames d = (ECDomainNames)domain;
				if (d == ECDomainNames.secp224r1) continue; // not support point-compression

				ECKeyPair pair1 = ECKeyPair.Create (d);
				Key key1 = Key.Create (pair1);
				ECKeyPair pair2 = key1.ToECPublicKey (d);
				Assert.AreEqual (pair1.PublicKey, pair2.PublicKey);
			}
		}

		[Test]
		public void TEST ()
		{
			Key key1 = new Key (new byte[] {0x23, 0x01});
			Key key2 = new Key (new byte[] {0x34, 0x02});
			Key key3 = new Key (new byte[] {0x34, 0x01});
			Key key4 = new Key (new byte[] {0x34, 0x12});
			Key key_eq = new Key (new byte[] {0x23, 0x01});

			Assert.AreEqual (6, Key.MatchBitsFromMSB (key1, key2));
			Assert.AreEqual (11, Key.MatchBitsFromMSB (key1, key3));
			Assert.AreEqual (3, Key.MatchBitsFromMSB (key1, key4));

			Assert.AreEqual (1, Key.MatchDigitsFromMSB (key1, key2, 16));
			Assert.AreEqual (2, Key.MatchDigitsFromMSB (key1, key3, 16));
			Assert.AreEqual (0, Key.MatchDigitsFromMSB (key1, key4, 16));

			Assert.AreEqual (16, Key.MatchBitsFromMSB (key1, key_eq));
			Assert.AreEqual (4, Key.MatchDigitsFromMSB (key1, key_eq, 16));
		}
	}
}
