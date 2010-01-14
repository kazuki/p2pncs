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

using System;
using p2pncs.Net;

namespace p2pncs.Simulation
{
	public class ConstantRTO : IRTOAlgorithm
	{
		TimeSpan _rto;

		public ConstantRTO (TimeSpan rto)
		{
			_rto = rto;
		}

		public void AddSample (TimeSpan rtt)
		{
		}

		public void AddSample (System.Net.EndPoint ep, TimeSpan rtt)
		{
		}

		public TimeSpan GetRTO ()
		{
			return _rto;
		}

		public TimeSpan GetRTO (System.Net.EndPoint ep)
		{
			return _rto;
		}
	}
}
