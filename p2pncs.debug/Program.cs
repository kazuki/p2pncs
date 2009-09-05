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
using System.IO;
using System.Net;
using System.Threading;
using Kazuki.Net.HttpServer;
using Kazuki.Net.HttpServer.Middlewares;
using openCrypto.EllipticCurve;
using p2pncs.Net;
using p2pncs.Net.Overlay;
using p2pncs.Security.Cryptography;
using p2pncs.Simulation.VirtualNet;
using p2pncs.Threading;

namespace p2pncs.debug
{
	class Program : IDisposable
	{
		const int NODES = 10;
		VirtualNetwork _network;
		Interrupters _ints;
		IntervalInterrupter _churnInt;
		List<DebugNode> _list = new List<DebugNode> ();
		List<IPEndPoint> _eps = new List<IPEndPoint> ();
		IPEndPoint[] _init_nodes;
		int _nodeIdx = -1;
		Random _rnd = new Random ();

		static void Main ()
		{
			p2pncs.Simulation.OSTimerPrecision.SetCurrentThreadToHighPrecision ();
			using (Program prog = new Program ()) {
				prog.Run ();
			}
		}

		public Program ()
		{
			_ints = new Interrupters ();
			_churnInt = new IntervalInterrupter (TimeSpan.FromSeconds (500.0 / NODES), "Churn Timer");
			Directory.CreateDirectory ("db");
		}

		public void Run ()
		{
			int gw_port = 8080;

			_network = new VirtualNetwork (LatencyTypes.Constant (20), 5, PacketLossType.Constant (0.05), Environment.ProcessorCount);

			int step = Math.Max (1, NODES / 10);
			for (int i = 0; i < NODES; i++) {
				AddNode ((i % step) == 0 ? gw_port++ : -1);
				Thread.Sleep (100);
			}
			Console.WriteLine ("{0} Nodes Inserted", NODES);

			_churnInt.AddInterruption (delegate () {
				lock (_list) {
					if (_list.Count <= 10)
						return;
					int idx;
					DebugNode removed;
					while (true) {
						idx = _rnd.Next (0, _list.Count);
						removed = _list[idx];
						if (!removed.IsGateway)
							break;
					}
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

		DebugNode AddNode (int gw_port)
		{
			int nodeIdx = Interlocked.Increment (ref _nodeIdx);
			IPAddress adrs = IPAddress.Loopback;
			VirtualDatagramEventSocket sock = new VirtualDatagramEventSocket (_network, adrs);
			IPEndPoint pubEP = new IPEndPoint (adrs, 1 + nodeIdx);
			IPEndPoint bindTcpEP = new IPEndPoint (IPAddress.Loopback, 30000 + nodeIdx);
			sock.Bind (new IPEndPoint (IPAddress.Any, pubEP.Port));
			DebugNode node = DebugNode.Create (nodeIdx, _ints, sock, bindTcpEP, pubEP, gw_port);
			lock (_list) {
				_list.Add (node);
				_eps.Add (pubEP);
			}
			if (_init_nodes == null) {
				node.PortOpenChecker.Join (_eps.ToArray ());
				_eps.Add (pubEP);
				if (_eps.Count == 4)
					_init_nodes = _eps.ToArray ();
			} else {
				node.PortOpenChecker.Join (_init_nodes);
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

		class DebugNode : Node
		{
			ECKeyPair _imPrivateKey;
			Key _imPublicKey;
			string _name;
			IHttpServer _server = null;
			WebApp _app;
			SessionMiddleware _sessionMiddleware;
			int _idx;
			bool _is_gw;
			IPEndPoint _bindTcpEP;

			public static DebugNode Create (int idx, Interrupters ints, IDatagramEventSocket bindedDgramSock, IPEndPoint bindTcpEp, IPEndPoint bindUdpEp, int gw_port)
			{
				string db_path = string.Format ("db{0}{1}.sqlite", Path.DirectorySeparatorChar, idx);
				ITcpListener listener = new p2pncs.Net.TcpListener ();
				listener.Bind (bindTcpEp);
				listener.ListenStart ();
				return new DebugNode (idx, ints, listener, bindedDgramSock, bindTcpEp, bindUdpEp, gw_port, db_path);
			}

			DebugNode (int idx, Interrupters ints, ITcpListener listener, IDatagramEventSocket bindedDgramSock, IPEndPoint bindTcpEp, IPEndPoint bindUdpEp, int gw_port, string dbpath)
				: base (ints, bindedDgramSock, listener, dbpath, (ushort)bindUdpEp.Port, (ushort)bindTcpEp.Port)
			{
				_idx = idx;
				_bindTcpEP = bindTcpEp;
				_imPrivateKey = ECKeyPair.Create (DefaultAlgorithm.ECDomainName);
				_imPublicKey = Key.Create (_imPrivateKey);
				_name = "Node-" + idx.ToString ("x");
				_app = new WebApp (this, ints);
				_is_gw = gw_port > 0;
				if (_is_gw) {
					_sessionMiddleware = new SessionMiddleware (MMLC.CreateDBConnection, _app);
					_server = HttpServer.CreateEmbedHttpServer (_sessionMiddleware, null, true, true, false, gw_port, 16);
				}
			}

			public void WaitOne ()
			{
				_app.ExitWaitHandle.WaitOne ();
			}

			public override IPAddress GetCurrentPublicIPAddress ()
			{
				if (_dgramSock is VirtualDatagramEventSocket)
					return (_dgramSock as VirtualDatagramEventSocket).PublicIPAddress;
				return base.GetCurrentPublicIPAddress ();
			}

			public bool IsGateway {
				get { return _is_gw; }
			}

			public override void Dispose ()
			{
				lock (this) {
					base.Dispose ();
					if (_server != null) _server.Dispose ();
					if (_app != null) _app.Dispose ();
					if (_sessionMiddleware != null) _sessionMiddleware.Dispose ();
					_server = null;
					_app = null;
					_sessionMiddleware = null;
				}
			}
		}
	}
}
