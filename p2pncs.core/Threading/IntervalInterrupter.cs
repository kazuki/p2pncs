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
using System.Threading;

namespace p2pncs.Threading
{
	public class IntervalInterrupter : IDisposable
	{
		TimeSpan _interval;
		Thread _thread;
		bool _active = false, _disposed = false, _loadEqualizing = false;
		string _name;

		List<InterruptHandler> _list = new List<InterruptHandler> ();

		public IntervalInterrupter (TimeSpan interval, string name)
		{
			_interval = interval;
			_name = name;
		}

		public void Start ()
		{
			if (_disposed)
				return;
			if (_thread != null)
				Stop ();

			_active = true;
			_thread = ThreadTracer.CreateThread (Worker, _name);
			_thread.Start ();
		}

		public void AddInterruption (InterruptHandler handler)
		{
			lock (_list) {
				_list.Add (handler);
			}
		}

		public void RemoveInterruption (InterruptHandler handler)
		{
			lock (_list) {
				_list.Remove (handler);
			}
		}

		void Worker ()
		{
			List<InterruptHandler> list = new List<InterruptHandler> ();
			TimeSpan equaWait = TimeSpan.Zero;
			while (_active) {
				DateTime start = DateTime.Now;
				lock (_list) {
					list.AddRange (_list);
				}

				bool equalizingSleep = (_loadEqualizing && equaWait != TimeSpan.Zero);
				for (int i = 0; i < list.Count; i ++) {
					try {
						ThreadTracer.UpdateThreadName (_name + ":" + list[i].Method.ToString ());
						list[i] ();
					} catch {}
					if (equalizingSleep)
						Thread.Sleep (equaWait);
				}
				TimeSpan wait = _interval - (DateTime.Now - start);
				if (_loadEqualizing && list.Count > 0) {
					long temp = wait.Ticks / list.Count;
					if (temp > 0) temp /= 3;
					else if (temp < 0) temp *= 2;
					equaWait = new TimeSpan (equaWait.Ticks + temp);
					if (equaWait < TimeSpan.Zero)
						equaWait = TimeSpan.Zero;
				} else {
					equaWait = TimeSpan.Zero;
				}
				list.Clear ();
				if (wait > TimeSpan.Zero) {
					Thread.Sleep (wait);
				}
			}
		}

		public void Stop ()
		{
			_active = false;
			if (_thread != null) {
				try {
					_thread.Abort ();
				} catch {}
			}
			_thread = null;
		}

		public TimeSpan Interval {
			get { return _interval; }
			set {
				_interval = value;
				if (_active) {
					Stop ();
					Start ();
				}
			}
		}

		public bool Active {
			get { return _active; }
		}

		public bool LoadEqualizing {
			get { return _loadEqualizing; }
			set { _loadEqualizing = value;}
		}

		public void Dispose ()
		{
			_disposed = true;
			_active = false;
			lock (_list) {
				_list.Clear ();
			}
			if (_thread != null) {
				try {
					_thread.Abort ();
				} catch {}
			}
		}
	}
}
