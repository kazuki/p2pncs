/*
 * Copyright (C) 2009-2010 Kazuki Oikawa
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

namespace p2pncs.BBS
{
	[SerializableTypeId (0x1003)]
	class SimpleBBSHeader : IHashComputable, IMergeableFile
	{
		public SimpleBBSHeader ()
		{
		}

		#region IHashComputable Members

		public void ComputeHash (HashAlgorithm hash)
		{
		}

		#endregion

		#region IMergeableFile Members

		public IMergeableFileWebUIHelper WebUIHelper {
			get { return SimpleBBSWebUIHelper.Instance; }
		}

		public WebApp.IMergeableFileCommonProcess WebUIMergeableFileCommon {
			get { return BBSWebApp.Instance; }
		}

		#endregion
	}
}
