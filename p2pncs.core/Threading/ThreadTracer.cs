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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ThreadState = System.Diagnostics.ThreadState;

namespace p2pncs.Threading
{
	public static class ThreadTracer
	{
		static IThreadManager _threadMgr = null;
		static Dictionary<int, ThreadInfo> _threads = null;
		static ReaderWriterLockWrapper _lock = new ReaderWriterLockWrapper ();

		static ThreadTracer ()
		{
			try {
				_threadMgr = new ManagedThread ();
			} catch {
				try {
					_threadMgr = new LinuxThread ();
				} catch {
					_threadMgr = null;
					return;
				}
			}
			_threads = new Dictionary<int, ThreadInfo> ();
		}

		public static Thread CreateThread (ThreadStart start, string name)
		{
			if (_threadMgr != null) {
				return new ThreadInfo (start, name).Thread;
			} else {
				Thread thrd = new Thread (start);
				thrd.Name = name;
				return thrd;
			}
		}

		public static Thread CreateThread (ParameterizedThreadStart start, string name)
		{
			if (_threadMgr != null) {
				return new ThreadInfo (start, name).Thread;
			} else {
				Thread thrd = new Thread (start);
				thrd.Name = name;
				return thrd;
			}
		}

		public static void QueueToThreadPool (WaitCallback callback, string name)
		{
			QueueToThreadPool (callback, null, name);
		}

		public static void QueueToThreadPool (WaitCallback callback, object state, string name)
		{
			if (_threadMgr != null) {
				new ThreadInfo (callback, state, name);
			} else {
				ThreadPool.QueueUserWorkItem (callback, state);
			}
		}

		static void Register (ThreadInfo ti)
		{
			using (_lock.EnterWriteLock ()) {
				_threads[ti.ID] = ti;
			}
		}

		static void Unregister (ThreadInfo ti)
		{
			using (_lock.EnterWriteLock ()) {
				_threads.Remove (ti.ID);
			}
		}

		public static void UpdateThreadName (string new_name)
		{
			if (_threadMgr == null)
				return;

			int tid = _threadMgr.GetCurrentThreadId ();
			ThreadInfo ti;
			using (_lock.EnterReadLock ()) {
				if (!_threads.TryGetValue (tid, out ti))
					return;
				ti.Name = new_name;
			}
		}

		public static void AppendThreadName (string name)
		{
			if (_threadMgr == null)
				return;

			int tid = _threadMgr.GetCurrentThreadId ();
			ThreadInfo ti;
			using (_lock.EnterReadLock ()) {
				if (!_threads.TryGetValue (tid, out ti))
					return;
				ti.Name += name;
			}
		}

		public static ThreadTraceInfo[] GetThreadInfo ()
		{
			if (_threadMgr == null)
				return new ThreadTraceInfo[0];

			List<ThreadTraceInfo> list = new List<ThreadTraceInfo> ();
			using (_lock.EnterUpgradeableReadLock ()) {
				HashSet<int> threadIds = new HashSet<int> (_threads.Keys);
				ProcessThreadInfo[] threads = _threadMgr.GetThreads ();
				for (int i = 0; i < threads.Length; i ++) {
					ProcessThreadInfo pt = threads[i];
					ThreadInfo ti;
					if (!_threads.TryGetValue (pt.ID, out ti))
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
						(float)(deltaCpuTime.TotalMilliseconds / deltaTime.TotalMilliseconds),
						lastCheckCpu, pt.State));
					threadIds.Remove (pt.ID);
				}
				if (threadIds.Count > 0) {
					using (_lock.EnterWriteLock ()) {
						foreach (int id in threadIds)
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
			ParameterizedThreadStart _start2 = null;
			WaitCallback _start3 = null;
			string _name;
			int _id;
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

			public ThreadInfo (WaitCallback callback, object state, string name) : this (name)
			{
				_start3 = callback;
				ThreadPool.QueueUserWorkItem (Start3, state);
			}

			void Start ()
			{
				_startDT = DateTime.Now;
				LastCheckTime = _startDT;
				LastCheckTotalCpuTime = TimeSpan.Zero;
				_id = ThreadTracer._threadMgr.GetCurrentThreadId ();
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

			void Start3 (object state)
			{
				Start ();
				try {
					_start3 (state);
				} finally {
					ThreadTracer.Unregister (this);
				}
			}

			public int ID {
				get { return _id; }
			}

			public Thread Thread {
				get { return _thrd; }
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

		public interface IThreadManager
		{
			ProcessThreadInfo[] GetThreads ();

			int GetCurrentThreadId ();
		}

		public class ProcessThreadInfo
		{
			int _id;
			TimeSpan _totalProcessorTime;
			ThreadState _state;

			public ProcessThreadInfo (int id, TimeSpan totalProcessorTime, ThreadState state)
			{
				_id = id;
				_totalProcessorTime = totalProcessorTime;
				_state = state;
			}

			public int ID {
				get { return _id; }
			}

			public TimeSpan TotalProcessorTime {
				get { return _totalProcessorTime; }
			}

			public ThreadState State {
				get { return _state; }
			}
		}

		public class ManagedThread : IThreadManager
		{
			[DllImport ("kernel32.dll", EntryPoint = "GetCurrentThreadId")]
			static extern int GetCurrentThreadId_Internal ();

			public ManagedThread ()
			{
				if (Process.GetCurrentProcess ().Threads.Count == 0)
					throw new NotSupportedException ();
			}

			public ProcessThreadInfo[] GetThreads ()
			{
				Process proc = Process.GetCurrentProcess ();
				List<ProcessThreadInfo> list = new List<ProcessThreadInfo> (proc.Threads.Count);
				foreach (ProcessThread pt in proc.Threads) {
					list.Add (new ProcessThreadInfo (pt.Id, pt.TotalProcessorTime, pt.ThreadState));
				}
				return list.ToArray ();
			}

			public int GetCurrentThreadId ()
			{
				return GetCurrentThreadId_Internal ();
			}
		}

		public class LinuxThread : IThreadManager
		{
			[DllImport ("libc")]
			public static extern int syscall (int number);

			[DllImport ("libc")]
			public static extern int getpid ();

			[DllImport ("libc")]
			public static extern int sysconf (int name);

			const int SYSCONF_CLK_TCK = 2;
			const int SYSCALL_GETTID = 224;

			string _baseDir;
			double _clockTick;

			public LinuxThread ()
			{
				_baseDir = "/proc/" + getpid ().ToString () + "/task/";
				if (!Directory.Exists (_baseDir))
					throw new NotSupportedException ();
				_clockTick = sysconf (SYSCONF_CLK_TCK);
			}

			public ProcessThreadInfo[] GetThreads ()
			{
				string[] tasks = Directory.GetDirectories (_baseDir);
				List<ProcessThreadInfo> list = new List<ProcessThreadInfo> (tasks.Length);
				byte[] buffer = new byte[2048];
				StringBuilder sb = new StringBuilder ();
				for (int i = 0; i < tasks.Length; i++) {
					try {
						sb.Remove (0, sb.Length);
						using (FileStream fs = new FileStream (tasks[i] + "/stat", FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
							while (true) {
								int ret = fs.Read (buffer, 0, buffer.Length);
								if (ret <= 0) break;
								sb.Append (Encoding.ASCII.GetString (buffer, 0, ret));
							}
						}
						string[] items = sb.ToString ().Split (' ');
						int id = int.Parse (items[0]);
						long utime = long.Parse (items[12]);
						long stime = long.Parse (items[13]);
						ThreadState state;
						switch (items[2][0]) {
							case 'R': state = ThreadState.Running; break;
							case 'S':
							case 'D':
							case 'T': state = ThreadState.Wait; break;
							case 'W': state = ThreadState.Transition; break;
							case 'Z': state = ThreadState.Terminated; break;
							default:  state = ThreadState.Unknown; break;
						}
						list.Add (new ProcessThreadInfo (id, TimeSpan.FromSeconds ((utime + stime) / _clockTick), state));
					} catch {}
				}
				return list.ToArray ();
			}

			public int GetCurrentThreadId ()
			{
				return syscall (SYSCALL_GETTID);
			}
		}
	}
}
