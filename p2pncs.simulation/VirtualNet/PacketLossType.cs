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
	public static class PacketLossType
	{
		public static IPacketLossRate Constant (double rate)
		{
			return new ConstantLossRate (rate);
		}

		public static IPacketLossRate Lossless ()
		{
			return new ConstantLossRate (0.0);
		}

		class ConstantLossRate : IPacketLossRate
		{
			double _rate;

			public ConstantLossRate (double rate)
			{
				_rate = rate;
			}

			public double ComputePacketLossRate (EndPoint src, EndPoint dst)
			{
				return _rate;
			}
		}
	}
}
