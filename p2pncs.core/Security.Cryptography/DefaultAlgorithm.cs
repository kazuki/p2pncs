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
using System.Security.Cryptography;
using openCrypto.EllipticCurve;

namespace p2pncs.Security.Cryptography
{
	public static class DefaultAlgorithm
	{
		public static ECDomainNames ECDomainName {
			get { return ECDomainNames.secp256r1; }
		}

		public static HashAlgorithm CreateHashAlgorithm ()
		{
			return new SHA256Managed ();
		}

		public static int HashByteSize {
			get { return 32; }
		}

		public static ECDomainNames GetDefaultDomainName (int compressedPublicKeyBytes)
		{
			switch (compressedPublicKeyBytes - 1) {
				case 24: // 192bit
					return ECDomainNames.secp192r1;
				case 32: // 256bit
					return ECDomainNames.secp256r1;
				default:
					throw new NotSupportedException ();
			}
		}
	}
}
