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
using System.Net;
using System.Text;

namespace p2pncs.Net.Overlay.DFS.MMLC
{
	[SerializableTypeId (0x40a)]
	public class AuthServerInfo
	{
		[SerializableFieldId (0)]
		Key _publicKey;

		[SerializableFieldId (1)]
		EndPoint _ep;

		public AuthServerInfo (Key publicKey, EndPoint ep)
		{
			if (publicKey == null || ep == null)
				throw new ArgumentNullException ();
			if (!(ep is DnsEndPoint || ep is KeyEndPoint || ep is IPEndPoint))
				throw new ArgumentException ();
			_publicKey = publicKey;
			_ep = ep;
		}

		public Key PublicKey {
			get { return _publicKey; }
		}

		public EndPoint EndPoint {
			get { return _ep; }
		}

		void ToParsableString (StringBuilder sb)
		{
			sb.Append (_publicKey.ToBase64String ());
			sb.Append (';');
			if (_ep is KeyEndPoint) {
				sb.Append ("k;");
			} else if (_ep is DnsEndPoint) {
				sb.Append ("d;");
				sb.Append ((_ep as DnsEndPoint).DNS);
				sb.Append (':');
				sb.Append ((_ep as DnsEndPoint).Port);
			} else if (_ep is IPEndPoint) {
				sb.Append ("i;");
				sb.Append ((_ep as IPEndPoint).Address.ToString ());
				sb.Append (':');
				sb.Append ((_ep as IPEndPoint).Port);
			}
		}

		public string ToParsableString ()
		{
			StringBuilder sb = new StringBuilder ();
			ToParsableString (sb);
			return sb.ToString ();
		}

		public static string ToParsableString (AuthServerInfo[] list)
		{
			if (list == null || list.Length == 0)
				return string.Empty;

			StringBuilder sb = new StringBuilder ();
			for (int i = 0; i < list.Length - 1; i ++) {
				list[i].ToParsableString (sb);
				sb.Append (',');
			}
			list[list.Length - 1].ToParsableString (sb);

			return sb.ToString ();
		}

		public static AuthServerInfo Parse (string text)
		{
			string[] items = text.Split (';');
			if (items.Length != 3)
				throw new FormatException ();
			Key pubKey = Key.FromBase64 (items[0]);
			EndPoint ep;
			string[] tmp;
			switch (items[1]) {
				case "k":
					ep = new KeyEndPoint ();
					break;
				case "d":
					tmp = items[2].Split (':');
					if (tmp.Length != 2)
						throw new FormatException ();
					ep = new DnsEndPoint (tmp[0], ushort.Parse (tmp[1]));
					break;
				case "i":
					tmp = items[2].Split (':');
					if (tmp.Length != 2)
						throw new FormatException ();
					ep = new IPEndPoint (IPAddress.Parse (tmp[0]), ushort.Parse (tmp[1]));
					break;
				default:
					throw new FormatException ();
			}
			return new AuthServerInfo (pubKey, ep);
		}

		public static AuthServerInfo[] ParseArray (string text)
		{
			if (text.Length == 0)
				return null;
			string[] items = text.Split (',');
			AuthServerInfo[] list = new AuthServerInfo[items.Length];
			for (int i = 0; i < items.Length; i ++)
				list[i] = AuthServerInfo.Parse (items[i]);
			return list;
		}

		[SerializableTypeId (0x40b)]
		public class DnsEndPoint : EndPoint
		{
			[SerializableFieldId (0)]
			string _dns;

			[SerializableFieldId (1)]
			ushort _port;

			public DnsEndPoint (string dns, ushort port)
			{
				_dns = dns;
				_port = port;
			}

			public string DNS {
				get { return _dns; }
			}

			public ushort Port {
				get { return _port; }
			}
		}

		[SerializableTypeId (0x40c)]
		public class KeyEndPoint : EndPoint
		{
			// AuthServerInfo.PublicKey is EndPoint of KBR
			public KeyEndPoint () {}
		}
	}
}
