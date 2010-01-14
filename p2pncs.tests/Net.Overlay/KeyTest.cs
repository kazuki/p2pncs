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

using System;
using p2pncs.Net.Overlay;
using NUnit.Framework;
using openCrypto.EllipticCurve;
using p2pncs.Security.Cryptography;

namespace p2pncs.tests.Net.Overlay
{
	[TestFixture]
	public class KeyTest
	{
		[Test]
		public void EC_RoundtripTest ()
		{
			ECDomainNames[] domains = new ECDomainNames[] {
				ECDomainNames.secp192r1,
				ECDomainNames.secp256r1
			};
			foreach (ECDomainNames domain in domains) {
				ECKeyPair pair1 = ECKeyPair.Create (domain);
				Key key1 = Key.Create (pair1);
				ECKeyPair pair2 = key1.ToECPublicKey ();
				Assert.AreEqual (pair1.DomainName, pair2.DomainName);
				Assert.AreEqual (pair1.PublicKey, pair2.PublicKey);
				pair2 = ECKeyPairExtensions.CreatePrivate (pair1.PrivateKey);
				Assert.AreEqual (pair1.DomainName, pair2.DomainName);
				Assert.AreEqual (pair1.PrivateKey, pair2.PrivateKey);
				pair2 = ECKeyPairExtensions.CreatePublic (pair1.ExportPublicKey (true));
				Assert.AreEqual (pair1.DomainName, pair2.DomainName);
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

		[Test]
		public void ParseTest ()
		{
			for (int len = 1; len <= 32; len ++) {
				for (int i = 0; i < 3; i ++) {
					Key k1 = Key.CreateRandom (len);
					string str = k1.ToString ();
					Key k2 = Key.Parse (str);
					Assert.AreEqual (k1, k2);
				}
			}
		}
	}
}
