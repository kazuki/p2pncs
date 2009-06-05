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

using p2pncs.Net;
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.Anonymous;

namespace p2pncs.Evaluation
{
	class AnonymousSocketInfo
	{
		public AnonymousSocketInfo (IMessagingSocket msock)
		{
			MessagingSocket = msock;
		}

		public IMessagingSocket MessagingSocket { get; set; }
		public IAnonymousSocket BaseSocket {
			get { return MessagingSocket.BaseSocket as IAnonymousSocket; }
		}
	}
}
