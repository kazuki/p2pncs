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

namespace p2pncs.Threading
{
	public class ThreadTraceInfo
	{
		uint _id;
		string _name;
		DateTime _start;
		float _cpuUsage;
		TimeSpan _totalCpuUsageTime;

		public ThreadTraceInfo (uint id, string name, DateTime start, float cpuUsage, TimeSpan totalCpuUsageTime)
		{
			_id = id;
			_name = name;
			_start = start;
			_cpuUsage = cpuUsage;
			_totalCpuUsageTime = totalCpuUsageTime;
		}

		public uint ID {
			get { return _id; }
		}

		public string Name {
			get { return _name; }
		}

		public DateTime StartTime {
			get { return _start; }
		}

		public float CpuUsage {
			get { return _cpuUsage; }
		}

		public TimeSpan TotalCpuUsageTime {
			get { return _totalCpuUsageTime; }
		}
	}
}
