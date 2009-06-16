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
		ECKeyPair _imPrivateKey;
		Key _imPublicKey;
		string _name;
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
			LoadConfig (_config, out _imPrivateKey, out _imPublicKey, out _name);
		}
#endif

		public static void LoadConfig (XmlConfig config, out ECKeyPair imPrivateKey, out Key imPublicKey, out string name)
		{
			bool saveFlag = false;
			config.Define<int> ("net/bind/port", IntParser.Instance, new IntRangeValidator (1, ushort.MaxValue), 65000);
			config.Define<int> ("gw/bind/port", IntParser.Instance, new IntRangeValidator (1, ushort.MaxValue), 8080);
			config.Define<byte[]> ("im/private-key", BinaryParser.Instance, null, null);
			config.Define<string> ("im/name", StringParser.Instance, null, "名無し");
			try {
				if (File.Exists (CONFIG_PATH))
					config.Load (CONFIG_PATH);
			} catch {
				saveFlag = true;
			}

			byte[] raw = config.GetValue<byte[]> ("im/private-key");
			if (raw == null || raw.Length == 0) {
				imPrivateKey = ECKeyPair.Create (DefaultAlgorithm.ECDomainName);
				config.SetValue<byte[]> ("im/private-key", imPrivateKey.PrivateKey, false);
				saveFlag = true;
			} else {
				imPrivateKey = ECKeyPair.CreatePrivate (DefaultAlgorithm.ECDomainName, raw);
			}
			imPublicKey = Key.Create (imPrivateKey);

			name = config.GetValue<string> ("im/name");
			if (name == null || name.Length == 0) {
				name = "名無し";
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
			using (Node node = new Node (ints, dgramSock))
			using (WebApp app = new WebApp (node, _imPublicKey, _imPrivateKey, _name, ints))
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
