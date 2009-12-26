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
using System.Net;
using System.Threading;
using openCrypto.EllipticCurve;
using p2pncs.Security.Cryptography;

namespace p2pncs.Net.Overlay.Anonymous
{
	public class MCRSocket : ISocket, MCRManager.IRouteInfo
	{
		int _seq = -1;
		MCRManager _mgr;
		bool _binding = false, _binded = false;
		bool _active = true;
		MCREndPoint _firstHop;
		SymmetricKey[] _relayKeys;
		EventHandlers<Type, ReceivedEventArgs> _received = new EventHandlers<Type,ReceivedEventArgs> ();
		MCRBindEndPoint _bindEP = null;
		MCREndPoint _localEP = null;

		public event EventHandler Binded;
		public event EventHandler Disconnected;

		public MCRSocket (MCRManager mgr)
		{
			_mgr = mgr;
		}

		#region ISocket Members

		public ISocket Accept ()
		{
			throw new NotSupportedException ();
		}

		public void Bind (EndPoint localEP)
		{
			lock (this) {
				if (_binding || _binded)
					throw new ApplicationException ("Šù‚ÉƒoƒCƒ“ƒh‚³‚ê‚Ä‚¢‚Ü‚·");
				_binding = true;
			}
			MCRBindEndPoint bindEP = localEP as MCRBindEndPoint;
			if (bindEP == null || bindEP.RelayNodes.Length <= 0) throw new ArgumentException ();

			_bindEP = bindEP;
			_relayKeys = new SymmetricKey[bindEP.RelayNodes.Length];
			uint routeLabel = MCRManager.GenerateRouteLabel ();
			_firstHop = new MCREndPoint (bindEP.RelayNodes[0].EndPoint, routeLabel);
			byte[] encrypted = MCRManager.CipherUtility.CreateEstablishMessageData (bindEP.RelayNodes, _relayKeys, "HELLO",
				ConstantParameters.ECDomainName, MCRManager.DefaultSymmetricKeyOption, MCRManager.FixedMessageSize);
			MCRManager.EstablishRouteMessage msg = new MCRManager.EstablishRouteMessage (routeLabel, encrypted);
			_mgr.AddRouteInfo (_firstHop, this);
			_mgr.Socket.BeginInquire (msg, _firstHop.EndPoint, delegate (IAsyncResult ar) {
				if (_mgr.Socket.EndInquire (ar) != null)
					return;
				Close ();
			}, null);
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
			if (message == null || remoteEP == null)
				throw new ArgumentNullException ();
			MCREndPoint mcrRemoteEP = remoteEP as MCREndPoint;
			if (mcrRemoteEP == null)
				throw new ArgumentException ();
			SendToInternal (message, mcrRemoteEP);
		}

		public EventHandlers<Type, ReceivedEventArgs> Received {
			get { return _received; }
		}

		public void Close ()
		{
			lock (this) {
				if (!_active)
					return;
				_active = false;
			}

			if (_firstHop != null) {
				_mgr.RemoveRouteInfo (_firstHop);
				RaiseDisconnectedEvent ();
			}
		}

		public EndPoint LocalEndPoint {
			get {
				if (_localEP == null)
					throw new System.Net.Sockets.SocketException ();
				return _localEP;
			}
		}

		public EndPoint RemoteEndPoint {
			get { throw new System.Net.Sockets.SocketException (); }
		}

		#endregion

		#region IDisposable Members

		public void Dispose ()
		{
			Close ();
		}

		#endregion

		#region IRouteInfo Members

		void MCRManager.IRouteInfo.Received (IInquirySocket sock, InquiredEventArgs e, MCREndPoint ep, MCRManager.RoutedMessage msg)
		{
			uint seq;
			object payload = MCRManager.CipherUtility.DecryptRoutedPayload (_relayKeys, out seq, msg.Payload);
			MCREndPoint endPoint = msg.EndPoint as MCREndPoint;
			if (endPoint != null) {
				try {
					Received.Invoke (payload.GetType (), this, new ReceivedEventArgs (payload, endPoint));
				} catch {}
				return;
			}

			if (payload is MCRManager.EstablishedRouteMessage) {
				lock (this) {
					if (_binded)
						return;
					_binded = true;
				}
				_localEP = new MCREndPoint (_bindEP.RelayNodes[_bindEP.RelayNodes.Length - 1].EndPoint,
					((MCRManager.EstablishedRouteMessage)payload).Label);
				RaiseBindedEvent ();
				return;
			}
		}

		void MCRManager.IRouteInfo.Received (IInquirySocket sock, ReceivedEventArgs e, MCREndPoint ep, MCRManager.RoutedMessage msg)
		{
			throw new NotImplementedException ();
		}

		#endregion

		#region Properties
		public bool IsBinded {
			get { return _binded; }
		}
		#endregion

		public void SendToTerminalNode (object msg)
		{
			if (msg == null)
				throw new ArgumentNullException ();
			SendToInternal (msg, null);
		}

		void SendToInternal (object msg, MCREndPoint ep)
		{
			uint seq = (uint)Interlocked.Increment (ref _seq);
			byte[] payload = MCRManager.CipherUtility.CreateRoutedPayload (_relayKeys, seq, msg, MCRManager.FixedMessageSize);
			_mgr.Socket.BeginInquire (new MCRManager.RoutedMessage (_firstHop.Label, ep, payload), _firstHop.EndPoint, delegate (IAsyncResult ar) {
				if (_mgr.Socket.EndInquire (ar) != null)
					return;
				Close ();
			}, null);
		}

		#region Misc
		void RaiseDisconnectedEvent ()
		{
			if (Disconnected == null)
				return;
			try {
				Disconnected (this, EventArgs.Empty);
			} catch {}
		}
		void RaiseBindedEvent ()
		{
			if (Binded == null)
				return;
			try {
				Binded (this, EventArgs.Empty);
			} catch {}
		}
		#endregion
	}
}
