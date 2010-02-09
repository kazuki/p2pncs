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
using System.Collections.Generic;
using System.Net;
using System.Threading;
using p2pncs.Threading;
using p2pncs.Utility;

namespace p2pncs.Net.Overlay.Anonymous
{
	public class MCRAggregator : ISocket
	{
		const float FACTOR = 1.5f;
		int _numOfRoutes, _relay_min, _relay_max, _active = 0;
		MCRManager _mgr;
		IntervalInterrupter _mcrInt, _routeCheckInt;
		RelayNodeSelectorDelegate _selector;
		List<MCRSocket> _sockets;
		MCRAggregatedEndPoint _localEP = null;
		EventHandlers<Type, ReceivedEventArgs> _received = new EventHandlers<Type,ReceivedEventArgs> ();
		DuplicationChecker<ulong> _dupChecker = new DuplicationChecker<ulong> (MCRManager.DuplicationCheckSize);
		bool _checking = false;

		public event EventHandler ChangedActiveRoutes;

		public MCRAggregator (MCRManager mgr, int minRelays, int maxRelays, int numOfRoutes, IntervalInterrupter mcrTimeoutCheck, IntervalInterrupter routeCheckTimer, RelayNodeSelectorDelegate selector)
		{
			if (numOfRoutes <= 0 || (minRelays <= 0 || maxRelays <= 0 || minRelays > maxRelays))
				throw new ArgumentOutOfRangeException ();
			if (mcrTimeoutCheck == null || routeCheckTimer == null || selector == null)
				throw new ArgumentNullException ();
			_mgr = mgr;
			_relay_min = minRelays;
			_relay_max = maxRelays;
			_numOfRoutes = numOfRoutes;
			_mcrInt = mcrTimeoutCheck;
			_routeCheckInt = routeCheckTimer;
			_selector = selector;
			_sockets = new List<MCRSocket> ((int)Math.Ceiling (numOfRoutes * FACTOR));

			routeCheckTimer.AddInterruption (CheckRoutes);
		}

		#region Route Check
		void CheckRoutes ()
		{
			lock (this) {
				if (_checking)
					return;
				_checking = true;
			}
			try {
				int create_routes = (int)Math.Ceiling ((_numOfRoutes - _active) * FACTOR);
				create_routes -= _sockets.Count - _active;
				if (_numOfRoutes == 0 || create_routes <= 0)
					return;

				for (int i = 0; i < create_routes; i ++) {
					NodeHandle[] relays = _selector (ThreadSafeRandom.Next (_relay_min, _relay_max + 1));
					if (relays == null || relays.Length == 0)
						return;
					MCRSocket sock = new MCRSocket (_mgr, true);
					sock.Binded += MCRSocket_Binded;
					sock.Disconnected += MCRSocket_Disconnected;
					sock.Received.AddUnknownKeyHandler (MCRSocket_Received);
					lock (_sockets) {
						_sockets.Add (sock);
					}
					sock.Bind (new MCRBindEndPoint (relays));
				}
			} finally {
				_checking = false;
			}
		}
		#endregion

		#region MCRSocket Event Handlers
		void MCRSocket_Binded (object sender, EventArgs e)
		{
			Interlocked.Increment (ref _active);
			lock (_sockets) {
				UpdateLocalEndPoints ();
			}
			RaiseChangedActiveRoutesEvent ();
		}

		void MCRSocket_Disconnected (object sender, EventArgs e)
		{
			MCRSocket sock = (MCRSocket)sender;
			bool removed;
			lock (_sockets) {
				removed = _sockets.Remove (sock);
				if (removed)
					UpdateLocalEndPoints ();
			}
			sock.Dispose ();
			if (removed) {
				if (sock.IsBinded)
					Interlocked.Decrement (ref _active);
				CheckRoutes ();
			}
			RaiseChangedActiveRoutesEvent ();
		}

		void MCRSocket_Received (object sender, ReceivedEventArgs e)
		{
			MCRReceivedEventArgs e2 = (MCRReceivedEventArgs)e;
			if (e2.ID != 0 && !_dupChecker.Check (e2.ID))
				return;
			_received.Invoke (e2.Message.GetType (), this, e2);
		}
		#endregion

		#region ISocket Members

#pragma warning disable 67
		public event EventHandler<AcceptingEventArgs> Accepting;
		public event EventHandler<AcceptedEventArgs> Accepted;
#pragma warning restore 67

		public void Bind (EndPoint localEP)
		{
			throw new NotSupportedException ();
		}

		public void Connect (EndPoint remoteEP)
		{
			throw new NotSupportedException ();
		}

		public void Send (object message)
		{
			throw new NotSupportedException ();
		}

		public void SendTo (object message, EndPoint remoteEP)
		{
			if (remoteEP != null && !(remoteEP is MCREndPoint || remoteEP is MCRAggregatedEndPoint))
				throw new ArgumentException ();
			if (_localEP == null)
				throw new System.Net.Sockets.SocketException ();

			List<MCRSocket> sockets;
			lock (_sockets) {
				sockets = new List<MCRSocket> (_sockets);
			}
			ulong id;
			do {
				id = ThreadSafeRandom.NextUInt64 ();
			} while (id == 0);
			for (int i = 0; i < sockets.Count; i ++)
				if (sockets[i].IsBinded)
					sockets[i].SendTo (message, id, remoteEP, _localEP.EndPoints);
		}

		public EventHandlers<Type, ReceivedEventArgs> Received {
			get { return _received; }
		}

		public void Close ()
		{
			lock (this) {
				if (_numOfRoutes == 0)
					return;
				_numOfRoutes = 0;
			}
			_routeCheckInt.RemoveInterruption (CheckRoutes);
			lock (_sockets) {
				for (int i = 0; i < _sockets.Count; i++)
					_sockets[i].Dispose ();
				_sockets.Clear ();
			}
		}

		public EndPoint LocalEndPoint {
			get { return _localEP; }
		}

		public EndPoint RemoteEndPoint {
			get { throw new NotSupportedException (); }
		}

		#endregion

		#region IDisposable Members

		public void Dispose ()
		{
			Close ();
		}

		#endregion

		#region Misc
		void RaiseChangedActiveRoutesEvent ()
		{
			if (ChangedActiveRoutes == null)
				return;
			try {
				ChangedActiveRoutes (this, EventArgs.Empty);
			} catch {}
		}

		void UpdateLocalEndPoints ()
		{
			List<MCREndPoint> eps = new List<MCREndPoint> (_sockets.Count);
			for (int i = 0; i < _sockets.Count; i++)
				if (_sockets[i].IsBinded)
					eps.Add ((MCREndPoint)_sockets[i].LocalEndPoint);
			_localEP = new MCRAggregatedEndPoint (eps.ToArray ());
		}
		#endregion

		public delegate NodeHandle[] RelayNodeSelectorDelegate (int maxNum);
	}
}
  