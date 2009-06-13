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
using openCrypto.EllipticCurve;
using p2pncs.Net;
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.Anonymous;
using p2pncs.Net.Overlay.DHT;
using p2pncs.Security.Cryptography;

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
		Interrupters _ints;

		ECKeyPair _kbrPrivateKey;

		public Node (Interrupters ints, IDatagramEventSocket bindedDgramSock)
		{
			_ints = ints;
			_dgramSock = bindedDgramSock;
			_messagingSock = new MessagingSocket (_dgramSock, true, SymmetricKey.NoneKey, p2pncs.Serializer.Instance,
				null, ints.MessagingInt, DefaultMessagingTimeout, DefaultMessagingRetry, DefaultMessagingRetryBufferSize, DefaultMessagingDuplicationCheckBufferSize);
			TestLogger.SetupUdpMessagingSocket (_messagingSock);
			_kbrPrivateKey = ECKeyPair.Create (DefaultECDomainName);
			_kbr = new SimpleIterativeRouter2 (Key.Create (_kbrPrivateKey), _messagingSock, new SimpleRoutingAlgorithm (), p2pncs.Serializer.Instance, true);
			_dht = new SimpleDHT (_kbr, _messagingSock, new OnMemoryLocalHashTable (_kbr, ints.DHTInt));
			_anonymous = new AnonymousRouter (_dht, _kbrPrivateKey, ints.AnonymousInt);
			ints.KBRStabilizeInt.AddInterruption (delegate () {
				_kbr.RoutingAlgorithm.Stabilize ();
			});
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
			_anonymous.Close ();
			_dht.Dispose ();
			_kbr.Close ();
			_messagingSock.Dispose ();
			_dgramSock.Dispose ();
		}
	}
}
