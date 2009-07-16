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
using System.Text;
using System.Net;
using System.Net.Sockets;

namespace p2pncs.Net
{
	public interface ITcpListener : IDisposable
	{
		void Bind (IPEndPoint bindEP);
		void ListenStart ();
		void RegisterAcceptHandler (Type firstMessageType, EventHandler<TcpListenerAcceptedEventArgs> handler);
		void UnregisterAcceptHandler (Type firstMessageType);

		void SendMessage (Socket sock, object msg);
		object ReceiveMessage (Socket sock, int max_size);
	}
}
