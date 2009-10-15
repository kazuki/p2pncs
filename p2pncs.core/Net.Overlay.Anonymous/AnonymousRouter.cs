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
using p2pncs.Threading;
using RouteLabel = System.UInt32;

namespace p2pncs.Net.Overlay.Anonymous
{
	public partial class AnonymousRouter : IAnonymousRouter
	{
		static readonly SymmetricKeyOption SymmetricCryptOption = new SymmetricKeyOption ();
		static readonly object ACK = "ACK";
		static readonly EndPoint MCRDummyEndPoint = new IPEndPoint (IPAddress.Any, 0);
		const int EstablishingPayloadSize = 304;
		const int RoutedPayloadSize = 928;
		static readonly TimeSpan MCR_DelayTime = TimeSpan.FromSeconds (10);
		static readonly TimeSpan MCR_EstablishTimeout = MCR_DelayTime + MCR_DelayTime;
		static readonly TimeSpan MCR_PingInterval = TimeSpan.FromSeconds (10);
		static readonly TimeSpan MCR_Timeout = MCR_PingInterval + MCR_DelayTime;

		IKeyBasedRouter _kbr;
		IKeyBasedRoutingAlgorithm _kbrAlgo;
		IMessagingSocket _sock;
		ECKeyPair _kbrPrivateKey;
		Key _kbrPublicKey;
		IntervalInterrupter _checkInt;

		Dictionary<Key, AnonymousEndPoint> _endPoints = new Dictionary<Key, AnonymousEndPoint> ();
		ReaderWriterLockWrapper _endPointsLock = new ReaderWriterLockWrapper ();

		Dictionary<MCREndPoint, IRouteInfo> _routes = new Dictionary<MCREndPoint, IRouteInfo> ();
		ReaderWriterLockWrapper _routesLock = new ReaderWriterLockWrapper ();

		public AnonymousRouter (IKeyBasedRouter kbr, IKeyBasedRoutingAlgorithm kbrAlgo, IMessagingSocket sock, ECKeyPair kbrPrivateKey, IntervalInterrupter timeoutCheckInt)
		{
			_kbr = kbr;
			_kbrAlgo = kbrAlgo;
			_sock = sock;
			_kbrPrivateKey = kbrPrivateKey;
			_kbrPublicKey = Key.Create (kbrPrivateKey);
			if (!_kbrPublicKey.Equals (_kbrAlgo.SelfNodeHandle.NodeID))
				throw new ArgumentException ();
			_checkInt = timeoutCheckInt;
			_checkInt.AddInterruption (TimeoutCheck);

			_sock.InquiredHandlers.Add (typeof (EstablishRouteMessage), InquiredHandler_EstablishRouteMessage);
			_sock.InquiredHandlers.Add (typeof (RoutedMessage), InquiredHandler_RoutedMessage);
		}

		#region IAnonymousRouter Members

		public IAnonymousEndPoint CreateEndPoint (ECKeyPair privateKey, object opts)
		{
			AnonymousEndPointOptions aepOpts = (AnonymousEndPointOptions)opts;
			if (aepOpts == null || aepOpts.AppId == null)
				throw new ArgumentNullException ();

			Key pubKey = Key.Create (privateKey);
			AnonymousEndPoint aep;

			using (_endPointsLock.EnterWriteLock ()) {
				if (_endPoints.ContainsKey (pubKey))
					throw new ArgumentException ();

				aep = new AnonymousEndPoint (this, pubKey, privateKey, aepOpts);
				_endPoints.Add (pubKey, aep);
			}
			aep.Init ();
			return aep;
		}

		public IAnonymousEndPoint GetEndPoint (Key address)
		{
			using (_endPointsLock.EnterReadLock ()) {
				return _endPoints[address];
			}
		}

		public IAnonymousEndPoint[] GetEndPoints ()
		{
			List<IAnonymousEndPoint> list = new List<IAnonymousEndPoint> ();
			using (_endPointsLock.EnterReadLock ()) {
				foreach (AnonymousEndPoint aep in _endPoints.Values)
					list.Add (aep);
			}
			return list.ToArray ();
		}

		public void Close ()
		{
			_checkInt.RemoveInterruption (TimeoutCheck);
			_sock.InquiredHandlers.Remove (typeof (EstablishRouteMessage), InquiredHandler_EstablishRouteMessage);
			_sock.InquiredHandlers.Remove (typeof (RoutedMessage), InquiredHandler_RoutedMessage);
		}

		#endregion

		void RemoveEndPoint (IAnonymousEndPoint ep)
		{
			using (_endPointsLock.EnterWriteLock ()) {
				_endPoints.Remove (ep.Address);
			}
		}

		void TimeoutCheck ()
		{
			List<MCREndPoint> timeoutEndPoints = new List<MCREndPoint> ();
			HashSet<IRouteInfo> timeoutRouteInfos = new HashSet<IRouteInfo> ();

			using (_routesLock.EnterReadLock ()) {
				foreach (KeyValuePair<MCREndPoint, IRouteInfo> pair in _routes) {
					if (!pair.Value.Check ()) {
						timeoutEndPoints.Add (pair.Key);
						timeoutRouteInfos.Add (pair.Value);
					}
				}
			}
			if (timeoutEndPoints.Count > 0) {
				using (_routesLock.EnterWriteLock ()) {
					for (int i = 0; i < timeoutEndPoints.Count; i ++)
						_routes.Remove (timeoutEndPoints[i]);
				}
				foreach (IRouteInfo routeInfo in timeoutRouteInfos) {
					routeInfo.Close ();
					Logger.Log (LogLevel.Trace, this, "MCR: Close {0}", routeInfo.GetType ().Name);
				}
			}

			using (_endPointsLock.EnterReadLock ()) {
				foreach (AnonymousEndPoint aep in _endPoints.Values) {
					aep.CheckRoutes ();
				}
			}
		}

		static RouteLabel GenerateRouteLabel ()
		{
			return BitConverter.ToUInt32 (openCrypto.RNG.GetBytes (4), 0);
		}

		interface IRouteInfo
		{
			void Inquired (AnonymousRouter router, RoutedMessage msg, MCREndPoint sender);
			bool Check ();
			void Close ();
		}
	}
}
