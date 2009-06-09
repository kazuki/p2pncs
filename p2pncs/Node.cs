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
using openCrypto.EllipticCurve;
using p2pncs.Net;
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.Anonymous;
using p2pncs.Net.Overlay.DHT;
using p2pncs.Security.Cryptography;
using p2pncs.Threading;

namespace p2pncs
{
	class Node : IDisposable
	{
		public static readonly TimeSpan DefaultMessagingTimeout = TimeSpan.FromMilliseconds (200);
		public const int DefaultMessagingRetry = 2;
		public const int DefaultMessagingRetryBufferSize = 8192;
		public const int DefaultMessagingDuplicationCheckBufferSize = 1024;
		public const ECDomainNames DefaultECDomainName = ECDomainNames.secp192r1;

		IDatagramEventSocket _dgramSock;
		IMessagingSocket _messagingSock;
		IKeyBasedRouter _kbr;
		IDistributedHashTable _dht;
		IAnonymousRouter _anonymous;
		IntervalInterrupter _msgInt = new IntervalInterrupter (TimeSpan.FromMilliseconds (100), "MessagingSocket Interval Interrupter");
		IntervalInterrupter _dhtInt = new IntervalInterrupter (TimeSpan.FromSeconds (1), "DHT Timeout Check Interrupter");
		IntervalInterrupter _kbrInt = new IntervalInterrupter (TimeSpan.FromSeconds (10), "KBR Stabilize Interval Interrupter");
		IntervalInterrupter _anonInt = new IntervalInterrupter (TimeSpan.FromMilliseconds (100), "AnonymousRouter Timeout Check Interrupter");

		ECKeyPair _kbrPrivateKey;

		public Node (IDatagramEventSocket bindedDgramSock)
		{
			_dgramSock = bindedDgramSock;
			_messagingSock = new MessagingSocket (_dgramSock, true, SymmetricKey.NoneKey, p2pncs.Serializer.Instance,
				null, _msgInt, DefaultMessagingTimeout, DefaultMessagingRetry, DefaultMessagingRetryBufferSize, DefaultMessagingDuplicationCheckBufferSize);
			TestLogger.SetupUdpMessagingSocket (_messagingSock);
			_kbrPrivateKey = ECKeyPair.Create (DefaultECDomainName);
			_kbr = new SimpleIterativeRouter2 (Key.Create (_kbrPrivateKey), _messagingSock, new SimpleRoutingAlgorithm (), p2pncs.Serializer.Instance, true);
			_dht = new SimpleDHT (_kbr, _messagingSock, new OnMemoryLocalHashTable (_dhtInt));
			_anonymous = new AnonymousRouter (_dht, _kbrPrivateKey, _anonInt);
			_kbrInt.AddInterruption (delegate () {
				_kbr.RoutingAlgorithm.Stabilize ();
			});

			_msgInt.Start ();
			_kbrInt.Start ();
			_dhtInt.Start ();
			_anonInt.Start ();
		}

		public IntervalInterrupter MessagingSocketInterrupter {
			get { return _msgInt; }
		}

		public IDatagramEventSocket DatagramEventSocket {
			get { return _dgramSock; }
		}

		public IMessagingSocket MessagingSocket {
			get { return _messagingSock; }
		}

		public IKeyBasedRouter KeyBasedRouter {
			get { return _kbr; }
		}

		public IDistributedHashTable DistributedHashTable {
			get { return _dht; }
		}

		public IAnonymousRouter AnonymousRouter {
			get { return _anonymous; }
		}

		public void Dispose ()
		{
			_msgInt.Dispose ();
			_kbrInt.Dispose ();
			_dhtInt.Dispose ();
			_anonInt.Dispose ();

			_anonymous.Close ();
			_dht.Dispose ();
			_kbr.Close ();
			_messagingSock.Dispose ();
			_dgramSock.Dispose ();
		}
	}
}