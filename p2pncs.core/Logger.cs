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
using p2pncs.Net;
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.Anonymous;
using p2pncs.Net.Overlay.DHT;
using p2pncs.Threading;
using NLogLogger = NLog.Logger;
using LogEventInfo = NLog.LogEventInfo;
using LayoutRenderer = NLog.LayoutRenderer;
using LayoutRendererAttribute = NLog.LayoutRendererAttribute;

namespace p2pncs
{
	public static class Logger
	{
		static Dictionary<LogLevel, NLog.LogLevel> _levelMap = new Dictionary<LogLevel, NLog.LogLevel> ();
		static NLogLogger _logger = NLog.LogManager.GetLogger (string.Empty);
		const int KeyShortLength = 6;

		static Logger ()
		{
			_levelMap.Add (LogLevel.Fatal, NLog.LogLevel.Fatal);
			_levelMap.Add (LogLevel.Error, NLog.LogLevel.Error);
			_levelMap.Add (LogLevel.Warn, NLog.LogLevel.Warn);
			_levelMap.Add (LogLevel.Info, NLog.LogLevel.Info);
			_levelMap.Add (LogLevel.Debug, NLog.LogLevel.Debug);
			_levelMap.Add (LogLevel.Trace, NLog.LogLevel.Trace);
		}

		public static void Log (LogLevel level, object sender, string message, params object[] args)
		{
			string typeName = (sender == null ? "(null)" : sender.GetType ().FullName);
			LogEventInfo info = new LogEventInfo (_levelMap[level], typeName, string.Format (message, args));
			info.Context.Add ("sender", sender);
			_logger.Log (info);
		}

		[LayoutRenderer ("kbr")]
		public class KBRRenderer : LayoutRenderer
		{
			protected override void Append (StringBuilder builder, LogEventInfo logEvent)
			{
				IKeyBasedRouter kbr = logEvent.Context["sender"] as IKeyBasedRouter;
				if (kbr == null) return;
				builder.Append (kbr.SelftNodeId.ToString ().Substring (0, KeyShortLength));
			}

			protected override int GetEstimatedBufferSize (LogEventInfo logEvent)
			{
				return 128;
			}
		}

		[LayoutRenderer ("ar")]
		public class ARRenderer : LayoutRenderer
		{
			protected override void Append (StringBuilder builder, LogEventInfo logEvent)
			{
				IAnonymousRouter ar = logEvent.Context["sender"] as IAnonymousRouter;
				if (ar == null) return;
				builder.Append (ar.KeyBasedRouter.SelftNodeId.ToString ().Substring (0, KeyShortLength));
			}

			protected override int GetEstimatedBufferSize (LogEventInfo logEvent)
			{
				return 128;
			}
		}

		[LayoutRenderer ("ar.subscribe")]
		public class ARSRenderer : LayoutRenderer
		{
			protected override void Append (StringBuilder builder, LogEventInfo logEvent)
			{
				ISubscribeInfo subscribe = logEvent.Context["sender"] as ISubscribeInfo;
				if (subscribe == null) return;
				builder.Append (subscribe.AnonymousRouter.KeyBasedRouter.SelftNodeId.ToString ().Substring (0, KeyShortLength));
				builder.Append ('+');
				builder.Append (subscribe.Key.ToString ().Substring (0, KeyShortLength));
			}

			protected override int GetEstimatedBufferSize (LogEventInfo logEvent)
			{
				return 128;
			}
		}
	}

	public enum LogLevel
	{
		Fatal,
		Error,
		Warn,
		Info,
		Debug,
		Trace
	}
}
