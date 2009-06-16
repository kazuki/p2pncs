using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
