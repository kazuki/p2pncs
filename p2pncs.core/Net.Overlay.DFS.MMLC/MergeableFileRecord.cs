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
using p2pncs.Security.Cryptography;

namespace p2pncs.Net.Overlay.DFS.MMLC
{
	[SerializableTypeId (0x401)]
	public class MergeableFileRecord
	{
		[SerializableFieldId (0)]
		IHashComputable _content;

		[SerializableFieldId (1)]
		DateTime _created;

		[SerializableFieldId (2)]
		DateTime _lastManaged;

		[SerializableFieldId (3)]
		Key _publicKey;

		[SerializableFieldId (4)]
		byte[] _sign;

		[SerializableFieldId (5)]
		Key _hash;

		[SerializableFieldId (6)]
		byte _auth_idx;

		[SerializableFieldId (7)]
		byte[] _auth;

		public MergeableFileRecord (IHashComputable content, DateTime created, DateTime lastManaged, Key hash, Key publicKey, byte[] sign, byte auth_idx, byte[] auth)
		{
			if (lastManaged.Kind != DateTimeKind.Utc || created.Kind != DateTimeKind.Utc)
				throw new ArgumentException ();
			_content = content;
			_created = created;
			_lastManaged = lastManaged;
			_hash = hash;
			_publicKey = publicKey;
			_sign = sign;
			_auth_idx = auth_idx;
			_auth = auth;
			if (hash == null)
				UpdateHash ();
		}

		public void UpdateHash ()
		{
			using (HashAlgorithm algo = DefaultAlgorithm.CreateHashAlgorithm ()) {
				byte[] tmp;
				_content.ComputeHash (algo);
				tmp = BitConverter.GetBytes (_created.Ticks);
				algo.TransformBlock (tmp, 0, tmp.Length, null, 0);
				tmp = BitConverter.GetBytes (_lastManaged.Ticks);
				algo.TransformBlock (tmp, 0, tmp.Length, null, 0);
				if (_publicKey != null)
					algo.TransformBlock (_publicKey.GetByteArray (), 0, _publicKey.KeyBytes, null, 0);
				algo.TransformFinalBlock (tmp, 0, 0);
				_hash = new Key (algo.Hash);
			}
		}

		public IHashComputable Content {
			get { return _content; }
			internal set { _content = value; }
		}

		public DateTime CreatedTime {
			get { return _created; }
			set {
				if (value.Kind != DateTimeKind.Utc)
					throw new ArgumentException ();
				_created = value;
			}
		}

		public DateTime LastManagedTime {
			get { return _lastManaged; }
			set {
				if (value.Kind != DateTimeKind.Utc)
					throw new ArgumentException (); 
				_lastManaged = value;
			}
		}

		public Key Hash {
			get { return _hash; }
		}

		public Key PublicKey {
			get { return _publicKey; }
		}

		public byte[] Signature {
			get { return _sign; }
		}

		public byte AuthorityIndex {
			get { return _auth_idx; }
			set { _auth_idx = value;}
		}

		public byte[] Authentication {
			get { return _auth; }
			set { _auth = value;}
		}
	}
}
