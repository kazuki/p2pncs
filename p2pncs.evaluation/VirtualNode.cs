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
using openCrypto.EllipticCurve;
using p2pncs.Net;
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.Anonymous;
using p2pncs.Net.Overlay.DHT;
using p2pncs.Security.Cryptography;
using p2pncs.Simulation;
using p2pncs.Simulation.VirtualNet;
using p2pncs.Threading;

namespace p2pncs.Evaluation
{
	class VirtualNode : IDisposable
	{
		ECKeyPair _nodePrivateKey;
		Key _nodeId;
		IPEndPoint _pubEP;
		IMessagingSocket _msock;
		IKeyBasedRouter _kbr;
		IDistributedHashTable _dht;
		IAnonymousRouter _anonRouter;
		IntervalInterrupter _kbrStabilizeInt;
		EvalEnvironment _env;
		List<IMessagingSocket> _anonSockets = new List<IMessagingSocket> ();

		public static ECDomainNames DefaultECDomain = ECDomainNames.secp112r2;
		static Random _rnd = new Random ();
		public static TimeSpan DefaultMessagingTimeout = TimeSpan.FromSeconds (3);
		public static int DefaultMessagingRetries = 2;
		public static int DefaultMessagingRetryBufferSize = 8192;
		public static int DefaultMessagingDupCheckSize = 8192;
		static RandomIPAddressGenerator _ipGenerator = new RandomIPAddressGenerator ();

		public VirtualNode (EvalEnvironment env, VirtualNetwork network, EvalOptionSet opt,
			IntervalInterrupter messagingInt, IntervalInterrupter kbrStabilizeInt, IntervalInterrupter anonInt, IntervalInterrupter dhtInt)
		{
			IPAddress pubAdrs = _ipGenerator.Next ();
			int bindPort;
			lock (_rnd) {
				bindPort = _rnd.Next (1024, ushort.MaxValue);
			}
			_pubEP = new IPEndPoint (pubAdrs, bindPort);
			_nodePrivateKey = ECKeyPair.Create (DefaultECDomain);
			_nodeId = Key.Create (_nodePrivateKey);
			VirtualDatagramEventSocket sock = new VirtualDatagramEventSocket (network, pubAdrs);
			sock.Bind (new IPEndPoint (IPAddress.Any, bindPort));
			_msock = opt.BypassMessagingSerializer
				? (IMessagingSocket)new VirtualMessagingSocket (sock, true, messagingInt, DefaultMessagingTimeout, DefaultMessagingRetries, DefaultMessagingRetryBufferSize, DefaultMessagingDupCheckSize)
				: (IMessagingSocket)new MessagingSocket (sock, true, SymmetricKey.NoneKey, Serializer.Instance, null, messagingInt, DefaultMessagingTimeout, DefaultMessagingRetries, DefaultMessagingRetryBufferSize, DefaultMessagingDupCheckSize);
			_kbr = opt.UseNewKeyBasedRouter
				? (IKeyBasedRouter)new SimpleIterativeRouter2 (_nodeId, _msock, new SimpleRoutingAlgorithm (), Serializer.Instance, opt.NewKBRStrictMode)
				: (IKeyBasedRouter)new SimpleIterativeRouter (_nodeId, _msock, new SimpleRoutingAlgorithm (), Serializer.Instance);
			_dht = new SimpleDHT (_kbr, _msock, new OnMemoryLocalHashTable (dhtInt));
			AnonymousRouter2.DefaultRelayNodes = opt.AnonymousRouteRelays;
			AnonymousRouter2.DefaultSubscribeRoutes = opt.AnonymousRouteRoutes + opt.AnonymousRouteBackupRoutes;
			AnonymousRouter2.AC_DefaultUseSubscribeRoutes = opt.AnonymousRouteRoutes;
			_anonRouter = opt.UseNewAnonymousRouter
				? (IAnonymousRouter)new AnonymousRouter2 (_dht, _nodePrivateKey, anonInt)
				: (IAnonymousRouter)new AnonymousRouter (_dht, _nodePrivateKey, anonInt);
			_kbrStabilizeInt = kbrStabilizeInt;
			_kbrStabilizeInt.AddInterruption (_kbr.RoutingAlgorithm.Stabilize);
			_env = env;
			_anonRouter.Accepting += AnonymousRouter_Accepting;
			_anonRouter.Accepted += AnonymousRouter_Accepted;
		}

		void AnonymousRouter_Accepting (object sender, AcceptingEventArgs args)
		{
			DatagramEventSocketWrapper sock = new DatagramEventSocketWrapper ();
			args.Accept (sock.ReceivedHandler, sock);
		}

		void AnonymousRouter_Accepted (object sender, AcceptedEventArgs args)
		{
			DatagramEventSocketWrapper sock = (DatagramEventSocketWrapper)args.State;
			sock.Socket = args.Socket;
			CreateAnonymousSocket (sock);
		}

		void InquiredStringMessage (object sender, InquiredEventArgs e)
		{
			IMessagingSocket msock = sender as IMessagingSocket;
			string msg = ((string)e.InquireMessage) + "-" + SubscribeKey + "-ok";
			msock.StartResponse (e, msg);
		}

		public IMessagingSocket CreateAnonymousSocket (DatagramEventSocketWrapper sock)
		{
			IMessagingSocket msock = _env.CreateMessagingSocket (sock);
			msock.AddInquiredHandler (typeof (string), InquiredStringMessage);
			lock (_anonSockets) {
				_anonSockets.Add (msock);
			}
			return msock;
		}

		public string SubscribeKey { get; set; }

		public IList<IMessagingSocket> AnonymousSockets {
			get { return _anonSockets; }
		}

		public IPEndPoint PublicEndPoint {
			get { return _pubEP; }
		}

		public ECKeyPair NodePrivateKey {
			get { return _nodePrivateKey; }
		}

		public Key NodeID {
			get { return _nodeId; }
		}

		public IMessagingSocket MessagingSocket {
			get { return _msock; }
		}

		public IKeyBasedRouter KeyBasedRouter {
			get { return _kbr; }
		}

		public IDistributedHashTable DistributedHashTable {
			get { return _dht; }
		}

		public IAnonymousRouter AnonymousRouter {
			get { return _anonRouter; }
		}

		public void Dispose ()
		{
			_kbrStabilizeInt.RemoveInterruption (_kbr.RoutingAlgorithm.Stabilize);
			_anonRouter.Close ();
			_dht.Dispose ();
			_kbr.Close ();
			_msock.Dispose ();
		}
	}
}
