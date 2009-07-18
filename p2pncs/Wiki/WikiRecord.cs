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

using System.Security.Cryptography;
using System.Text;
using p2pncs.Net.Overlay;
using p2pncs.Security.Cryptography;

namespace p2pncs.Wiki
{
	[SerializableTypeId (0x1009)]
	class WikiRecord : IHashComputable
	{
		[SerializableFieldId (0)]
		string _pageName;

		[SerializableFieldId (1)]
		Key _parentHash;

		[SerializableFieldId (2)]
		string _name;

		[SerializableFieldId (3)]
		WikiMarkupType _markupType;

		[SerializableFieldId (4)]
		byte[] _raw_body;

		[SerializableFieldId (5)]
		WikiCompressType _compressType;

		[SerializableFieldId (6)]
		WikiDiffType _diffType;

		string _body;

		public WikiRecord (string pageName, Key parentHash, string name, WikiMarkupType markupType, byte[] raw_body, WikiCompressType compressType, WikiDiffType diffType)
		{
			_pageName = pageName;
			_parentHash = parentHash;
			_name = name;
			_markupType = markupType;
			_raw_body = raw_body;
			_compressType = compressType;
			_diffType = diffType;
			_body = Encoding.UTF8.GetString (raw_body);
		}

		public string PageName {
			get { return _pageName; }
		}

		public Key ParentHash {
			get { return _parentHash; }
		}

		public string Name {
			get { return _name; }
		}

		public WikiMarkupType MarkupType {
			get { return _markupType; }
		}

		public byte[] RawBody {
			get { return _raw_body; }
		}

		public string Body {
			get { return _body; }
		}

		public WikiCompressType CompressType {
			get { return _compressType; }
		}

		public WikiDiffType DiffType {
			get { return _diffType; }
		}

		#region IHashComputable Members

		public void ComputeHash (HashAlgorithm hash)
		{
			if (_pageName.Length > 0) {
				byte[] tmp = Encoding.UTF8.GetBytes (_pageName);
				hash.TransformBlock (tmp, 0, tmp.Length, null, 0);
			}
			if (_parentHash != null)
				hash.TransformBlock (_parentHash.GetByteArray (), 0, _parentHash.KeyBytes, null, 0);
			if (_name.Length > 0) {
				byte[] tmp = Encoding.UTF8.GetBytes (_name);
				hash.TransformBlock (tmp, 0, tmp.Length, null, 0);
			}
			if (_raw_body.Length > 0)
				hash.TransformBlock (_raw_body, 0, _raw_body.Length, null, 0);
			byte[] enums = new byte[] {
				(byte)_markupType,
				(byte)_compressType,
				(byte)_diffType
			};
			hash.TransformBlock (enums, 0, enums.Length, null, 0);
		}

		#endregion
	}
}
