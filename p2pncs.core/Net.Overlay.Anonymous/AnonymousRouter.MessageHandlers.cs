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
using p2pncs.Security.Cryptography;
using p2pncs.Threading;
using RouteLabel = System.UInt32;

namespace p2pncs.Net.Overlay.Anonymous
{
	public partial class AnonymousRouter
	{
		void InquiredHandler_EstablishRouteMessage (object sender, InquiredEventArgs args)
		{
			_sock.StartResponse (args, ACK);

			SymmetricKey key; EndPoint nextHop; object payload;
			EstablishRouteMessage msg = (EstablishRouteMessage)args.InquireMessage;
			byte[] nextPayload = MCRCipherUtility.DecryptEstablishMessageData (_kbrPrivateKey,
				msg.Encrypted, _kbrPublicKey.KeyBytes, SymmetricCryptOption, out key, out nextHop, out payload);
			if (payload != null)
				nextHop = MCRDummyEndPoint;

			IRouteInfo routeInfo;
			using (_routesLock.EnterWriteLock ()) {
				MCREndPoint prevEP = new MCREndPoint (args.EndPoint, msg.Label), nextEP;
				if (_routes.ContainsKey (prevEP))
					return;
				while (true) {
					nextEP = new MCREndPoint (nextHop, GenerateRouteLabel ());
					if (!_routes.ContainsKey (nextEP)) {
						if (payload == null) {
							routeInfo = new MCRRelayInfo (prevEP, nextEP, key);
						} else {
							routeInfo = new MCRBoundaryInfo (prevEP, key, nextEP.Label, payload);
						}
						_routes.Add (prevEP, routeInfo);
						_routes.Add (nextEP, routeInfo);
						break;
					}
				}
			}

			if (payload == null) {
				MCRRelayInfo relayInfo = (MCRRelayInfo)routeInfo;
				Logger.Log (LogLevel.Trace, this, "MCR: Create Relay {0}<-->{1}", relayInfo.PrevEndPoint, relayInfo.NextEndPoint);
				msg = new EstablishRouteMessage (relayInfo.NextEndPoint.Label, nextPayload);
				_sock.BeginInquire (msg, relayInfo.NextEndPoint.EndPoint, delegate (IAsyncResult ar) {
					if (_sock.EndInquire (ar) != null)
						return;
					using (_routesLock.EnterWriteLock ()) {
						_routes.Remove (relayInfo.PrevEndPoint);
						_routes.Remove (relayInfo.NextEndPoint);
					}
					_sock.BeginInquire (new DisconnectMessage (relayInfo.PrevEndPoint.Label), relayInfo.PrevEndPoint.EndPoint, null, null);
				}, null);
			} else {
				MCRBoundaryInfo boundaryInfo = (MCRBoundaryInfo)routeInfo;
				Logger.Log (LogLevel.Trace, this, "MCR: Create Boundary. {0}<--|Payload={1}", boundaryInfo.PrevEndPoint, boundaryInfo.Payload);
				boundaryInfo.Send (_sock, new EstablishedRouteMessage (boundaryInfo.Label));
			}
		}

		void InquiredHandler_RoutedMessage (object sender, InquiredEventArgs args)
		{
			RoutedMessage msg = (RoutedMessage)args.InquireMessage;
			MCREndPoint srcEP = new MCREndPoint (args.EndPoint, msg.Label);
			IRouteInfo routeInfo;
			using (_routesLock.EnterReadLock ()) {
				if (!_routes.TryGetValue (srcEP, out routeInfo))
					routeInfo = null;
			}
			if (routeInfo == null) {
				_sock.StartResponse (args, null);
			} else {
				_sock.StartResponse (args, ACK);
				routeInfo.Inquired (this, msg, srcEP);
			}
		}
	}
}
