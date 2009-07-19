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
using System.Text;
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
		string _title;

		[SerializableFieldId (2)]
		MergeableFileHeaderFlags _flags;

		[SerializableFieldId (3)]
		DateTime _created;

		[SerializableFieldId (4)]
		DateTime _lastManaged;

		[SerializableFieldId (5)]
		IHashComputable _content;

		[SerializableFieldId (6)]
		AuthServerInfo[] _authServers;

		[SerializableFieldId (7)]
		byte[] _sign;

		[SerializableFieldId (8)]
		Key _recordsetHash;

		int _numOfRecords;
		DateTime _lastModified;

		public MergeableFileHeader (Key key, string title, MergeableFileHeaderFlags flags, IHashComputable content, AuthServerInfo[] authServers)
			: this (key, title, flags, DateTime.UtcNow, DateTime.UtcNow, content, authServers, null, null)
		{
		}

		public MergeableFileHeader (string title, MergeableFileHeaderFlags flags, IHashComputable content, AuthServerInfo[] authServers)
			: this (null, title, flags, DateTime.UtcNow, DateTime.UtcNow, content, authServers, null, null)
		{
		}

		public MergeableFileHeader (Key key, string title, MergeableFileHeaderFlags flags, DateTime created, DateTime lastManaged, IHashComputable content, AuthServerInfo[] authServers, byte[] sign, Key recordsetHash)
		{
			if (created.Kind != DateTimeKind.Utc || lastManaged.Kind != DateTimeKind.Utc)
				throw new ArgumentException ();
			_key = key;
			_title = title;
			_flags = flags;
			_created = created;
			_lastManaged = lastManaged;
			_content = content;
			_authServers = authServers;
			_sign = sign;
			_recordsetHash = (recordsetHash != null ? recordsetHash : new Key (new byte[DefaultAlgorithm.HashByteSize]));
			if (_recordsetHash.KeyBytes != DefaultAlgorithm.HashByteSize)
				throw new FormatException ();
		}

		public MergeableFileHeader CopyBasisInfo ()
		{
			return new MergeableFileHeader (_key, _title, _flags, _created, _lastManaged, _content, _authServers, _sign, null);
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
				tmp = Encoding.UTF8.GetBytes (_title);
				hash.TransformBlock (tmp, 0, tmp.Length, null, 0);
				tmp = BitConverter.GetBytes ((long)_flags);
				hash.TransformBlock (tmp, 0, tmp.Length, null, 0);
				tmp = BitConverter.GetBytes (_created.ToUniversalTime ().Ticks);
				hash.TransformBlock (tmp, 0, tmp.Length, null, 0);
				tmp = BitConverter.GetBytes (_lastManaged.ToUniversalTime ().Ticks);
				hash.TransformBlock (tmp, 0, tmp.Length, null, 0);
				if (_content != null)
					_content.ComputeHash (hash);
				if (_authServers != null) {
					tmp = Encoding.ASCII.GetBytes (AuthServerInfo.ToParsableString (_authServers));
					hash.TransformBlock (tmp, 0, tmp.Length, null, 0);
				}
				hash.TransformFinalBlock (new byte[0], 0, 0);
				return hash.Hash;
			}
		}

		public Key Key {
			get { return _key; }
			internal set { _key = value; }
		}

		public string Title {
			get { return _title; }
		}

		public MergeableFileHeaderFlags Flags {
			get { return _flags; }
		}

		public DateTime CreatedTime {
			get { return _created; }
			internal set { _created = value; }
		}

		public DateTime LastManagedTime {
			get { return _lastManaged; }
			internal set { _lastManaged = value; }
		}

		public byte[] Signature {
			get { return _sign; }
		}

		public IHashComputable Content {
			get { return _content; }
			internal set { _content = value; }
		}

		public AuthServerInfo[] AuthServers {
			get { return _authServers; }
		}

		public Key RecordsetHash {
			get { return _recordsetHash; }
			set { _recordsetHash = value;}
		}

		public int NumberOfRecords {
			get { return _numOfRecords; }
			internal set { _numOfRecords = value; }
		}

		public DateTime LastModifiedTime {
			get { return _lastModified; }
			internal set { _lastModified = value; }
		}
	}
}
