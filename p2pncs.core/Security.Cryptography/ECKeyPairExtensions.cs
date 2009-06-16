using System;
using openCrypto.EllipticCurve;

namespace p2pncs.Security.Cryptography
{
	public static class ECKeyPairExtensions
	{
		public static ECKeyPair CreatePublic (byte[] publicKey)
		{
			return ECKeyPair.CreatePublic (DefaultAlgorithm.GetDefaultDomainName (publicKey.Length), publicKey);
		}

		public static ECKeyPair CreatePrivate (byte[] privateKey)
		{
			return ECKeyPair.CreatePrivate (DefaultAlgorithm.GetDefaultDomainName (privateKey.Length + 1), privateKey);
		}
	}
}
