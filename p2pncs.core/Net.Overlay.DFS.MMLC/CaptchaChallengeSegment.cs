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
	[SerializableTypeId (0x40f)]
	public class CaptchaChallengeSegment
	{
		[SerializableFieldId (0)]
		byte _idx;

		[SerializableFieldId (1)]
		byte _total;

		[SerializableFieldId (2)]
		byte[] _segment;

		[SerializableFieldId (3)]
		int _id;

		public CaptchaChallengeSegment (byte idx, byte total, byte[] segment, int id)
		{
			_idx = idx;
			_total = total;
			_segment = segment;
			_id = id;
		}

		public byte SegmentIndex {
			get { return _idx; }
		}

		public byte NumberOfSegments {
			get { return _total; }
		}

		public byte[] Segment {
			get { return _segment; }
		}

		public int ID {
			get { return _id; }
		}
	}
}
