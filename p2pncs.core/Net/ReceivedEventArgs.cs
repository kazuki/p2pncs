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
using System.Net;

namespace p2pncs.Net
{
	public class ReceivedEventArgs : EventArgs
	{
		object _obj;
		EndPoint _remoteEP;

		public ReceivedEventArgs (object obj, EndPoint remoteEP)
		{
			_obj = obj;
			_remoteEP = remoteEP;
		}

		public object Message {
			get { return _obj; }
		}

		public EndPoint RemoteEndPoint {
			get { return _remoteEP; }
		}
	}
}
