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
using System.IO;
using System.Net;
using Kazuki.Net.HttpServer;
using openCrypto.EllipticCurve;
using p2pncs.Net;
using p2pncs.Net.Overlay;
using p2pncs.Security.Cryptography;
using XmlConfigLibrary;

namespace p2pncs
{
	class Program : IDisposable
	{
		const string CONFIG_PATH = "p2pncs.xml";
#if !DEBUG
		XmlConfig _config = new XmlConfig ();
#endif

		static void Main (string[] args)
		{
#if DEBUG
			using (DebugProgram prog = new DebugProgram ()) {
				prog.Run ();
			}
#else
			using (Program prog = new Program ()) {
				prog.Run ();
			}
#endif
		}

#if !DEBUG
		public Program ()
		{
			LoadConfig (_config);
		}
#endif

		public static void LoadConfig (XmlConfig config)
		{
			bool saveFlag = false;
			config.Define<int> ("net/bind/port", IntParser.Instance, new IntRangeValidator (1, ushort.MaxValue), 65000);
			config.Define<int> ("gw/bind/port", IntParser.Instance, new IntRangeValidator (1, ushort.MaxValue), 8080);
			try {
				saveFlag = !File.Exists (CONFIG_PATH);
				if (!saveFlag)
					config.Load (CONFIG_PATH);
			} catch {
				saveFlag = true;
			}

			if (saveFlag)
				config.Save (CONFIG_PATH);
		}

#if !DEBUG
		public void Run ()
		{
			IDatagramEventSocket dgramSock = UdpSocket.CreateIPv4 ();
			dgramSock.Bind (new IPEndPoint (IPAddress.Any, _config.GetValue<int> ("net/bind/port")));
			using (Interrupters ints = new Interrupters ())
			using (Node node = new Node (ints, dgramSock, "database.sqlite", _config.GetValue<int> ("net/bind/port")))
			using (WebApp app = new WebApp (node))
			using (HttpServer.CreateEmbedHttpServer (app, null, true, true, false, _config.GetValue<int> ("gw/bind/port"), 16)) {
				app.ExitWaitHandle.WaitOne ();
				TestLogger.Dump ();
			}
		}
#endif

		public void Dispose ()
		{
		}
	}
}
