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
using System.Text;

namespace p2pncs
{
	public class StatisticsNoticeEventArgs : EventArgs
	{
		StatisticsNoticeType _type;
		TimeSpan _span;
		int _intValue;

		StatisticsNoticeEventArgs (StatisticsNoticeType type, TimeSpan span, int intValue)
		{
			_type = type;
			_span = span;
			_intValue = intValue;
		}

		public static StatisticsNoticeEventArgs CreateSuccess ()
		{
			return new StatisticsNoticeEventArgs (StatisticsNoticeType.Success, TimeSpan.Zero, 0);
		}

		public static StatisticsNoticeEventArgs CreateFailure ()
		{
			return new StatisticsNoticeEventArgs (StatisticsNoticeType.Failure, TimeSpan.Zero, 0);
		}

		public static StatisticsNoticeEventArgs CreateLifeTime (TimeSpan time)
		{
			return new StatisticsNoticeEventArgs (StatisticsNoticeType.LifeTime, time, 0);
		}

		public static StatisticsNoticeEventArgs CreateRTT (TimeSpan time)
		{
			return new StatisticsNoticeEventArgs (StatisticsNoticeType.RTT, time, 0);
		}

		public static StatisticsNoticeEventArgs CreateHops (int hops)
		{
			return new StatisticsNoticeEventArgs (StatisticsNoticeType.Hops, TimeSpan.Zero, hops);
		}

		public StatisticsNoticeType Type {
			get { return _type; }
		}

		public TimeSpan TimeSpan {
			get { return _span; }
		}

		public int IntValue {
			get { return _intValue; }
		}
	}
}
