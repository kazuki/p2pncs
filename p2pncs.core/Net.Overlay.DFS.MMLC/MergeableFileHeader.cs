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
using openCrypto.EllipticCurve.Signature;
using p2pncs.Security.Cryptography;

namespace p2pncs.Net.Overlay.DFS.MMLC
{
	[SerializableTypeId (0x400)]
	public class MergeableFileHeader
	{
		[SerializableFieldId (0)]
		Key _key;

		[SerializableFieldId (1)]
		DateTime _lastManaged;

		[SerializableFieldId (2)]
		IHashComputable _content;

		[SerializableFieldId (3)]
		byte[] _sign;

		[SerializableFieldId (4)]
		Key _recordsetHash;

		public MergeableFileHeader (ECKeyPair privateKey, DateTime lastManaged, IHashComputable content)
		{
			_lastManaged = lastManaged;
			_content = content;
			_recordsetHash = new Key (new byte[DefaultAlgorithm.HashByteSize]);
			Sign (privateKey);
		}

		public MergeableFileHeader (Key key, DateTime lastManaged, IHashComputable content, byte[] sign, Key recordsetHash)
		{
			_key = key;
			_lastManaged = lastManaged;
			_content = content;
			_sign = sign;
			_recordsetHash = recordsetHash;
			if (_recordsetHash.KeyBytes != DefaultAlgorithm.HashByteSize)
				throw new FormatException ();
		}

		public MergeableFileHeader CopyBasisInfo ()
		{
			return new MergeableFileHeader (_key, _lastManaged, _content, _sign, new Key (new byte[DefaultAlgorithm.HashByteSize]));
		}

		public void Sign (ECKeyPair privateKey)
		{
			_key = Key.Create (privateKey);
			using (ECDSA ecdsa = new ECDSA (privateKey)) {
				_sign = ecdsa.SignHash (ComputeHash ());
			}
		}

		public bool Verify ()
		{
			using (ECDSA ecdsa = new ECDSA (_key.ToECPublicKey ())) {
				return ecdsa.VerifyHash (ComputeHash (), _sign);
			}
		}

		byte[] ComputeHash ()
		{
			using (HashAlgorithm hash = DefaultAlgorithm.CreateHashAlgorithm ()) {
				byte[] tmp = _key.GetByteArray ();
				hash.TransformBlock (tmp, 0, tmp.Length, null, 0);
				tmp = BitConverter.GetBytes (_lastManaged.ToUniversalTime ().Ticks);
				hash.TransformBlock (tmp, 0, tmp.Length, null, 0);
				if (_content != null)
					_content.ComputeHash (hash);
				hash.TransformFinalBlock (new byte[0], 0, 0);
				return hash.Hash;
			}
		}

		public Key Key {
			get { return _key; }
		}

		public DateTime LastManagedTime {
			get { return _lastManaged; }
		}

		public byte[] Signature {
			get { return _sign; }
		}

		public IHashComputable Content {
			get { return _content; }
			internal set { _content = value; }
		}

		public Key RecordsetHash {
			get { return _recordsetHash; }
			set { _recordsetHash = value;}
		}
	}
}
