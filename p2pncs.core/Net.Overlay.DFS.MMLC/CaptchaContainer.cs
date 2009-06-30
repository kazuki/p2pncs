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

using System.Net;
using System.Security.Cryptography;
using openCrypto;
using openCrypto.EllipticCurve;
using openCrypto.EllipticCurve.Encryption;
using p2pncs.Security.Cryptography;

namespace p2pncs.Net.Overlay.DFS.MMLC
{
	[SerializableTypeId (0x412)]
	public class CaptchaContainer
	{
		[SerializableFieldId (0)]
		EndPoint _ep;

		[SerializableFieldId (1)]
		byte[] _encrypted;

		[SerializableFieldId (2)]
		int _id;

		public CaptchaContainer (EndPoint ep, int id, Key pubKey, object payload)
		{
			_ep = ep;
			_id = id;

			using (SymmetricAlgorithm algo = new CamelliaManaged ())
			using (ECIES ecies = new ECIES (DefaultAlgorithm.ECDomainName, algo)) {
				ecies.Parameters.PublicKey = pubKey.GetByteArray ();
				_encrypted = ecies.Encrypt (Serializer.Instance.Serialize (payload));
			}
		}

		public static object Decrypt (ECKeyPair privateKey, byte[] encrypted)
		{
			using (SymmetricAlgorithm algo = new CamelliaManaged ())
			using (ECIES ecies = new ECIES (DefaultAlgorithm.ECDomainName, algo)) {
				ecies.Parameters.PrivateKey = privateKey.PrivateKey;
				try {
					return Serializer.Instance.Deserialize (ecies.Decrypt (encrypted));
				} catch {
					return null;
				}
			}
		}

		public EndPoint EndPoint {
			get { return _ep; }
		}

		public byte[] Encrypted {
			get { return _encrypted; }
		}

		public int ID {
			get { return _id; }
		}
	}
}
