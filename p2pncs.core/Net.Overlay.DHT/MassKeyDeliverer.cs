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
using p2pncs.Threading;
using p2pncs.Utility;

namespace p2pncs.Net.Overlay.DHT
{
	public class MassKeyDeliverer : IDisposable
	{
		const int SEND_NODES = 3;
		IKeyBasedRouter _router;
		IMessagingSocket _sock;
		IMassKeyDelivererLocalStore _store;
		IntervalInterrupter _int;
		List<DHTEntry>[] _values;

		public MassKeyDeliverer (IKeyBasedRouter router, IMassKeyDelivererLocalStore store, IntervalInterrupter timer)
		{
			_router = router;
			_sock = router.MessagingSocket;
			_store = store;
			_int = timer;
			timer.AddInterruption (Deliver);
			_values = new List<DHTEntry> [router.RoutingAlgorithm.MaxRoutingLevel];
			for (int i = 0; i < _values.Length; i ++)
				_values[i] = new List<DHTEntry> ();
			_sock.AddInquiredHandler (typeof (Message), Messaging_Inquired);
		}

		void Messaging_Inquired (object sender, InquiredEventArgs e)
		{
			Message msg = e.InquireMessage as Message;
			_router.RoutingAlgorithm.Touch (new NodeHandle (msg.Sender, e.EndPoint));

			for (int i = 0; i < msg.Entries.Length; i ++) {
				DHTEntry entry = msg.Entries[i];
				_store.Put (entry.Key, entry.TypeId, entry.ExpirationDate, entry.Value);
			}
		}

		void Deliver ()
		{
			_store.GetEachRoutingLevelValues (_values);
			for (int i = 0; i < _values.Length; i++) {
				if (_values[i].Count == 0)
					continue;
				NodeHandle[] nodes = _router.RoutingAlgorithm.GetNextHopNodes (_values[i][0].Key, 32, null);
				if (nodes == null)
					continue;
				nodes = nodes.RandomSelection (SEND_NODES);
				Message msg = new Message (_router.SelftNodeId, _values[i].ToArray ());
				for (int q = 0; q < nodes.Length; q ++)
					_sock.BeginInquire (msg, nodes[q].EndPoint, Deliver_Callback, nodes[q]);
			}

			for (int i = 0; i < _values.Length; i++)
				_values[i].Clear ();
		}

		void Deliver_Callback (IAsyncResult ar)
		{
			NodeHandle nodeHandle = ar.AsyncState as NodeHandle;
			object ret = _sock.EndInquire (ar);
			if (ret == null)
				_router.RoutingAlgorithm.Fail (nodeHandle);
			else
				_router.RoutingAlgorithm.Touch (nodeHandle);
		}

		public void Dispose ()
		{
			_int.RemoveInterruption (Deliver);
			_sock.RemoveInquiredHandler (typeof (Message), Messaging_Inquired);
		}

		[SerializableTypeId (0x304)]
		sealed class Message
		{
			[SerializableFieldId (0)]
			Key _sender;

			[SerializableFieldId (1)]
			DHTEntry[] _entries;

			public Message (Key sender, DHTEntry[] entries)
			{
				_sender = sender;
				_entries = entries;
			}

			public Key Sender {
				get { return _sender; }
			}

			public DHTEntry[] Entries {
				get { return _entries; }
			}
		}
	}
}
