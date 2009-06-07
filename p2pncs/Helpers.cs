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
using Kazuki.Net.HttpServer;

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
					IAsyncResult ar = Dns.BeginGetHostAddresses (items[0], null, null);
					if (!ar.AsyncWaitHandle.WaitOne (5000))
						return null;
					IPAddress[] list = Dns.EndGetHostAddresses (ar);
					if (list.Length == 0)
						return null;
					adrs = list[0];
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
				return HttpUtility.UrlDecode (value, Encoding.UTF8);
			return string.Empty;
		}
	}
}
