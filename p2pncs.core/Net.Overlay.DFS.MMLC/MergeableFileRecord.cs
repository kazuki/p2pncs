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
		DateTime _lastManaged;

		[SerializableFieldId (2)]
		Key _hash;

		public MergeableFileRecord (IHashComputable content)
		{
			_content = content;
		}

		public MergeableFileRecord (IHashComputable content, DateTime lastManaged, Key hash)
		{
			_content = content;
			_lastManaged = lastManaged;
			_hash = hash;
		}

		public void UpdateHash ()
		{
			using (HashAlgorithm algo = DefaultAlgorithm.CreateHashAlgorithm ()) {
				_content.ComputeHash (algo);
				byte[] tmp = BitConverter.GetBytes (_lastManaged.ToUniversalTime ().Ticks);
				algo.TransformFinalBlock (tmp, 0, tmp.Length);
				_hash = new Key (algo.Hash);
			}
		}

		public IHashComputable Content {
			get { return _content; }
			internal set { _content = value; }
		}

		public DateTime LastManagedTime {
			get { return _lastManaged; }
			set { _lastManaged = value;}
		}

		public Key Hash {
			get { return _hash; }
		}
	}
}
