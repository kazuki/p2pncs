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

namespace p2pncs.Net.Overlay.Anonymous
{
	public class AcceptingEventArgs : EventArgs
	{
		protected Key _recipiendId;
		protected object _payload, _state = null;
		protected DatagramReceiveEventHandler _handler = null;

		public AcceptingEventArgs (Key recipientId, object payload)
		{
			_recipiendId = recipientId;
			_payload = payload;
		}

		public Key RecipientId {
			get { return _recipiendId; }
		}

		public object Payload {
			get { return _payload; }
		}

		public DatagramReceiveEventHandler ReceiveEventHandler {
			get { return _handler; }
		}

		public object State {
			get { return _state; }
		}

		public void Accept (DatagramReceiveEventHandler handler, object state)
		{
			if (handler == null)
				throw new ArgumentNullException ();
			_handler = handler;
			_state = state;
		}

		public void Reject ()
		{
			_handler = null;
		}
	}
}
