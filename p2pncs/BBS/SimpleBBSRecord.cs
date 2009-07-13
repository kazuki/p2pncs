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
using p2pncs.Security.Cryptography;
using p2pncs.Net.Overlay.DFS.MMLC;

namespace p2pncs.BBS
{
	[SerializableTypeId (0x1004)]
	class SimpleBBSRecord : IHashComputable, IMergeableFile
	{
		[SerializableFieldId (0)]
		string _name;

		[SerializableFieldId (1)]
		string _body;

		public SimpleBBSRecord (string name, string body)
		{
			_name = name;
			_body = body;
		}

		public string Name {
			get { return _name; }
		}

		public string Body {
			get { return _body; }
		}

		const string TABLE = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
		public string GetShortId (MergeableFileRecord record)
		{
			byte[] hash = record.Hash.GetByteArray ();
			int value = (hash[0] << 16) | (hash[1] << 8) | hash[2];
			char[] buf = new char[4];
			for (int i = 0; i < 4; i ++) {
				buf[i] = TABLE[value % TABLE.Length];
				value /= TABLE.Length;
			}
			return new string (buf);
		}

		#region IHashComputable Members

		public void ComputeHash (HashAlgorithm hash)
		{
			byte[] tmp = Encoding.UTF8.GetBytes (_name);
			hash.TransformBlock (tmp, 0, tmp.Length, null, 0);
			tmp = Encoding.UTF8.GetBytes (_body);
			hash.TransformBlock (tmp, 0, tmp.Length, null, 0);
		}

		#endregion

		#region IMergeableFile Members

		public IMergeableFileWebUIHelper WebUIHelper {
			get { return SimpleBBSWebUIHelper.Instance; }
		}

		#endregion
	}
}
