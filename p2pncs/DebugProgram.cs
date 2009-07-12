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

#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using Kazuki.Net.HttpServer;
using openCrypto.EllipticCurve;
using p2pncs.Net;
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.DFS.MMLC;
using p2pncs.Security.Captcha;
using p2pncs.Security.Cryptography;
using p2pncs.Simulation;
using p2pncs.Simulation.VirtualNet;
using p2pncs.Threading;
using XmlConfigLibrary;

namespace p2pncs
{
	class DebugProgram : IDisposable
	{
		const int NODES = 100;
		RandomIPAddressGenerator _ipgen = new RandomIPAddressGenerator ();
		VirtualNetwork _network;
		Interrupters _ints;
		IntervalInterrupter _churnInt;
		XmlConfig _config = new XmlConfig ();
		List<DebugNode> _list = new List<DebugNode> ();
		List<IPEndPoint> _eps = new List<IPEndPoint> ();
		IPEndPoint[] _init_nodes;
		int _nodeIdx = -1;
		Random _rnd = new Random ();

		public DebugProgram ()
		{
			_ints = new Interrupters ();
			Program.LoadConfig (_config);
			_churnInt = new IntervalInterrupter (TimeSpan.FromSeconds (500.0 / NODES), "Churn Timer");
			Directory.CreateDirectory ("db");
		}

		public void Run ()
		{
			int base_port = _config.GetValue<int> ("gw/bind/port");

			_network = new VirtualNetwork (LatencyTypes.Constant (20), 5, PacketLossType.Constant (0.05), Environment.ProcessorCount);

			for (int i = 0; i < NODES; i++) {
				AddNode (base_port);
				if (i == 10) base_port = -1;
				Thread.Sleep (100);
			}
			Console.WriteLine ("{0} Nodes Inserted", NODES);

			_churnInt.AddInterruption (delegate () {
				lock (_list) {
					if (_list.Count <= 10)
						return;
					int idx = _rnd.Next (10, _list.Count);
					DebugNode removed = _list[idx];
					try {
						removed.Dispose ();
					} catch {}
					_list.RemoveAt (idx);
					_eps.RemoveAt (idx);
					AddNode (-1);
				}
			});
			_churnInt.Start ();

			_list[0].WaitOne ();
		}

		DebugNode AddNode (int base_port)
		{
			IPAddress adrs = _ipgen.Next ();
			VirtualDatagramEventSocket sock = new VirtualDatagramEventSocket (_network, adrs);
			IPEndPoint pubEP = new IPEndPoint (adrs, 10000);
			sock.Bind (new IPEndPoint (IPAddress.Any, pubEP.Port));
			DebugNode node = new DebugNode (Interlocked.Increment (ref _nodeIdx));
			node.Prepare (_ints, sock, base_port, pubEP);
			lock (_list) {
				_list.Add (node);
				_eps.Add (pubEP);
			}
			if (_init_nodes == null) {
				node.Node.PortOpenChecker.Join (_eps.ToArray ());
				_eps.Add (pubEP);
				if (_eps.Count == 4)
					_init_nodes = _eps.ToArray ();
			} else {
				node.Node.PortOpenChecker.Join (_init_nodes);
			}
			return node;
		}

		public void Dispose ()
		{
			_churnInt.Dispose ();
			_ints.Dispose ();
			_network.Close ();
			lock (_list) {
				for (int i = 0; i < _list.Count; i++) {
					try {
						_list[i].Dispose ();
					} catch {}
				}
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
				_imPrivateKey = ECKeyPair.Create (DefaultAlgorithm.ECDomainName);
				_imPublicKey = Key.Create (_imPrivateKey);
				_name = "Node-" + idx.ToString ("x");
			}

			public void Prepare (Interrupters ints, IDatagramEventSocket bindedDgramSock, int base_port, IPEndPoint ep)
			{
				_name = _name + " (" + ep.ToString () + ")";
				_node = new Node (ints, bindedDgramSock, string.Format ("db{0}{1}.sqlite", Path.DirectorySeparatorChar, _idx), ep.Port);
				_app = new WebApp (_node);
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
#endif
