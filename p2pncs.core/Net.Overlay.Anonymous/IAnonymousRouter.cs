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
using openCrypto.EllipticCurve;
using p2pncs.Net.Overlay.DHT;

namespace p2pncs.Net.Overlay.Anonymous
{
	public interface IAnonymousRouter
	{
		ISubscribeInfo SubscribeRecipient (Key recipientId, ECKeyPair privateKey);
		void UnsubscribeRecipient (Key recipientId);

		void AddBoundaryNodeReceivedEventHandler (Type type, EventHandler<BoundaryNodeReceivedEventArgs> handler);
		void RemoveBoundaryNodeReceivedEventHandler (Type type);

		IAsyncResult BeginConnect (Key recipientId, Key destinationId, AnonymousConnectionType type, object payload, AsyncCallback callback, object state);
		IAnonymousSocket EndConnect (IAsyncResult ar);

		ISubscribeInfo GetSubscribeInfo (Key recipientId);
		IList<ISubscribeInfo> GetAllSubscribes ();
		IList<IAnonymousSocket> GetAllConnections ();

		IKeyBasedRouter KeyBasedRouter { get; }
		IDistributedHashTable DistributedHashTable { get; }

		void Close ();
	}
}
