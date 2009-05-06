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

namespace p2pncs.Simulation.VirtualNet
{
	public static class LatencyTypes
	{
		public static ILatency Constant (int ms)
		{
			return Constant (TimeSpan.FromMilliseconds (ms));
		}

		public static ILatency Constant (TimeSpan latency)
		{
			return new ConstantLatency (latency);
		}

		class ConstantLatency : ILatency
		{
			TimeSpan _latency;

			public ConstantLatency (TimeSpan latency)
			{
				_latency = latency;
			}

			public TimeSpan ComputeLatency (EndPoint src, EndPoint dst)
			{
				return _latency;
			}

			public TimeSpan MaxLatency {
				get { return _latency; }
			}
		}
	}
}
