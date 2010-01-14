/*
 * Copyright (C) 2009-2010 Kazuki Oikawa
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

namespace p2pncs.Net.Overlay.Anonymous
{
	public class AcceptedEventArgs : EventArgs
	{
		IAnonymousSocket _sock;
		AnonymousConnectionType _type;
		Key _recipiendId, _destId;
		object _state, _payload;

		public AcceptedEventArgs (IAnonymousSocket sock, AcceptingEventArgs acceptingArgs)
		{
			_sock = sock;
			_type = acceptingArgs.ConnectionType;
			_recipiendId = acceptingArgs.RecipientId;
			_destId = acceptingArgs.DestinationId;
			_state = acceptingArgs.State;
			_payload = acceptingArgs.Payload;
		}

		public IAnonymousSocket Socket {
			get { return _sock; }
		}

		public AnonymousConnectionType ConnectionType {
			get { return _type; }
		}

		public Key RecipientId {
			get { return _recipiendId; }
		}

		public Key DestinationId {
			get { return _destId; }
		}

		public object Payload {
			get { return _payload; }
		}

		public object State {
			get { return _state; }
		}
	}
}
