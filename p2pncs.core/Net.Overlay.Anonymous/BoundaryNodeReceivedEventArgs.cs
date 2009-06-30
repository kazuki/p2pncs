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
	public abstract class BoundaryNodeReceivedEventArgs : EventArgs
	{
		Key _key;
		object _req;
		bool _needResponse;

		public BoundaryNodeReceivedEventArgs (Key key, object request, bool needRes)
		{
			_key = key;
			_req = request;
			_needResponse = needRes;
		}

		public Key RecipientKey {
			get { return _key; }
		}

		public object Request {
			get { return _req; }
		}

		public bool NeedsResponse {
			get { return _needResponse; }
		}

		public abstract void StartResponse (object response);

		public abstract void SendMessage (object msg);
	}
}
