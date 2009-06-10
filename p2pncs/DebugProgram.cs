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
using System.Net;
using Kazuki.Net.HttpServer;
using openCrypto.EllipticCurve;
using p2pncs.Net;
using p2pncs.Net.Overlay;
using p2pncs.Simulation;
using p2pncs.Simulation.VirtualNet;
using XmlConfigLibrary;

namespace p2pncs
{
	class DebugProgram : IDisposable
	{
		const int NODES = 100;
		RandomIPAddressGenerator _ipgen = new RandomIPAddressGenerator ();
		VirtualNetwork _network;
		Interrupters _ints;
		XmlConfig _config = new XmlConfig ();
		List<DebugNode> _list = new List<DebugNode> ();

		public DebugProgram ()
		{
			_ints = new Interrupters ();
			{
				ECKeyPair tmpKey;
				Key tmpPubKey;
				string tmpName;
				Program.LoadConfig (_config, out tmpKey, out tmpPubKey, out tmpName);
			}
		}

		public void Run ()
		{
			int base_port = _config.GetValue<int> ("gw/bind/port");

			_network = new VirtualNetwork (LatencyTypes.Constant (40), 5, PacketLossType.Constant (0.05), 2);
			IPEndPoint[] eps = null;
			List<IPEndPoint> ep_list = new List<IPEndPoint> (4);
			for (int i = 0; i < NODES; i++) {
				IPAddress adrs = _ipgen.Next ();
				VirtualDatagramEventSocket sock = new VirtualDatagramEventSocket (_network, adrs);
				IPEndPoint pubEP = new IPEndPoint (adrs, 10000);
				sock.Bind (new IPEndPoint (IPAddress.Any, pubEP.Port));
				DebugNode node = new DebugNode (i);
				if (i == 10) base_port = -1;
				ep_list.Add (pubEP);
				node.Prepare (_ints, sock, base_port, pubEP);
				lock (_list) {
					_list.Add (node);
				}
				if (eps == null) {
					node.Node.KeyBasedRouter.Join (ep_list.ToArray ());
					ep_list.Add (pubEP);
					if (ep_list.Count == ep_list.Capacity)
						eps = ep_list.ToArray ();
				} else {
					node.Node.KeyBasedRouter.Join (eps);
				}
			}

			_list[0].WaitOne ();
		}

		public void Dispose ()
		{
			_ints.Dispose ();
			_network.Close ();
			for (int i = 0; i < _list.Count; i++) {
				try {
					_list[i].Dispose ();
				} catch {}
			}
		}

		class DebugNode : IDisposable
		{
			ECKeyPair _imPrivateKey;
			Key _imPublicKey;
			string _name;
			IHttpServer _server = null;
			WebApp _app;
			Node _node;
			int _idx;

			public DebugNode (int idx)
			{
				_idx = idx;
				_imPrivateKey = ECKeyPair.Create (Node.DefaultECDomainName);
				_imPublicKey = Key.Create (_imPrivateKey);
				_name = "Node-" + idx.ToString ("x");
			}

			public void Prepare (Interrupters ints, IDatagramEventSocket bindedDgramSock, int base_port, IPEndPoint ep)
			{
				_name = _name + " (" + ep.ToString () + ")";
				_node = new Node (ints, bindedDgramSock);
				_app = new WebApp (_node, _imPublicKey, _imPrivateKey, _name, ints);
				if (base_port >= 0)
					_server = HttpServer.CreateEmbedHttpServer (_app, null, true, true, false, base_port + _idx, 16);
			}

			public void WaitOne ()
			{
				_app.ExitWaitHandle.WaitOne ();
			}

			public Node Node {
				get { return _node; }
			}

			public void Dispose ()
			{
				lock (this) {
					if (_server != null) _server.Dispose ();
					if (_app != null) _app.Dispose ();
					if (_node != null) _node.Dispose ();
					_server = null;
					_app = null;
					_node = null;
				}
			}
		}
	}
}
