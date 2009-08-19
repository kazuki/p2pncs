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
using System.Net.Sockets;

namespace p2pncs.Net
{
	public static class IPAddressUtility
	{
		public static bool IsPrivate (IPAddress adrs)
		{
			byte[] x = adrs.GetAddressBytes ();

			if (x[0] == 10)
				return true;
			else if (x[0] == 127)
				return true;
			else if (x[0] == 172 && (x[1] >= 16 && x[1] <= 31))
				return true;
			else if (x[0] == 192 && x[1] == 168)
				return true;
			else if (x[0] >= 224) // Class D & E
				return true;

			return false;
		}

		public static IPAddress GetLoopbackAddress (AddressFamily family)
		{
			switch (family) {
				case AddressFamily.InterNetwork:
					return IPAddress.Loopback;
				case AddressFamily.InterNetworkV6:
					return IPAddress.IPv6Loopback;
				default:
					throw new NotSupportedException ();
			}
		}

		public static IPAddress GetAnyAddress (AddressFamily family)
		{
			switch (family) {
				case AddressFamily.InterNetwork:
					return IPAddress.Any;
				case AddressFamily.InterNetworkV6:
					return IPAddress.IPv6Any;
				default:
					throw new NotSupportedException ();
			}
		}

		public static IPAddress GetNoneAddress (AddressFamily family)
		{
			switch (family) {
				case AddressFamily.InterNetwork:
					return IPAddress.None;
				case AddressFamily.InterNetworkV6:
					return IPAddress.IPv6None;
				default:
					throw new NotSupportedException ();
			}
		}
	}
}
