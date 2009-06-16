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
using System.IO;
using System.Text;
using openCrypto.EllipticCurve;
using p2pncs.Security.Cryptography;

namespace p2pncs.Net.Overlay
{
	[Serializable]
	public sealed class Key : IComparable<Key>, IComparable, IEquatable<Key>
	{
		static int[] RadixToBitLengtTable = new int[] {
			0, 0,
			1, /* radix-2 */
			0,
			2, /* radix-4 */
			0, 0, 0, 
			3, /* radix-8 */
			0, 0, 0, 0, 0, 0, 0,
			4, /* radix-16 */
		};
		static byte[] BitMask = new byte[] {
			0x0, 0x1, 0x3, 0x7, 0xF
		};

		byte[] _data;
		int _hash;

		#region Constructors
		public Key (byte[] raw) : this (raw, 0, raw.Length)
		{
		}

		public Key (byte[] raw, int offset, int size)
		{
			uint hash = 0;
			
			_data = new byte[size];
			Buffer.BlockCopy (raw, offset, _data, 0, size);

			int i = offset;
			for (; i < offset + size - 4; i += 4) {
				uint tmp = ((uint)raw[i]) | ((uint)raw[i + 1] << 8) |
					((uint)raw[i + 2] << 16) | ((uint)raw[i + 3] << 24);
				hash ^= tmp;
			}
			switch ((offset + size) - i) {
				case 1:
					hash ^= raw[i];
					break;
				case 2:
					hash ^= ((uint)raw[i]) | ((uint)raw[i + 1] << 8);
					break;
				case 3:
					hash ^= ((uint)raw[i]) | ((uint)raw[i + 1] << 8) | ((uint)raw[i + 2] << 16);
					break;
			}
			_hash = (int)hash;
		}
		#endregion

		#region Properties
		public int KeyBytes {
			get { return _data.Length; }
		}

		public int KeyBits {
			get { return _data.Length * 8; }
		}
		#endregion

		#region Bitwise Methods
		public Key Xor (Key key)
		{
			if (_data.Length != key._data.Length)
				throw new ArgumentException ();

			byte[] data = new byte[_data.Length];
			for (int i = 0; i < data.Length; i++)
				data[i] = (byte)(_data[i] ^ key._data[i]);
			return new Key (data);
		}

		public int ReadBits (int bitPos, int bits)
		{
			int int_pos = bitPos / 8;
			int bit_pos = bitPos % 8;
			byte mask = BitMask[bits];

			if (bit_pos + bits <= 8) {
				return (int)((_data[int_pos] >> bit_pos) & mask);
			}

			int value = (_data[int_pos] >> bit_pos) & mask;
			int shift = 8 - bit_pos;
			mask >>= shift;
			return (int)((value << shift) | (_data[int_pos + 1] & mask));
		}

		public int GetDigit (int radix, int idx)
		{
			int bits = RadixToBitLengtTable[radix];
			if (bits == 0)
				throw new ArgumentOutOfRangeException ();
			return ReadBits (bits * idx, bits);
		}

		public static int MatchBitsFromMSB (Key key1, Key key2)
		{
			if (key1._data.Length != key2._data.Length)
				throw new ArgumentException ();
			for (int i = key1._data.Length - 1, q = 0; i >= 0; i --, q ++)
				if (key1._data[i] != key2._data[i]) {
					byte mask = 0x80;
					byte k1 = key1._data[i], k2 = key2._data[i];
					for (int k = 0; mask != 0; k ++, mask >>= 1) {
						if ((k1 & mask) != (k2 & mask))
							return (q << 3) + k;
					}
				}

			// equals
			return key1.KeyBits;
		}

		public static int MatchDigitsFromMSB (Key key1, Key key2, int radix)
		{
			int bits = RadixToBitLengtTable[radix];
			if (bits == 0)
				throw new ArgumentOutOfRangeException ();
			int matchBits = MatchBitsFromMSB (key1, key2);
			return matchBits / bits;
		}
		#endregion

		#region Misc
		public static Key CreateRandom (int bytes)
		{
			return new Key (openCrypto.RNG.GetRNGBytes (bytes), 0, bytes);
		}
		public void WriteTo (Stream strm)
		{
			strm.Write (_data, 0, _data.Length);
		}

		public void CopyTo (byte[] array, int offset)
		{
			Buffer.BlockCopy (_data, 0, array, offset, _data.Length);
		}

		public byte[] GetByteArray ()
		{
			return (byte[])_data.Clone ();
		}

		public static bool Equals (Key keyA, Key keyB)
		{
			if (keyA == keyB)
				return true;
			if (keyA == null)
				return false;
			return keyA.Equals (keyB);
		}
		#endregion

		#region Convert to/from ECKeyPair
		public static Key Create (ECKeyPair pair)
		{
			byte[] pub = pair.ExportPublicKey (true);
			return new Key (pub);
		}
		public ECKeyPair ToECPublicKey ()
		{
			return ECKeyPair.CreatePublic (DefaultAlgorithm.GetDefaultDomainName (KeyBytes), _data);
		}
		#endregion

		#region IComparable<Key>, IComparable, IEquatable<Key> Members

		public int CompareTo (Key other)
		{
			if (other._data.Length < this._data.Length)
				return 1;
			else if (other._data.Length > this._data.Length)
				return -1;
			for (int i = _data.Length - 1; i >= 0; i --) {
				if (this._data[i] > other._data[i])
					return 1;
				else if (this._data[i] < other._data[i])
					return -1;
			}
			return 0;
		}

		public int CompareTo (object obj)
		{
			return CompareTo ((Key)obj);
		}

		public bool Equals (Key other)
		{
			if (other._data.Length != this._data.Length)
				return false;
			for (int i = 0; i < _data.Length; i ++) {
				if (this._data[i] != other._data[i])
					return false;
			}
			return true;
		}

		#endregion

		#region Override Members
		public override int GetHashCode ()
		{
			return _hash;
		}

		public override bool Equals (object obj)
		{
			return Equals ((Key)obj);
		}

		public override string ToString ()
		{
			char[] buf = new char[_data.Length * 2];
			for (int i = 0, q = buf.Length; i < _data.Length; i ++, q -= 2) {
				buf[q - 1] = ToHex ((byte)(_data[i] & 0xF));
				buf[q - 2] = ToHex ((byte)((_data[i] >>  4) & 0xF));
			}
			return new string (buf);
		}

		public string ToString (int radix)
		{
			if (radix == 16)
				return ToString ();

			StringBuilder sb = new StringBuilder ();
			int radix_bits = RadixToBitLengtTable[radix];
			if (radix_bits == 0)
				throw new ArgumentOutOfRangeException ();
			for (int i = KeyBits / radix_bits - 1; i >= 0; i --) {
				int digit = GetDigit (radix, i);
				if (digit < 10)
					sb.Append ((char)('0' + digit));
				else
					sb.Append ((char)('a' + (digit - 10)));
			}
			return sb.ToString ();
		}

		static char ToHex (byte value)
		{
			if (value < 10)
				return (char)('0' + value);
			return (char)('a' + (value - 10));
		}
		#endregion

		#region Parse
		public static Key Parse (string text)
		{
			return Parse (text, 16);
		}
		public static Key Parse (string text, int radix)
		{
			if (radix != 16)
				throw new NotImplementedException ();
			if (text.Length % 2 != 0)
				throw new ArgumentException ();
			byte[] raw = new byte[text.Length / 2];
			for (int i = 0, q = raw.Length - 1; i < text.Length; i += 2, q --)
				raw[q] = (byte)((FromHex (text[i]) << 4) | FromHex (text[i + 1]));
			return new Key (raw);
		}
		static int FromHex (char c)
		{
			if (c >= '0' && c <= '9')
				return c - '0';
			else if (c >= 'a' && c <= 'z')
				return c - 'a' + 10;
			else if (c >= 'A' && c <= 'Z')
				return c - 'A' + 10;
			throw new FormatException ();
		}
		#endregion
	}
}
