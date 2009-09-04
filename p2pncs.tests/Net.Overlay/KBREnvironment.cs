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
using openCrypto.EllipticCurve;
using p2pncs.Net;
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.Anonymous;
using p2pncs.Net.Overlay.DHT;
using p2pncs.Security.Cryptography;
using p2pncs.Simulation;
using p2pncs.Simulation.VirtualNet;
using p2pncs.Threading;

namespace p2pncs.tests.Net.Overlay
{
	class KBREnvironment : IDisposable
	{
		VirtualNetwork _network;
		IntervalInterrupter _interrupter, _dhtInt, _anonInt;
		List<EndPoint> _endPoints = new List<EndPoint> ();
		List<IMessagingSocket> _sockets = new List<IMessagingSocket> ();
		List<IKeyBasedRouter> _routers = new List<IKeyBasedRouter> ();
		List<IDistributedHashTable> _dhts = null;
		List<IAnonymousRouter> _anons = null;
		RandomIPAddressGenerator _ipGenerator = new RandomIPAddressGenerator ();
		EndPoint[] _initEPs = null;
		IRTOAlgorithm _rtoAlgo = new ConstantRTO (TimeSpan.FromSeconds (1));

		public KBREnvironment (bool enableDHT, bool enableAnon)
		{
			_network = new VirtualNetwork (LatencyTypes.Constant (20), 5, PacketLossType.Lossless (), 2);
			_interrupter = new IntervalInterrupter (TimeSpan.FromMilliseconds (50), "MessagingSocket Interrupter");
			_interrupter.Start ();
			if (enableDHT || enableAnon) {
				_dhts = new List<IDistributedHashTable> ();
				_dhtInt = new IntervalInterrupter (TimeSpan.FromSeconds (5), "DHT Maintenance Interrupter");
				_dhtInt.Start ();
			}
			if (enableAnon) {
				_anons = new List<IAnonymousRouter> ();
				_anonInt = new IntervalInterrupter (TimeSpan.FromMilliseconds (500), "Anonymous Interrupter");
				_anonInt.Start ();
			}
		}

		public void AddNodes (Key[] keys, ECKeyPair[] keyPairs)
		{
			for (int i = 0; i < keys.Length; i++) {
				IPAddress adrs = _ipGenerator.Next ();
				IPEndPoint ep = new IPEndPoint (adrs, 10000);
				VirtualDatagramEventSocket sock = new VirtualDatagramEventSocket (_network, adrs);
				sock.Bind (new IPEndPoint (IPAddress.Loopback, ep.Port));
				//IMessagingSocket msock = new MessagingSocket (sock, true, SymmetricKey.NoneKey, Serializer.Instance, null, _interrupter, TimeSpan.FromSeconds (1), 2, 1024);
				IMessagingSocket msock = new VirtualMessagingSocket (sock, true, _interrupter, _rtoAlgo, 2, 1024, 1024);
				_sockets.Add (msock);
				IKeyBasedRouter router = new SimpleIterativeRouter2 (keys[i], 0, msock, new SimpleRoutingAlgorithm (), Serializer.Instance, true);
				_routers.Add (router);
				if (_dhts != null) {
					IDistributedHashTable dht = new SimpleDHT (router, msock, new OnMemoryLocalHashTable (router, _dhtInt));
					_dhts.Add (dht);
					if (_anons != null) {
						IAnonymousRouter anonRouter = new AnonymousRouter (dht, keyPairs[i], _anonInt);
						_anons.Add (anonRouter);
					}
				}
				if (_endPoints.Count != 0) {
					if (_initEPs == null || _endPoints.Count < 3)
						_initEPs = _endPoints.ToArray ();
					router.Join (_initEPs);
				}
				_endPoints.Add (ep);
				Thread.Sleep (5);
			}
			Thread.Sleep (500);
		}

		public void RemoveNode (int index)
		{
			_endPoints.RemoveAt (index);
			if (_dhts != null) {
				if (_anons != null) {
					_anons[index].Close ();
					_anons.RemoveAt (index);
				}
				_dhts[index].Dispose ();
				_dhts.RemoveAt (index);
			}
			_routers[index].Close ();
			_routers.RemoveAt (index);
			_sockets[index].Close ();
			_sockets.RemoveAt (index);
		}

		public VirtualNetwork VirtualNetwork {
			get { return _network; }
		}

		public IList<EndPoint> EndPoints {
			get { return _endPoints; }
		}

		public IList<IMessagingSocket> Sockets {
			get { return _sockets; }
		}

		public IList<IKeyBasedRouter> KeyBasedRouters {
			get { return _routers; }
		}

		public IList<IDistributedHashTable> DistributedHashTables {
			get { return _dhts; }
		}

		public IList<IAnonymousRouter> AnonymousRouters {
			get { return _anons; }
		}

		public void Dispose ()
		{
			if (_dhts != null) {
				if (_anonInt != null) {
					_anonInt.Dispose ();
					for (int i = 0; i < _anons.Count; i++)
						_anons[i].Close ();
				}
				_dhtInt.Dispose ();
				for (int i = 0; i < _dhts.Count; i++)
					_dhts[i].Dispose ();
			}
			for (int i = 0; i < _routers.Count; i++)
				_routers[i].Close ();
			for (int i = 0; i < _sockets.Count; i++)
				_sockets[i].Dispose ();
			_network.Close ();
			_interrupter.Dispose ();
		}
	}
}
