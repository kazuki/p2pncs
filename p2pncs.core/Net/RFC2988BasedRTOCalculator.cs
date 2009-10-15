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
using p2pncs.Threading;

namespace p2pncs.Net
{
	public class RFC2988BasedRTOCalculator : IRTOAlgorithm
	{
		Dictionary<IPAddress, State> _states = null;
		State _state = null;
		int _timerGranularity, _minRTO;
		TimeSpan _defaultRTO;
		static readonly TimeSpan InvalidValue = TimeSpan.MinValue;

		public RFC2988BasedRTOCalculator (TimeSpan defaultRTO, TimeSpan minRTO, int timerGranularity, bool ignoreEP)
		{
			if (ignoreEP) {
				_states = null;
			} else {
				_states = new Dictionary<IPAddress,State> ();
			}
			_timerGranularity = timerGranularity;
			_minRTO = (int)minRTO.TotalMilliseconds;
			_defaultRTO = defaultRTO;
		}

		public void AddSample (EndPoint ep, TimeSpan rtt)
		{
			State state = GetState (ep, rtt);
			state.Update ((int)rtt.TotalMilliseconds, _timerGranularity);
		}

		public TimeSpan GetRTO (EndPoint ep)
		{
			State state = GetState (ep, InvalidValue);
			if (state == null)
				return _defaultRTO;
			return new TimeSpan (Math.Max (_minRTO, state.RTO) * TimeSpan.TicksPerMillisecond);
		}

		State GetState (EndPoint ep, TimeSpan rtt)
		{
			if (_states == null) {
				lock (this) {
					if (_state == null && !InvalidValue.Equals (rtt))
						_state = new State ((int)rtt.TotalMilliseconds, _timerGranularity);
					return _state;
				}
			}

			IPEndPoint ipep = ep as IPEndPoint;
			if (ipep == null)
				throw new ArgumentException ();

			State state;
			lock (_states) {
				bool success = _states.TryGetValue (ipep.Address, out state);
				if (!success && !InvalidValue.Equals (rtt)) {
					state = new State ((int)rtt.TotalMilliseconds, _timerGranularity);
					_states.Add (ipep.Address, state);
				}
			}
			return state;
		}

		class State
		{
			public int RTO;
			int _srtt, _var;

			public State (int init_rtt, int timerGranularity)
			{
				_srtt = init_rtt << 3;
				_var = init_rtt;
				RTO = init_rtt + Math.Max (timerGranularity, _var);
			}

			public void Update (int rtt, int timerGranularity)
			{
				lock (this) {
					rtt -= (_srtt >> 3);
					_srtt += rtt;
					if (rtt < 0)
						rtt = -rtt;
					rtt -= (_var >> 2);
					_var += rtt;
					RTO = (_srtt >> 3) + Math.Max (timerGranularity, _var);
				}
			}
		}
	}
}
