﻿/*
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
		ILocalHashTable _localDHT;
		IDistributedHashTable _dht;
		IAnonymousRouter _anonRouter;
		IntervalInterrupter _kbrStabilizeInt;
		EvalEnvironment _env;
		List<AnonymousSocketInfo> _anonSockets = new List<AnonymousSocketInfo> ();

		public static ECDomainNames DefaultECDomain = DefaultAlgorithm.ECDomainName;
		static Random _rnd = new Random ();
		public static IRTOAlgorithm DefaultMessagingRTO = new RFC2988BasedRTOCalculator (TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(100), 50, false);
		public static int DefaultMessagingRetries = 2;
		public static int DefaultMessagingRetryBufferSize = 1024;
		public static int DefaultMessagingDupCheckSize = 512;
		public static TimeSpan DefaultACMessagingRTO = TimeSpan.FromSeconds (16);
		public static int DefaultACMessagingRetries = 2;
		public static int DefaultACMessagingRetryBufferSize = 1024;
		public static int DefaultACMessagingDupCheckSize = 512;
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
				? (IMessagingSocket)new VirtualMessagingSocket (sock, true, messagingInt, DefaultMessagingRTO, DefaultMessagingRetries, DefaultMessagingRetryBufferSize, DefaultMessagingDupCheckSize)
				: (IMessagingSocket)new MessagingSocket (sock, true, SymmetricKey.NoneKey, Serializer.Instance, null, messagingInt, DefaultMessagingRTO, DefaultMessagingRetries, DefaultMessagingRetryBufferSize, DefaultMessagingDupCheckSize);
			_kbr = new SimpleIterativeRouter2 (_nodeId, 0, _msock, new SimpleRoutingAlgorithm (), Serializer.Instance, opt.NewKBRStrictMode);
			_localDHT = new OnMemoryLocalHashTable (_kbr, dhtInt);
			_dht = new SimpleDHT (_kbr, _msock, _localDHT);
			_dht.RegisterTypeID (typeof (string), 0, new LocalHashTableValueMerger<string> ());
			p2pncs.Net.Overlay.Anonymous.AnonymousRouter.DefaultRelayNodes = opt.AnonymousRouteRelays;
			p2pncs.Net.Overlay.Anonymous.AnonymousRouter.DefaultSubscribeRoutes = opt.AnonymousRouteRoutes + opt.AnonymousRouteBackupRoutes;
			p2pncs.Net.Overlay.Anonymous.AnonymousRouter.AC_DefaultUseSubscribeRoutes = opt.AnonymousRouteRoutes;
			_anonRouter = new AnonymousRouter (_dht, _nodePrivateKey, anonInt);
			_kbrStabilizeInt = kbrStabilizeInt;
			_kbrStabilizeInt.AddInterruption (_kbr.RoutingAlgorithm.Stabilize);
			_env = env;
		}

		public ISubscribeInfo Subscribe ()
		{
			ECKeyPair pair = ECKeyPair.Create (DefaultAlgorithm.ECDomainName);
			Key key = Key.Create (pair);
			ISubscribeInfo info = _anonRouter.SubscribeRecipient (key, pair);
			info.Accepting += AnonymousRouter_Accepting;
			info.Accepted += AnonymousRouter_Accepted;
			SubscribeKey = info.Key.ToString ();
			return info;
		}

		void AnonymousRouter_Accepting (object sender, AcceptingEventArgs args)
		{
			args.Accept (null, null);
		}

		void AnonymousRouter_Accepted (object sender, AcceptedEventArgs args)
		{
			CreateAnonymousSocket (args.Socket);
		}

		void InquiredStringMessage (object sender, InquiredEventArgs e)
		{
			IMessagingSocket msock = sender as IMessagingSocket;
			string msg = ((string)e.InquireMessage) + "-" + SubscribeKey + "-ok";
			msock.StartResponse (e, msg);
		}

		public IMessagingSocket CreateAnonymousSocket (IAnonymousSocket sock)
		{
			IMessagingSocket msock = null;
			if (sock.ConnectionType == AnonymousConnectionType.LowLatency) {
				IRTOAlgorithm rtoAlgo = new RFC2988BasedRTOCalculator (DefaultACMessagingRTO,
					TimeSpan.FromMilliseconds (100), 50, true);
				msock = _env.CreateMessagingSocket (sock, rtoAlgo, DefaultACMessagingRetries,
					DefaultACMessagingRetryBufferSize, DefaultACMessagingDupCheckSize);
				msock.AddInquiredHandler (typeof (string), InquiredStringMessage);
			}
			AnonymousSocketInfo info = new AnonymousSocketInfo (sock, msock);
			lock (_anonSockets) {
				_anonSockets.Add (info);
			}
			if (sock.ConnectionType == AnonymousConnectionType.LowLatency) {
				sock.InitializedEventHandlers ();
			}
			return msock;
		}

		public string SubscribeKey { get; set; }

		public IList<AnonymousSocketInfo> AnonymousSocketInfoList {
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

		public ILocalHashTable LocalDistributedHashTable {
			get { return _localDHT; }
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

			lock (_anonSockets) {
				for (int i = 0; i < _anonSockets.Count; i ++) {
					if (_anonSockets[i].MessagingSocket == null) {
						_anonSockets[i].BaseSocket.Dispose ();
					} else {
						_anonSockets[i].MessagingSocket.Dispose ();
					}
				}
				_anonSockets.Clear ();
			}
		}
	}
}
