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
using NUnit.Framework;
using p2pncs.Utility;

namespace p2pncs.tests.Utility
{
	[TestFixture]
	public class UriSafeBase64Test
	{
		static string ToUriSafeBase64 (byte[] array)
		{
			return Convert.ToBase64String (array).Replace ('+', '-').Replace ('/', '_').Replace ('=', '.');
		}

		[Test]
		public void LengthTest ()
		{
			byte[] ary = new byte[0];
			Assert.AreEqual (ToUriSafeBase64 (ary), UriSafeBase64.Encode (ary));
			ary = new byte[] {0xff}; Assert.AreEqual (ToUriSafeBase64 (ary), UriSafeBase64.Encode (ary));
			ary = new byte[] {0xff, 0xff}; Assert.AreEqual (ToUriSafeBase64 (ary), UriSafeBase64.Encode (ary));
			ary = new byte[] {0xff, 0xff, 0xff}; Assert.AreEqual (ToUriSafeBase64 (ary), UriSafeBase64.Encode (ary));
			ary = new byte[] {0xff, 0xff, 0xff, 0xff}; Assert.AreEqual (ToUriSafeBase64 (ary), UriSafeBase64.Encode (ary));
		}

		[Test]
		public void RandomRoundtripTest ()
		{
			byte[] buffer = new byte[64];
			Random rnd = new Random ();
			for (int i = 0; i < 10000; i ++) {
				rnd.NextBytes (buffer);
				string str1 = UriSafeBase64.Encode (buffer);
				string str2 = ToUriSafeBase64 (buffer);
				Assert.AreEqual (str2, str1);
				byte[] dec = UriSafeBase64.Decode (str1);
				Assert.AreEqual (buffer, dec);

				string url_encoded = System.Web.HttpUtility.UrlEncode (str1);
				Assert.AreEqual (str1, url_encoded);
			}
		}
	}
}
