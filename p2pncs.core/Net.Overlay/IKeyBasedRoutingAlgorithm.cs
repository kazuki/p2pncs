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

namespace p2pncs.Net.Overlay
{
	public interface IKeyBasedRoutingAlgorithm
	{
		void Setup (IKeyBasedRouter router);
		void NewApp (Key appId);
		void Join (Key appId, EndPoint[] initialNodes);
		void Close (Key appId);
		void Close ();

		void SetEndPointOption (object opt);
		void RemoveEndPointOption (Type optType);

		Key ComputeDistance (Key x, Key y);
		NodeHandle[] GetCloseNodes (Key appId, Key target, int maxNum);
		NodeHandle[] GetRandomNodes (Key appId, int maxNum);

		void Touch (MultiAppNodeHandle node);
		void Touch (EndPoint ep);
		void Fail (EndPoint ep);

		void Stabilize (Key appId);

		int GetRoutingTableSize ();
		int GetRoutingTableSize (Key appId);

		MultiAppNodeHandle SelfNodeHandle { get; }
	}
}
