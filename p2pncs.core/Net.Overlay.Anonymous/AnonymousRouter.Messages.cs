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
using RouteLabel = System.UInt32;

namespace p2pncs.Net.Overlay.Anonymous
{
	public partial class AnonymousRouter
	{
		[SerializableTypeId (0x500)]
		class EstablishRouteMessage
		{
			[SerializableFieldId (0)]
			RouteLabel _label;

			[SerializableFieldId (1)]
			byte[] _msg;

			public EstablishRouteMessage (RouteLabel label, byte[] msg)
			{
				_label = label;
				_msg = msg;
			}

			public byte[] Encrypted {
				get { return _msg; }
			}

			public RouteLabel Label {
				get { return _label; }
			}
		}

		[SerializableTypeId (0x501)]
		class EstablishedRouteMessage
		{
			[SerializableFieldId (0)]
			RouteLabel _label;

			public EstablishedRouteMessage (RouteLabel label)
			{
				_label = label;
			}
		}

		[SerializableTypeId (0x502)]
		class RoutedMessage
		{
			[SerializableFieldId (0)]
			RouteLabel _label;

			[SerializableFieldId (1)]
			byte[] _payload;

			public RoutedMessage (RouteLabel label, byte[] payload)
			{
				_label = label;
				_payload = payload;
			}

			public byte[] Payload {
				get { return _payload; }
			}

			public RouteLabel Label {
				get { return _label; }
			}
		}

		[SerializableTypeId (0x503)]
		class DisconnectMessage
		{
			[SerializableFieldId (0)]
			RouteLabel _label;

			public DisconnectMessage (RouteLabel label)
			{
				_label = label;
			}

			public RouteLabel Label {
				get { return _label; }
			}
		}

		[SerializableTypeId (0x504)]
		class PingMessage
		{
			static PingMessage _instance = new PingMessage ();
			PingMessage () {}
			public static PingMessage Instance {
				get { return _instance; }
			}
		}
	}
}
