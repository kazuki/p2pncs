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

namespace p2pncs.Net.Overlay
{
	public interface IKeyBasedRoutingAlgorithm
	{
		void Join (EndPoint[] initialNodes);
		void Close ();

		Key ComputeDistance (Key x, Key y);
		int ComputeRoutingLevel (Key x, Key y);
		int MaxRoutingLevel { get; }

		NodeHandle[] GetNextHopNodes (Key dest, int maxNum, Key exclude);
		NodeHandle[] GetRandomNodes (int maxNum);
		NodeHandle[] GetNeighbors (int maxNum);
		NodeHandle[] GetCloseNodes (Key target, int maxNum);

		void Touch (NodeHandle node);
		void Fail (NodeHandle node);

		void Setup (Key selfNodeId, IKeyBasedRouter router);
		void Stabilize ();
	}
}
