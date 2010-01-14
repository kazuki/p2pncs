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

namespace p2pncs.Net.Overlay.DFS.MMLC
{
	[SerializableTypeId (0x40e)]
	public class CaptchaChallengeRequest
	{
		[SerializableFieldId (0)]
		byte[] _hash;

		[SerializableFieldId (1)]
		byte[] _iv;
		
		[SerializableFieldId (2)]
		byte[] _key;

		public CaptchaChallengeRequest (byte[] hash, byte[] iv, byte[] key)
		{
			_hash = hash;
			_iv = iv;
			_key = key;
		}

		public byte[] Hash {
			get { return _hash; }
		}

		public byte[] IV {
			get { return _iv; }
		}

		public byte[] Key {
			get { return _key; }
		}
	}
}
