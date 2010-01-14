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

namespace p2pncs.Net.Overlay
{
	public class RoutingResult
	{
		NodeHandle[] _candidates;
		int _hops;
		//FailReason _reason = FailReason.None;

		public RoutingResult (NodeHandle[] candidates, int hops)
		{
			_candidates = candidates;
			_hops = hops;
		}

#if false
		public RoutingResult (FailReason reason, int hops)
		{
			_reason = reason;
			_hops = hops;
		}
#endif

		/// <remarks>配列の要素のEndPointがnullだった場合は、自身のノードを意味する</remarks>
		public NodeHandle[] RootCandidates {
			get { return _candidates; }
		}

		public int Hops {
			get { return _hops; }
		}

#if fasle
		public FailReason FailReason {
			get { return _reason; }
		}
#endif
	}
}
