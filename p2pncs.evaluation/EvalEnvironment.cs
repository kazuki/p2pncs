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
using System.Threading;
using p2pncs.Net;
using p2pncs.Net.Overlay.Anonymous;
using p2pncs.Security.Cryptography;
using p2pncs.Simulation.VirtualNet;
using p2pncs.Threading;

namespace p2pncs.Evaluation
{
	class EvalEnvironment : IDisposable
	{
		VirtualNetwork _network;
		List<VirtualNode> _nodes;
		IntervalInterrupter _msgInt1, _msgInt2, _anonInt, _kbrInt, _dhtInt, _churnInt = null;
		Random _rnd = new Random ();
		EvalOptionSet _opt;

		public EvalEnvironment (EvalOptionSet opt)
		{
			_opt = opt;
			_network = new VirtualNetwork (opt.GetLatency (), 5, opt.GetPacketLossRate (), Environment.ProcessorCount);
			_nodes = new List<VirtualNode> ();
			_msgInt1 = new IntervalInterrupter (TimeSpan.FromMilliseconds (50), "MessagingSocket Interrupter");
			_msgInt2 = new IntervalInterrupter (TimeSpan.FromMilliseconds (50), "AnonymousMessagingSocket Interrupter");
			_anonInt = new IntervalInterrupter (TimeSpan.FromMilliseconds (50), "Anonymous Interrupter");
			_kbrInt = new IntervalInterrupter (TimeSpan.FromSeconds (10), "KBR Stabilize Interrupter");
			_dhtInt = new IntervalInterrupter (TimeSpan.FromSeconds (10), "DHT Maintenance Interrupter");
			_kbrInt.LoadEqualizing = true;
			_dhtInt.LoadEqualizing = true;
			_msgInt1.Start ();
			_msgInt2.Start ();
			_anonInt.Start ();
			_kbrInt.Start ();
			if (opt.ChurnInterval > 0)
				_churnInt = new IntervalInterrupter (TimeSpan.FromMilliseconds (opt.ChurnInterval), "Churn Interrupter");
		}

		public void AddNodes (int count, bool viewStatusToConsole)
		{
			int initEndPoints = 2;
			List<EndPoint> endPoints = new List<EndPoint> (initEndPoints);
			EndPoint[] eps = null;
			int px = 0, py = 0;
			if (viewStatusToConsole) {
				Console.Write ("Add Nodes: ");
				px = Console.CursorLeft;
				py = Console.CursorTop;
			}
			for (int i = 0; i < count; i++) {
				if (viewStatusToConsole && (i % 10 == 0)) {
					Console.CursorLeft = px; Console.CursorTop = py;
					Console.Write ("[{0:p0}]", i / (double)count);
				}
				VirtualNode node = CreateNewVirtualNode ();
				lock (_nodes) {
					_nodes.Add (node);
				}
				if (i != 0) {
					if (eps == null || eps.Length < initEndPoints)
						eps = endPoints.ToArray ();
					node.KeyBasedRouter.Join (eps);
				}
				endPoints.Add (node.PublicEndPoint);
				Thread.Sleep (5);
			}
			if (viewStatusToConsole) {
				Console.CursorLeft = px; Console.CursorTop = py;
				Console.Write ("[stabilizing]");
			}
			for (int i = 0; i < _opt.NumberOfNodes; i++) {
				_nodes[i].KeyBasedRouter.RoutingAlgorithm.Stabilize ();
				Thread.Sleep (5);
			}
			if (viewStatusToConsole) {
				Console.CursorLeft = px; Console.CursorTop = py;
				Console.Write ("[waiting]    ");
			}
			Thread.Sleep (count * 5);
			if (viewStatusToConsole) {
				Console.CursorLeft = px; Console.CursorTop = py;
				Console.WriteLine ("[ok]         ");
			}
		}

		public void StartChurn ()
		{
			StartChurn (delegate () {
				VirtualNode node = CreateNewVirtualNode ();
				lock (_nodes) {
					int idx = _rnd.Next (2, _nodes.Count);
					Logger.Log (LogLevel.Trace, this, "Drop-out Node {0}, {1}", _nodes[idx].NodeID, _nodes[idx].PublicEndPoint);
					_nodes[idx].Dispose ();
					_nodes.RemoveAt (idx);
					_nodes.Add (node);
				}
				node.KeyBasedRouter.Join (new EndPoint[] {_nodes[0].PublicEndPoint});
			});
		}

		public void StartChurn (InterruptHandler handler)
		{
			if (_churnInt == null || _churnInt.Active)
				return;
			_churnInt.AddInterruption (handler);
		}

		public VirtualNode CreateNewVirtualNode ()
		{
			return new VirtualNode (this, _network, _opt, _msgInt1, _kbrInt, _anonInt, _dhtInt);
		}

		public IMessagingSocket CreateMessagingSocket (IDatagramEventSocket sock)
		{
			TimeSpan timeout = TimeSpan.FromSeconds (2);
			int retries = 3, retryBufferSize = 1024, dupCheckSize = 512;
			return CreateMessagingSocket (sock, timeout, retries, retryBufferSize, dupCheckSize);
		}

		public IMessagingSocket CreateMessagingSocket (IDatagramEventSocket sock, TimeSpan timeout, int retries, int retryBufferSize, int dupCheckSize)
		{
			VirtualDatagramEventSocket vsock = sock as VirtualDatagramEventSocket;
			if (_opt.BypassMessagingSerializer && vsock != null)
				return new VirtualMessagingSocket (vsock, true, _msgInt2, timeout, retries, retryBufferSize, dupCheckSize);
			else
				return new MessagingSocket (sock, true, SymmetricKey.NoneKey, Serializer.Instance, null, _msgInt2, timeout, retries, retryBufferSize, dupCheckSize);
		}

		public VirtualNetwork Network {
			get { return _network; }
		}

		public List<VirtualNode> Nodes {
			get { return _nodes; }
		}

		void IDisposable.Dispose ()
		{
			_msgInt1.Dispose ();
			_msgInt2.Dispose ();
			_kbrInt.Dispose ();
			_anonInt.Dispose ();
			if (_churnInt != null)
				_churnInt.Dispose ();
			_dhtInt.Dispose ();
			_network.Close ();
			for (int i = 0; i < _nodes.Count; i++)
				_nodes[i].Dispose ();
		}
	}
}
