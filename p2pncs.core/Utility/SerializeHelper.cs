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

using System.IO;
using p2pncs.Net.Overlay;

namespace p2pncs.Utility
{
	public class SerializeHelper
	{
		public static void RegisterCustomHandler (Serializer serializer)
		{
			serializer.AddCustomHandler (typeof (Key), 0x200, delegate (Stream strm, object obj, byte[] buffer) {
				Key k = (Key)obj;
				strm.WriteByte ((byte)k.KeyBytes);
				k.WriteTo (strm);
			}, delegate (Stream strm, byte[] buffer) {
				byte[] raw = new byte[strm.ReadByte ()];
				strm.Read (raw, 0, raw.Length);
				return new Key (raw);
			});
		}
	}
}
