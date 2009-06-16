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

namespace p2pncs
{
	[SerializableTypeId (0x1004)]
	class SimpleBBSRecord : IHashComputable
	{
		[SerializableFieldId (0)]
		string _name;

		[SerializableFieldId (1)]
		string _body;

		[SerializableFieldId (2)]
		DateTime _posted;

		public SimpleBBSRecord (string name, string body, DateTime posted)
		{
			_name = name;
			_body = body;
			_posted = posted;
		}

		public string Name {
			get { return _name; }
		}

		public string Body {
			get { return _body; }
		}

		public DateTime PostedTime {
			get { return _posted; }
		}

		#region IHashComputable Members

		public void ComputeHash (HashAlgorithm hash)
		{
			byte[] tmp = Encoding.UTF8.GetBytes (_name);
			hash.TransformBlock (tmp, 0, tmp.Length, null, 0);
			tmp = Encoding.UTF8.GetBytes (_body);
			hash.TransformBlock (tmp, 0, tmp.Length, null, 0);
			tmp = BitConverter.GetBytes (_posted.ToUniversalTime ().Ticks);
			hash.TransformBlock (tmp, 0, tmp.Length, null, 0);
		}

		#endregion
	}
}
