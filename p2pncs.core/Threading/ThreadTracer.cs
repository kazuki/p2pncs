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
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;

namespace p2pncs.Threading
{
	public static class ThreadTracer
	{
		[DllImport ("kernel32.dll")]
		static extern uint GetCurrentThreadId ();

		static bool _supported = false;
		static Dictionary<uint, ThreadInfo> _threads = null;
		static ReaderWriterLockWrapper _lock = new ReaderWriterLockWrapper ();

		static ThreadTracer ()
		{
			try {
				GetCurrentThreadId ();
				_supported = true;
			} catch {
				_supported = false;
				return;
			}
			_threads = new Dictionary<uint, ThreadInfo> ();
		}

		public static Thread CreateThread (ThreadStart start, string name)
		{
			if (_supported) {
				return new ThreadInfo (start, name).Thread;
			} else {
				Thread thrd = new Thread (start);
				thrd.Name = name;
				return thrd;
			}
		}

		public static Thread CreateThread (ParameterizedThreadStart start, string name)
		{
			if (_supported) {
				return new ThreadInfo (start, name).Thread;
			} else {
				Thread thrd = new Thread (start);
				thrd.Name = name;
				return thrd;
			}
		}

		static void Register (ThreadInfo ti)
		{
			using (_lock.EnterWriteLock ()) {
				_threads[ti.NativeThreadId] = ti;
			}
		}

		public static void UpdateThreadName (string new_name)
		{
			if (!_supported)
				return;

			uint tid = GetCurrentThreadId ();
			ThreadInfo ti;
			using (_lock.EnterReadLock ()) {
				if (!_threads.TryGetValue (tid, out ti))
					return;
				ti.Name = new_name;
			}
		}

		public static ThreadTraceInfo[] GetThreadInfo ()
		{
			if (!_supported)
				return new ThreadTraceInfo[0];

			List<ThreadTraceInfo> list = new List<ThreadTraceInfo> ();
			using (_lock.EnterUpgradeableReadLock ()) {
				HashSet<uint> threadIds = new HashSet<uint> (_threads.Keys);
				foreach (ProcessThread pt in Process.GetCurrentProcess().Threads) {
					ThreadInfo ti;
					if (!_threads.TryGetValue ((uint)pt.Id, out ti))
						continue;

					DateTime lastCheck = DateTime.Now;
					TimeSpan lastCheckCpu = pt.TotalProcessorTime;
					TimeSpan deltaTime = lastCheck.Subtract (ti.LastCheckTime);
					TimeSpan deltaCpuTime = lastCheckCpu - ti.LastCheckTotalCpuTime;
					ti.LastCheckTime = lastCheck;
					ti.LastCheckTotalCpuTime = lastCheckCpu;
					if (deltaTime.TotalMilliseconds < 1.0)
						deltaTime = TimeSpan.FromSeconds (1);
					list.Add (new ThreadTraceInfo (ti.ID, ti.Name, ti.StartDateTime,
						(float)(deltaCpuTime.TotalMilliseconds / deltaTime.TotalMilliseconds), lastCheckCpu));
					threadIds.Remove ((uint)pt.Id);
				}
				if (threadIds.Count > 0) {
					using (_lock.EnterWriteLock ()) {
						foreach (uint id in threadIds)
							_threads.Remove (id);
					}
				}
			}
			return list.ToArray ();
		}

		class ThreadInfo
		{
			Thread _thrd = null;
			ThreadStart _start1 = null;
			ParameterizedThreadStart _start2;
			string _name;
			uint _id;
			DateTime _startDT;

			ThreadInfo (string name)
			{
				_name = name;
			}

			public ThreadInfo (ThreadStart start, string name) : this (name)
			{
				_thrd = new Thread (Start1);
				_thrd.Name = name;
				_start1 = start;
			}

			public ThreadInfo (ParameterizedThreadStart start, string name) : this (name)
			{
				_thrd = new Thread (Start2);
				_thrd.Name = name;
				_start2 = start;
			}

			void Start ()
			{
				_startDT = DateTime.Now;
				LastCheckTime = _startDT;
				LastCheckTotalCpuTime = TimeSpan.Zero;
				_id = GetCurrentThreadId ();
				ThreadTracer.Register (this);
			}

			void Start1 ()
			{
				Start ();
				_start1 ();
			}

			void Start2 (object obj)
			{
				Start ();
				_start2 (obj);
			}

			public uint ID {
				get { return _id; }
			}

			public Thread Thread {
				get { return _thrd; }
			}

			public uint NativeThreadId {
				get { return _id; }
			}

			public string Name {
				get { return _name; }
				set { _name = value; }
			}

			public DateTime StartDateTime {
				get { return _startDT; }
			}

			public DateTime LastCheckTime { get; set; }
			public TimeSpan LastCheckTotalCpuTime { get; set; }
		}
	}
}
