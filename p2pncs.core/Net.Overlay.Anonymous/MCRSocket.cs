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
using p2pncs.Utility;

namespace p2pncs.Net.Overlay.Anonymous
{
	public class MCRSocket : ISocket, MCRManager.IRouteInfo
	{
		int _seq = 0;
		MCRManager _mgr;
		bool _binding = false, _binded = false, _active = true, _reliableMode = true;
		MCREndPoint _firstHop;
		SymmetricKey[] _relayKeys;
		EventHandlers<Type, ReceivedEventArgs> _received = new EventHandlers<Type,ReceivedEventArgs> ();
		MCRBindEndPoint _bindEP = null;
		MCREndPoint _localEP = null;
		DateTime _nextPingTime = DateTime.MaxValue;
		DateTime _pingRecvExpire = DateTime.MaxValue;
		AntiReplayWindow _antiReplay = new AntiReplayWindow (MCRManager.AntiReplayWindowSize);

		public event EventHandler Binded;
		public event EventHandler Disconnected;

		public MCRSocket (MCRManager mgr, bool reliableMode)
		{
			_mgr = mgr;
			_reliableMode = reliableMode;

			mgr.TimeoutCheckInterrupter.AddInterruption ((this as MCRManager.IRouteInfo).CheckTimeout);
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
					throw new ApplicationException ("既にバインドされています");
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
				object res = _mgr.Socket.EndInquire (ar);
				if (MCRManager.ACK.Equals (res))
					return;
				Close ();
				if (res == null)
					_mgr.RaiseInquiryFailedEvent (_firstHop.EndPoint);
			}, null);

			// MCR確立タイムアウトを指定: 1s * 中継ノード数 * 2(往復) * 1.5(再送考慮ファクタ)
			_pingRecvExpire = DateTime.Now + TimeSpan.FromSeconds (bindEP.RelayNodes.Length * 3);
		}

		public void Connect (EndPoint remoteEP)
		{
			throw new NotSupportedException ();
		}

		public void Send (object message)
		{
			throw new NotSupportedException ();
		}

		/// <param name="remoteEP">nullを指定した場合、MCR終端に配送する</param>
		public void SendTo (object message, EndPoint remoteEP)
		{
			ulong id;
			do {
				id = ThreadSafeRandom.NextUInt64 ();
			} while (id == 0);
			SendTo (message, id, remoteEP, null);
		}

		/// <param name="remoteEP">nullを指定した場合、MCR終端に配送する</param>
		internal void SendTo (object message, ulong id, EndPoint remoteEP, MCREndPoint[] srcEP)
		{
			if (message == null)
				throw new ArgumentNullException ();
			MCREndPoint[] mcrRemoteEPs = null;
			if (remoteEP is MCREndPoint)
				mcrRemoteEPs = new MCREndPoint[] {(MCREndPoint)remoteEP};
			else if (remoteEP is MCRAggregatedEndPoint)
				mcrRemoteEPs = ((MCRAggregatedEndPoint)remoteEP).EndPoints;
			if (remoteEP != null && mcrRemoteEPs == null)
				throw new ArgumentException ();
			if (srcEP == null || srcEP.Length == 0)
				srcEP = new MCREndPoint[] {_localEP};
			bool forceReliableMode = (message is MCRManager.PingMessage);
			
			uint seq = (uint)Interlocked.Increment (ref _seq);
			if (mcrRemoteEPs != null)
				message = new MCRManager.InterTerminalRequestMessage (mcrRemoteEPs, srcEP, message, id);
			byte[] payload = MCRManager.CipherUtility.CreateRoutedPayload (_relayKeys, seq, message, MCRManager.FixedMessageSize);
			message = new MCRManager.RoutedMessage (_firstHop.Label, payload);
			_nextPingTime = DateTime.Now + MCRManager.PingInterval;
			if (IsReliableMode || forceReliableMode) {
				_mgr.Socket.BeginInquire (message, _firstHop.EndPoint, delegate (IAsyncResult ar) {
					object res = _mgr.Socket.EndInquire (ar);
					if (MCRManager.ACK.Equals (res))
						return;
					Close ();
					if (res == null)
						_mgr.RaiseInquiryFailedEvent (_firstHop.EndPoint);
				}, null);
			} else {
				_mgr.Socket.SendTo (message, _firstHop.EndPoint);
			}
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

			_received.Clear ();
			_mgr.TimeoutCheckInterrupter.RemoveInterruption ((this as MCRManager.IRouteInfo).CheckTimeout);
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

		void MCRManager.IRouteInfo.Received (IInquirySocket sock, MCREndPoint ep, MCRManager.RoutedMessage msg, bool isReliableMode)
		{
			uint seq;
			object payload = MCRManager.CipherUtility.DecryptRoutedPayload (_relayKeys, out seq, msg.Payload);

			if (payload is MCRManager.EstablishedRouteMessage) {
				lock (this) {
					if (_binded)
						return;
					_binded = true;
				}
				_localEP = new MCREndPoint (_bindEP.RelayNodes[_bindEP.RelayNodes.Length - 1].EndPoint,
					((MCRManager.EstablishedRouteMessage)payload).Label);
				_nextPingTime = DateTime.Now + MCRManager.PingInterval;
				_pingRecvExpire = DateTime.Now + MCRManager.MaxPingInterval;
				RaiseBindedEvent ();
				return;
			}

			// EstablishRouteMessageは_pingRecvExpireに影響を与えないのでここで値を更新
			_pingRecvExpire = DateTime.Now + MCRManager.MaxPingInterval;

			if (!_antiReplay.Check (seq) || payload is MCRManager.PingMessage)
				return;

			if (payload is MCRManager.InterTerminalPayload) {
				MCRManager.InterTerminalPayload itp = payload as MCRManager.InterTerminalPayload;
				Received.Invoke (itp.Payload.GetType (), this, new MCRReceivedEventArgs (itp.Payload, itp.ID, itp.SrcEndPoints, isReliableMode));
				return;
			}

			try {
				Received.Invoke (payload.GetType (), this, new MCRReceivedEventArgs (payload, 0, isReliableMode));
			} catch {}
		}

		void MCRManager.IRouteInfo.CheckTimeout ()
		{
			if (_pingRecvExpire < DateTime.Now) {
				Close ();
				return;
			}

			if (_nextPingTime < DateTime.Now)
				SendTo (MCRManager.PingMessage.Instance, null);
		}

		#endregion

		#region Properties
		public bool IsBinded {
			get { return _binded; }
		}

		public bool IsReliableMode {
			get { return _reliableMode; }
			set { _reliableMode = value;}
		}
		#endregion

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
