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
using p2pncs.Net.Overlay.DHT;
using openCrypto.EllipticCurve;

namespace p2pncs.Net.Overlay.Anonymous
{
	public interface IAnonymousRouter
	{
		event AcceptingEventHandler Accepting;
		event AcceptedEventHandler Accepted;

		void SubscribeRecipient (Key recipientId, ECKeyPair privateKey);
		void UnsubscribeRecipient (Key recipientId);

		IAsyncResult BeginConnect (Key recipientId, Key destinationId, AnonymousConnectionType type, AsyncCallback callback, object state);
		IAnonymousSocket EndConnect (IAsyncResult ar);

		ISubscribeInfo GetSubscribeInfo (Key recipientId);

		IKeyBasedRouter KeyBasedRouter { get; }
		IDistributedHashTable DistributedHashTable { get; }

		void Close ();
	}
}
