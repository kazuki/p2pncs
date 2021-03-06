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
using System.Net;
using System.IO;

namespace p2pncs.Net.Overlay
{
	[Serializable]
	[SerializableTypeId (0x101)]
	public class NodeHandle
	{
		[SerializableFieldId (0)]
		EndPoint _ep;

		[SerializableFieldId (1)]
		Key _id;

		[SerializableFieldId (2)]
		ushort _tcpPort;

		public NodeHandle (Key id, EndPoint ep, ushort tcpPort)
		{
			_id = id;
			_ep = ep;
			_tcpPort = tcpPort;
		}

		public EndPoint EndPoint {
			get { return _ep; }
		}

		public Key NodeID {
			get { return _id; }
		}

		public ushort TcpPort {
			get { return _tcpPort; }
		}

		public override string ToString ()
		{
			return (_id == null ? "null" : _id.ToString ()) + (_ep == null ? "@null" : "@" + _ep.ToString ()) + "#" + _tcpPort.ToString ();
		}
	}
}
