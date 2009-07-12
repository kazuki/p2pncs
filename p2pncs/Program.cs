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

		public static bool LoadConfig (XmlConfig config)
		{
			bool saveFlag = false, exists = false;
			config.Define<int> ("net/bind/port", IntParser.Instance, new IntRangeValidator (1, ushort.MaxValue), 65000);
			config.Define<int> ("gw/bind/port", IntParser.Instance, new IntRangeValidator (1, ushort.MaxValue), 8080);
			try {
				exists = File.Exists (CONFIG_PATH);
				saveFlag = !exists;
				if (exists)
					config.Load (CONFIG_PATH);
			} catch {
				saveFlag = true;
			}

			if (saveFlag)
				config.Save (CONFIG_PATH);
			return exists;
		}

#if !DEBUG
		public void Run ()
		{
			if (!LoadConfig (_config)) {
				Console.WriteLine ("設定ファイルを保存しました。");
				Console.WriteLine ("README.txt を参考に設定ファイルを編集してください。");
				Console.WriteLine ();
				Console.WriteLine ("エンターキーを押すと終了します");
				Console.ReadLine ();
				return;
			}

			IDatagramEventSocket dgramSock = UdpSocket.CreateIPv4 ();
			dgramSock.Bind (new IPEndPoint (IPAddress.Any, _config.GetValue<int> ("net/bind/port")));
			using (Interrupters ints = new Interrupters ())
			using (Node node = new Node (ints, dgramSock, "database.sqlite", _config.GetValue<int> ("net/bind/port")))
			using (WebApp app = new WebApp (node))
			using (HttpServer.CreateEmbedHttpServer (app, null, true, true, false, _config.GetValue<int> ("gw/bind/port"), 16)) {
				Console.WriteLine ("正常に起動しました。");
				Console.WriteLine ("ブラウザで http://127.0.0.1:{0}/ を開いてください。", _config.GetValue<int> ("gw/bind/port"));
				app.ExitWaitHandle.WaitOne ();
				app.CreateStatisticsXML ().Save ("statistics-" + DateTime.Now.ToString ("yyyyMMddHHmmss") + ".xml");
			}
		}
#endif

		public void Dispose ()
		{
		}
	}
}
