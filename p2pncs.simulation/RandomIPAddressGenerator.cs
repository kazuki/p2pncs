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
using p2pncs.Net;

namespace p2pncs.Simulation
{
	public class RandomIPAddressGenerator
	{
		HashSet<IPAddress> _generated = new HashSet<IPAddress> ();
		Random _rnd = new Random ();
		byte[] _buffer = new byte[4];

		public IPAddress Next ()
		{
			lock (_generated) {
				byte[] x = _buffer;
				while (true) {
					// Generate Random Address
					_rnd.NextBytes (x);
					IPAddress adrs = new IPAddress (x);

					// Check: Private Address
					if (IPAddressUtility.IsPrivate (adrs))
						continue;

					// Check: Class-full Broadcast Address
					if (x[0] < 128 && ((x[1] == 0 && x[2] == 0 && x[3] == 0) || (x[1] == 255 && x[2] == 255 && x[3] == 255)))
						continue;
					if (x[0] < 192 && ((x[2] == 0 && x[3] == 0) || (x[2] == 255 && x[3] == 255)))
						continue;
					if (x[0] < 223 && (x[3] == 0 || x[3] == 255))
						continue;

					// Check: Already Generated Address
					if (_generated.Add (adrs))
						return adrs;
				}
			}
		}
	}
}
