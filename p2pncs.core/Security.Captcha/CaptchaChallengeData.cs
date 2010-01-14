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

namespace p2pncs.Security.Captcha
{
	[SerializableTypeId (0x40d)]
	public class CaptchaChallengeData
	{
		[SerializableFieldId (0)]
		byte _specific_id;

		[SerializableFieldId (1)]
		byte[] _token;

		[SerializableFieldId (2)]
		byte[] _data;

		public CaptchaChallengeData (byte specific, byte[] token, byte[] data)
		{
			_specific_id = specific;
			_token = token;
			_data = data;
		}

		public byte SpecificID {
			get { return _specific_id; }
		}

		public byte[] Token {
			get { return _token; }
		}

		public byte[] Data {
			get { return _data; }
		}
	}
}
