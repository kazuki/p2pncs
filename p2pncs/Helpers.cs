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

using System.Collections.Generic;
using System.Net;
using Kazuki.Net.HttpServer;
using p2pncs.Net;

namespace p2pncs
{
	static class Helpers
	{
		public static EndPoint Parse (string value)
		{
			try {
				string[] items = value.Split (':');
				if (items.Length != 2)
					return null;
				ushort port;
				if (!ushort.TryParse (items[1], out port))
					return null;
				IPAddress adrs;
				if (!IPAddress.TryParse (items[0], out adrs)) {
					return new DnsEndPoint (items[0], (ushort)port);
				}
				return new IPEndPoint (adrs, port);
			} catch {
				return null;
			}
		}

		public static string GetQueryValue (IHttpRequest req, string name)
		{
			string value;
			if (req.QueryData.TryGetValue (name, out value))
				return value;
			return string.Empty;
		}

		public static string GetValueSafe (Dictionary<string, string> dic, string name)
		{
			string value;
			if (!dic.TryGetValue (name, out value) || value == null)
				value = string.Empty;
			return value;
		}
	}
}
