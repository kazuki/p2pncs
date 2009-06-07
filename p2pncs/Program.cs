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
using XmlConfigLibrary;

namespace p2pncs
{
	class Program : IDisposable
	{
		const string CONFIG_PATH = "p2pncs.xml";
		XmlConfig _config = new XmlConfig ();
		ECKeyPair _imPrivateKey;
		Key _imPublicKey;
		string _name;
		Node _node;
#if DEBUG
		Node[] _nodes = new Node[2];
#endif
		IHttpServer _server;

		static void Main (string[] args)
		{
			using (Program prog = new Program ()) {
				prog.Run ();
			}
		}

		public Program ()
		{
			LoadConfig ();
		}

		void LoadConfig ()
		{
			bool saveFlag = false;
			_config.Define<int> ("net/bind/port", IntParser.Instance, new IntRangeValidator (1, ushort.MaxValue), 65000);
			_config.Define<int> ("gw/bind/port", IntParser.Instance, new IntRangeValidator (1, ushort.MaxValue), 8080);
			_config.Define<byte[]> ("im/private-key", BinaryParser.Instance, null, null);
			_config.Define<string> ("im/name", StringParser.Instance, null, "名無し");
			try {
				if (File.Exists (CONFIG_PATH))
					_config.Load (CONFIG_PATH);
			} catch {
				saveFlag = true;
			}

			byte[] raw = _config.GetValue<byte[]> ("im/private-key");
			if (raw == null || raw.Length == 0) {
				_imPrivateKey = ECKeyPair.Create (Node.DefaultECDomainName);
				_config.SetValue<byte[]> ("im/private-key", _imPrivateKey.PrivateKey, false);
				saveFlag = true;
			} else {
				_imPrivateKey = ECKeyPair.CreatePrivate (Node.DefaultECDomainName, raw);
			}
			_imPublicKey = Key.Create (_imPrivateKey);

			_name = _config.GetValue<string> ("im/name");
			if (_name == null || _name.Length == 0) {
				_name = "名無し";
				saveFlag = true;
			}

			if (saveFlag)
				_config.Save (CONFIG_PATH);
		}

		public void Run ()
		{
			IDatagramEventSocket dgramSock = UdpSocket.CreateIPv4 ();
			dgramSock.Bind (new IPEndPoint (
#if DEBUG
				IPAddress.Loopback
#else
				IPAddress.Any
#endif
				, _config.GetValue<int> ("net/bind/port")));
			_node = new Node (dgramSock);
#if DEBUG
			for (int i = 0; i < _nodes.Length; i ++) {
				dgramSock = UdpSocket.CreateIPv4 ();
				dgramSock.Bind (new IPEndPoint (IPAddress.Loopback, _config.GetValue<int> ("net/bind/port") + i + 1));
				_nodes[i] = new Node (dgramSock);
				ECKeyPair imPrivate = ECKeyPair.Create (Node.DefaultECDomainName);
				Key imPublic = Key.Create (imPrivate);
				_nodes[i].AnonymousRouter.SubscribeRecipient (imPublic, imPrivate);
				_nodes[i].KeyBasedRouter.Join (new IPEndPoint[] {new IPEndPoint (IPAddress.Loopback, _config.GetValue<int> ("net/bind/port"))});
			}
#endif
			p2pncs.Simulation.OSTimerPrecision.SetCurrentThreadToHighPrecision ();
			using (WebApp app = new WebApp (_node, _imPublicKey, _imPrivateKey, _name)) {
				_server = HttpServer.CreateEmbedHttpServer (app, null, true, true, false, _config.GetValue<int> ("gw/bind/port"), 16);
				app.ExitWaitHandle.WaitOne ();
				TestLogger.Dump ();
			}
		}

		public void Dispose ()
		{
			if (_node != null)
				_node.Dispose ();
#if DEBUG
			for (int i = 0; i < _nodes.Length; i ++)
				_nodes[i].Dispose ();
#endif
			if (_server != null)
				_server.Dispose ();
		}
	}
}
