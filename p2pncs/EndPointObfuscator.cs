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
using System.Collections.Generic;
using System.Net;
using System.Text;
using p2pncs.Net;
using p2pncs.Net.Overlay;
using p2pncs.Utility;

namespace p2pncs
{
	static class EndPointObfuscator
	{
		const byte TYPE_IP = 0;
		const byte TYPE_DNS = 1;
		static readonly byte[] XOR_KEY;
		static Dictionary<char, byte> DNS_MAP = new Dictionary<char, byte> ();
		static Dictionary<byte, string> DNS_RMAP = new Dictionary<byte, string> ();
		static List<CEntry> DNS_CLIST = new List<CEntry> ();

		static EndPointObfuscator ()
		{
			// 0 - 37
			const string TXT = "abcdefghijklmnopqrstuvwxyz0123456789-.";
			for (int i = 0; i < TXT.Length; i ++)
				AddDnsMap (i, TXT[i]);

			// 38 - 62
			AddCDnsMap (38, 'A', ".com");
			AddCDnsMap (39, 'B', ".info");
			AddCDnsMap (40, 'C', ".net");
			AddCDnsMap (41, 'D', ".org");
			AddCDnsMap (42, 'E', ".co.jp");
			AddCDnsMap (43, 'F', ".ne.jp");
			AddCDnsMap (44, 'G', ".jp");

			// 63 = NULL

			XOR_KEY = new byte[] {
				0x63, 0x7c, 0x77, 0x7b, 0xf2, 0x6b, 0x6f, 0xc5, 0x30, 0x01, 0x67, 0x2b, 0xfe, 0xd7, 0xab, 0x76,
				0xca, 0x82, 0xc9, 0x7d, 0xfa, 0x59, 0x47, 0xf0, 0xad, 0xd4, 0xa2, 0xaf, 0x9c, 0xa4, 0x72, 0xc0,
				0xb7, 0xfd, 0x93, 0x26, 0x36, 0x3f, 0xf7, 0xcc, 0x34, 0xa5, 0xe5, 0xf1, 0x71, 0xd8, 0x31, 0x15,
				0x04, 0xc7, 0x23, 0xc3, 0x18, 0x96, 0x05, 0x9a, 0x07, 0x12, 0x80, 0xe2, 0xeb, 0x27, 0xb2, 0x75,
				0x09, 0x83, 0x2c, 0x1a, 0x1b, 0x6e, 0x5a, 0xa0, 0x52, 0x3b, 0xd6, 0xb3, 0x29, 0xe3, 0x2f, 0x84,
				0x53, 0xd1, 0x00, 0xed, 0x20, 0xfc, 0xb1, 0x5b, 0x6a, 0xcb, 0xbe, 0x39, 0x4a, 0x4c, 0x58, 0xcf,
				0xd0, 0xef, 0xaa, 0xfb, 0x43, 0x4d, 0x33, 0x85, 0x45, 0xf9, 0x02, 0x7f, 0x50, 0x3c, 0x9f, 0xa8,
				0x51, 0xa3, 0x40, 0x8f, 0x92, 0x9d, 0x38, 0xf5, 0xbc, 0xb6, 0xda, 0x21, 0x10, 0xff, 0xf3, 0xd2,
				0xcd, 0x0c, 0x13, 0xec, 0x5f, 0x97, 0x44, 0x17, 0xc4, 0xa7, 0x7e, 0x3d, 0x64, 0x5d, 0x19, 0x73,
				0x60, 0x81, 0x4f, 0xdc, 0x22, 0x2a, 0x90, 0x88, 0x46, 0xee, 0xb8, 0x14, 0xde, 0x5e, 0x0b, 0xdb,
				0xe0, 0x32, 0x3a, 0x0a, 0x49, 0x06, 0x24, 0x5c, 0xc2, 0xd3, 0xac, 0x62, 0x91, 0x95, 0xe4, 0x79,
				0xe7, 0xc8, 0x37, 0x6d, 0x8d, 0xd5, 0x4e, 0xa9, 0x6c, 0x56, 0xf4, 0xea, 0x65, 0x7a, 0xae, 0x08,
				0xba, 0x78, 0x25, 0x2e, 0x1c, 0xa6, 0xb4, 0xc6, 0xe8, 0xdd, 0x74, 0x1f, 0x4b, 0xbd, 0x8b, 0x8a,
				0x70, 0x3e, 0xb5, 0x66, 0x48, 0x03, 0xf6, 0x0e, 0x61, 0x35, 0x57, 0xb9, 0x86, 0xc1, 0x1d, 0x9e,
				0xe1, 0xf8, 0x98, 0x11, 0x69, 0xd9, 0x8e, 0x94, 0x9b, 0x1e, 0x87, 0xe9, 0xce, 0x55, 0x28, 0xdf,
				0x8c, 0xa1, 0x89, 0x0d, 0xbf, 0xe6, 0x42, 0x68, 0x41, 0x99, 0x2d, 0x0f, 0xb0, 0x54, 0xbb, 0x16
			};
		}

		static void AddDnsMap (int id, char c)
		{
			DNS_MAP.Add (c, (byte)id);
			DNS_RMAP.Add ((byte)id, c.ToString ());
		}

		static void AddCDnsMap (byte id, char c, string txt)
		{
			DNS_CLIST.Add (new CEntry (txt, c));
			DNS_MAP.Add (c, (byte)id);
			DNS_RMAP.Add ((byte)id, txt);
		}

		public static string Encode (EndPoint ep)
		{
			byte[] raw;
			if (ep is IPEndPoint) {
				raw = Encode (ep as IPEndPoint);
			} else if (ep is DnsEndPoint) {
				raw = Encode (ep as DnsEndPoint);
			} else {
				throw new ArgumentException ();
			}
			return "%" + new Key (Encode (raw)).ToString () + "%";
		}

		static byte[] Encode (IPEndPoint ep)
		{
			byte[] raw = new byte[]{TYPE_IP}.Join (ep.Address.GetAddressBytes (), new byte[] {
				(byte)(ep.Port >> 8), (byte)(ep.Port & 0xFF)
			});
			return raw;
		}

		static byte[] Encode (DnsEndPoint ep)
		{
			string dns = ep.DNS.ToLowerInvariant ();

			for (int i = 0; i < dns.Length; i ++) {
				char c = dns[i];
				if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || c == '.' || c == '-')
					continue;
				throw new FormatException ();
			}

			dns = Compress (dns);
			byte[] raw = new byte[dns.Length];
			for (int i = 0; i < dns.Length; i ++)
				raw[i] = DNS_MAP[dns[i]];
			raw = Compress (raw);		

			raw = new byte[]{TYPE_DNS}.Join (raw, new byte[] {
				(byte)(ep.Port >> 8), (byte)(ep.Port & 0xFF)
			});
			return raw;
		}

		public static EndPoint Decode (string str)
		{
			if (!str.StartsWith ("%") || !str.EndsWith ("%"))
				throw new FormatException ();
			str = str.Substring (1, str.Length - 2);
			byte[] raw = Decode (Key.Parse (str).GetByteArray ());
			switch (raw[0]) {
				case TYPE_IP:
					return new IPEndPoint (new IPAddress (raw.CopyRange (1, raw.Length - 3)),
						(raw[raw.Length - 2] << 8) | raw[raw.Length - 1]);
				case TYPE_DNS:
					return new DnsEndPoint (DecodeDns (raw.CopyRange (1, raw.Length - 3)),
						(ushort)((raw[raw.Length - 2] << 8) | raw[raw.Length - 1]));
				default:
					throw new FormatException ();
			}
		}

		static string DecodeDns (byte[] raw)
		{
			raw = Decompress (raw);
			StringBuilder sb = new StringBuilder (raw.Length);
			for (int i = 0; i < raw.Length; i ++)
				sb.Append (DNS_RMAP[raw[i]]);
			return sb.ToString ();
		}

		static byte[] Encode (byte[] raw)
		{
			for (int i = 1; i < raw.Length; i ++)
				raw[i] ^= XOR_KEY[raw[i - 1]];
			raw[0] ^= XOR_KEY[raw[raw.Length - 1]];
			return raw;
		}

		static byte[] Decode (byte[] raw)
		{
			raw[0] ^= XOR_KEY[raw[raw.Length - 1]];
			for (int i = raw.Length - 1; i > 0; i--)
				raw[i] ^= XOR_KEY[raw[i - 1]];
			return raw;
		}

		static string Compress (string dns)
		{
			for (int i = 0; i < DNS_CLIST.Count; i++) {
				if (dns.Contains (DNS_CLIST[i].Text))
					dns = dns.Replace (DNS_CLIST[i].Text, DNS_CLIST[i].Char.ToString ());
			}
			return dns;
		}

		static byte[] Compress (byte[] raw6)
		{
			int blocks = raw6.Length / 4;
			int mod = raw6.Length % 4;
			byte[] raw = new byte[blocks * 3 + mod];
			int idx1 = 0, idx2 = 0;
			for (int i = 0; i < blocks; i ++, idx1 += 4, idx2 += 3) {
				raw[idx2 + 0] = (byte)((raw6[idx1 + 0] << 2) | (raw6[idx1 + 1] >> 4));
				raw[idx2 + 1] = (byte)((raw6[idx1 + 1] << 4) | (raw6[idx1 + 2] >> 2));
				raw[idx2 + 2] = (byte)((raw6[idx1 + 2] << 6) | (raw6[idx1 + 3] >> 0));
			}
			switch (mod) {
				case 1:
					raw[idx2 + 0] = (byte)(raw6[idx1 + 0] << 2);
					break;
				case 2:
					raw[idx2 + 0] = (byte)((raw6[idx1 + 0] << 2) | (raw6[idx1 + 1] >> 4));
					raw[idx2 + 1] = (byte)(raw6[idx1 + 1] << 4);
					break;
				case 3:
					raw[idx2 + 0] = (byte)((raw6[idx1 + 0] << 2) | (raw6[idx1 + 1] >> 4));
					raw[idx2 + 1] = (byte)((raw6[idx1 + 1] << 4) | (raw6[idx1 + 2] >> 2));
					raw[idx2 + 2] = (byte)((raw6[idx1 + 2] << 6) | 0x3F);
					break;
			}
			return raw;
		}

		static byte[] Decompress (byte[] raw)
		{
			int blocks = raw.Length / 3;
			int mod = raw.Length % 3;
			byte[] raw6 = new byte[blocks * 4 + mod];
			int idx1 = 0, idx2 = 0;
			for (int i = 0; i < blocks; i++, idx1 += 4, idx2 += 3) {
				raw6[idx1 + 0] = (byte)(raw[idx2 + 0] >> 2);
				raw6[idx1 + 1] = (byte)(((raw[idx2 + 0] & 0x3) << 4) | (raw[idx2 + 1] >> 4));
				raw6[idx1 + 2] = (byte)(((raw[idx2 + 1] & 0xF) << 2) | (raw[idx2 + 2] >> 6));
				raw6[idx1 + 3] = (byte)(raw[idx2 + 2] & 0x3F);
			}
			switch (mod) {
				case 1:
					raw6[idx1 + 0] = (byte)(raw[idx2 + 0] >> 2);
					break;
				case 2:
					raw6[idx1 + 0] = (byte)(raw[idx2 + 0] >> 2);
					raw6[idx1 + 1] = (byte)(((raw[idx2 + 0] & 0x3) << 4) | (raw[idx2 + 1] >> 4));
					break;
			}
			if (raw6.Length > 0 && raw6[raw6.Length - 1] == 0x3F)
				Array.Resize<byte> (ref raw6, raw6.Length - 1);
			return raw6;
		}

		struct CEntry
		{
			public string Text;
			public char Char;

			public CEntry (string l, char c)
			{
				this.Text = l;
				this.Char = c;
			}
		}
	}
}
