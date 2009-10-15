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
using LogEventInfo = NLog.LogEventInfo;
using NLogLogger = NLog.Logger;

namespace p2pncs
{
	public static class Logger
	{
		static NLog.LogLevel[] _levelMap = new NLog.LogLevel[] {
			NLog.LogLevel.Fatal,
			NLog.LogLevel.Error,
			NLog.LogLevel.Warn,
			NLog.LogLevel.Info,
			NLog.LogLevel.Debug,
			NLog.LogLevel.Trace
		};
		static NLogLogger _logger = NLog.LogManager.GetLogger (string.Empty);

		public static void Log (LogLevel level, object sender, string message, params object[] args)
		{
			string typeName = (sender == null ? "(null)" : sender.GetType ().FullName);
			LogEventInfo info = new LogEventInfo (_levelMap[(int)level], typeName, string.Format (message, args));
			info.Context.Add ("sender", sender);
			_logger.Log (info);
		}
	}

	public enum LogLevel
	{
		Fatal = 0,
		Error,
		Warn,
		Info,
		Debug,
		Trace
	}
}
