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
		Key[] _parentHashList;

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

		public WikiRecord (string pageName, Key[] parentHashList, string name, WikiMarkupType markupType, string body, byte[] raw_body, WikiCompressType compressType, WikiDiffType diffType)
		{
			_pageName = pageName;
			_parentHashList = parentHashList;
			_name = name;
			_markupType = markupType;
			_raw_body = raw_body;
			_compressType = compressType;
			_diffType = diffType;
			_body = body;
			SyncBodyAndRawBody ();
		}

		public void SyncBodyAndRawBody ()
		{
			if (_body != null)
				return;
			switch (_compressType) {
				case WikiCompressType.None:
					_body = Encoding.UTF8.GetString (_raw_body);
					break;
				case WikiCompressType.LZMA:
					_body = Encoding.UTF8.GetString (p2pncs.Utility.LzmaUtility.Decompress (_raw_body));
					break;
				default:
					throw new FormatException ();
			}
		}

		public string PageName {
			get { return _pageName; }
		}

		public Key[] ParentHashList {
			get { return _parentHashList; }
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
			if (_parentHashList != null) {
				for (int i = 0; i < _parentHashList.Length; i ++)
					hash.TransformBlock (_parentHashList[i].GetByteArray (), 0, _parentHashList[i].KeyBytes, null, 0);
			}
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
