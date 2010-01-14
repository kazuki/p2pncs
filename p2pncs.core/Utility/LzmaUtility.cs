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

using System.IO;
using SevenZip.Compression.LZMA;

namespace p2pncs.Utility
{
	public static class LzmaUtility
	{
		public static byte[] Compress (byte[] data)
		{
			using (MemoryStream instrm = new MemoryStream (data))
			using (MemoryStream outstrm = new MemoryStream ()) {
				Encoder encoder = new Encoder ();
				encoder.WriteCoderProperties (outstrm);
				outstrm.WriteByte ((byte)(data.Length >> 24));
				outstrm.WriteByte ((byte)(data.Length >> 16));
				outstrm.WriteByte ((byte)(data.Length >> 8));
				outstrm.WriteByte ((byte)(data.Length >> 0));
				encoder.Code (instrm, outstrm, -1, -1, null);
				outstrm.Close ();
				return outstrm.ToArray ();
			}
		}

		public static byte[] Decompress (byte[] data)
		{
			using (MemoryStream instrm = new MemoryStream (data))
			using (MemoryStream outstrm = new MemoryStream ()) {
				Decoder decoder = new Decoder ();
				byte[] tmp = new byte[5];
				instrm.Read (tmp, 0, 5);
				decoder.SetDecoderProperties (tmp);

				instrm.Read (tmp, 0, 4);
				int outsize = (tmp[0] << 24) | (tmp[1] << 16) | (tmp[2] << 8) | tmp[3];
				decoder.Code (instrm, outstrm, data.Length - 9, outsize, null);
				outstrm.Close ();
				return outstrm.ToArray ();
			}
		}
	}
}
