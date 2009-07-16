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
			bool exists = false;
			config.Define<int> (ConfigFields.NetBindUdp, IntParser.Instance, new IntRangeValidator (1, ushort.MaxValue), new Random ().Next (49152, ushort.MaxValue));
			config.Define<int> (ConfigFields.NetBindTcp, IntParser.Instance, new IntRangeValidator (1, ushort.MaxValue), config.GetValue<int> (ConfigFields.NetBindUdp));
			config.Define<int> (ConfigFields.GwBindTcp, IntParser.Instance, new IntRangeValidator (1, ushort.MaxValue), 8080);
			config.Define<bool> (ConfigFields.GwBindAny, BooleanParser.Instance, null, false);
			try {
				exists = File.Exists (CONFIG_PATH);
				if (exists)
					config.Load (CONFIG_PATH);
			} catch {}

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
			dgramSock.Bind (new IPEndPoint (IPAddress.Any, _config.GetValue<int> (ConfigFields.NetBindUdp)));
			using (Interrupters ints = new Interrupters ())
			using (Node node = new Node (ints, dgramSock, "database.sqlite", _config.GetValue<int> (ConfigFields.NetBindUdp)))
			using (WebApp app = new WebApp (node))
			using (HttpServer.CreateEmbedHttpServer (app, null, true, true, _config.GetValue<bool> (ConfigFields.GwBindAny), _config.GetValue<int> (ConfigFields.GwBindTcp), 16)) {
				Console.WriteLine ("正常に起動しました。");
				Console.WriteLine ("ブラウザで http://127.0.0.1:{0}/ を開いてください。", _config.GetValue<int> (ConfigFields.GwBindTcp));
				Console.WriteLine ();
				Console.WriteLine ("注意: このコマンドプロンプトウィンドウは閉じないでください。");
				Console.WriteLine ("プログラムを終了するときは、左側のメニューから[ネットワーク]→[終了]を選ぶか、");
				Console.WriteLine ("http://127.0.0.1:{0}/net/exit を開いて、\"終了する\"ボタンを押してください。", _config.GetValue<int> (ConfigFields.GwBindTcp));
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
