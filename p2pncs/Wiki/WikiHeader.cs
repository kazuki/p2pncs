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
using p2pncs.Security.Cryptography;

namespace p2pncs.Wiki
{
	[SerializableTypeId (0x1008)]
	class WikiHeader : IHashComputable, IMergeableFile
	{
		[SerializableFieldId (0)]
		string _title;

		[SerializableFieldId (1)]
		bool _freeze;

		public WikiHeader (string title, bool freeze)
		{
			_title = title;
			_freeze = freeze;
		}

		public string Title {
			get { return _title; }
		}

		public bool IsFreeze {
			get { return _freeze; }
		}

		#region IHashComputable Members

		public void ComputeHash (HashAlgorithm hash)
		{
			byte[] tmp = Encoding.UTF8.GetBytes (_title);
			hash.TransformBlock (tmp, 0, tmp.Length, null, 0);
			tmp = new byte[] {(byte)(_freeze ? 1 : 0)};
			hash.TransformBlock (tmp, 0, tmp.Length, null, 0);
		}

		#endregion

		#region IMergeableFile Members

		public IMergeableFileWebUIHelper WebUIHelper {
			get { return WikiWebUIHelper.Instance; }
		}

		#endregion
	}
}
