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

namespace p2pncs.Utility
{
	public class AntiReplayWindow
	{
		uint _lastSeq = 0;
		int _windowSize;
		ulong[] _bitmaps;

		public AntiReplayWindow (int windowSize)
		{
			if ((windowSize % 64) != 0)
				throw new System.NotImplementedException ();
			_bitmaps = new ulong[windowSize / 64];
			_windowSize = windowSize;
			_lastSeq = (uint)windowSize;
		}

		/// <summary>Reference: RFC2401 Appendix.C</summary>
		public bool Check (uint seq)
		{
			uint diff;
			int idx, bits;
			if (seq == 0)
				return false; /* first == 0 or wrapped */
			if (seq > _lastSeq) { /* new larger sequence number */
				diff = seq - _lastSeq;
				if (diff < _windowSize) { /* In window */
					idx = (int)(diff >> 6);
					bits = (int)(diff & 0x3f);
					int i = _bitmaps.Length - idx - 1;
					for (; i > 0; i --)
						_bitmaps[i + idx] = (_bitmaps[i] << bits) | (_bitmaps[i - 1] >> (64 - bits));
					_bitmaps[i + idx] = _bitmaps[i] << bits;
				} else { /* This packet has a "way larger" */
					for (int i = 0; i < _bitmaps.Length; i ++)
						_bitmaps[i] = 0;
				}
				_bitmaps[0] |= 1;
				_lastSeq = seq;
				return true; /* larger is good */
			}
			diff = _lastSeq - seq;
			if (diff >= _windowSize)
				return false; // too old or wrapped
			idx = (int)(diff >> 6);
			bits = (int)(diff & 0x3f);
			if ((_bitmaps[idx] & (1UL << bits)) != 0)
				return false; /* already seen */
			_bitmaps[idx] |= 1UL << bits; /* mark as seen */
			return true; /* out of order but good */
		}
	}
}
