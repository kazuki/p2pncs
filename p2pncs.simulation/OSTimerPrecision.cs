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
using System.Runtime.InteropServices;
using System.Security;

namespace p2pncs.Simulation
{
	public static class OSTimerPrecision
	{
		/*[DllImport ("Avrt.dll"), SuppressUnmanagedCodeSecurity]
		extern static IntPtr AvSetMmThreadCharacteristics (string task, ref int index);

		[DllImport ("Avrt.dll"), SuppressUnmanagedCodeSecurity]
		[return: MarshalAs (UnmanagedType.Bool)]
		extern static bool AvRevertMmThreadCharacteristics (IntPtr handle);

		[ThreadStatic]
		static IntPtr ThreadState;*/

		[DllImport ("winmm.dll")]
		public static extern uint timeBeginPeriod (uint uMilliseconds);

		[DllImport ("winmm.dll")]
		public static extern uint timeEndPeriod (uint uMilliseconds);

		public static void SetCurrentThreadToHighPrecision ()
		{
			try {
				timeBeginPeriod (1);
			} catch {}
			/*int index = 0;
			try {
				ThreadState = AvSetMmThreadCharacteristics ("Simulation", ref index);
			} catch {}*/
		}

		public static void RevertCurrentThreadPrecision ()
		{
			try {
				timeEndPeriod (1);
			} catch {}
			/*if (ThreadState != IntPtr.Zero) {
				try {
					AvRevertMmThreadCharacteristics (ThreadState);
					ThreadState = IntPtr.Zero;
				} catch {}
			}*/
		}
	}
}
