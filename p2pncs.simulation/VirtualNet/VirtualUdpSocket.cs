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
using System.Threading;
using p2pncs.Net;

namespace p2pncs.Simulation.VirtualNet
{
	public class VirtualUdpSocket : ISocket, VirtualNetwork.ISocketDeliver
	{
		VirtualNetwork _vnet;
		VirtualNetwork.VirtualNetworkNode _vnet_node = null;
		EndPoint _bindPubEP, _localEP;
		IPAddress _pubIP;
		long _recvBytes = 0, _sentBytes = 0, _recvDgrams = 0, _sentDgrams = 0;
		EventHandlers<Type, ReceivedEventArgs> _received = new EventHandlers<Type,ReceivedEventArgs> ();
		bool _bypassSerialize = true;

		public VirtualUdpSocket (VirtualNetwork vnet, IPAddress publicIPAddress, bool bypassSerialize)
		{
			if (vnet == null)
				throw new ArgumentNullException ();
			_vnet = vnet;
			_pubIP = publicIPAddress;
			_bypassSerialize = bypassSerialize;
		}

		void VirtualNetwork.ISocketDeliver.Deliver (EndPoint remoteEP, object msg)
		{
			_received.Invoke (msg.GetType (), this, new ReceivedEventArgs (msg, remoteEP));
			Interlocked.Increment (ref _recvDgrams);
		}

		void VirtualNetwork.ISocketDeliver.Deliver (EndPoint remoteEP, byte[] buf, int offset, int size)
		{
			object msg = Serializer.Instance.Deserialize (buf, offset, size);
			(this as VirtualNetwork.ISocketDeliver).Deliver (remoteEP, msg);
			Interlocked.Add (ref _recvBytes, size);
		}

		void VirtualNetwork.ISocketDeliver.FailedDeliver (EndPoint remoteEP)
		{
		}

		internal VirtualNetwork.VirtualNetworkNode VirtualNodeInfo {
			get { return _vnet_node; }
		}

		#region ISocket Members

#pragma warning disable 67
		public event EventHandler<AcceptingEventArgs> Accepting;
		public event EventHandler<AcceptedEventArgs> Accepted;
#pragma warning restore 67

		public void Bind (EndPoint localEP)
		{
			_localEP = localEP;
			_bindPubEP = new IPEndPoint (_pubIP, ((IPEndPoint)localEP).Port);
			_vnet_node = _vnet.AddVirtualNode (this, _bindPubEP);
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
			if (_vnet == null)
				return;
			if (remoteEP == null)
				throw new ArgumentNullException ();
			if (_bypassSerialize) {
				_vnet.AddSendQueue (_bindPubEP, remoteEP, message, false);
			} else {
				byte[] buf = Serializer.Instance.Serialize (message);
				if (buf.Length > ConstantParameters.MaxUdpDatagramSize)
					throw new System.Net.Sockets.SocketException ();
				_vnet.AddSendQueue (_bindPubEP, remoteEP, buf, 0, buf.Length, false);
				Interlocked.Add (ref _sentBytes, buf.Length);
			}
			Interlocked.Increment (ref _sentDgrams);
		}

		public EventHandlers<Type, ReceivedEventArgs> Received {
			get { return _received; }
		}

		public void Close ()
		{
			VirtualNetwork vnet = _vnet;
			lock (this) {
				if (_vnet == null)
					return;
				_vnet = null;
			}
			
			_received.Clear ();
			vnet.RemoveVirtualNode (_vnet_node);
			_vnet_node = null;
			_pubIP = IPAddress.None;
		}

		public EndPoint LocalEndPoint {
			get { return _localEP; }
		}

		public EndPoint RemoteEndPoint {
			get { throw new System.Net.Sockets.SocketException (); }
		}

		public int MaxDatagramSize {
			get { return ConstantParameters.MaxUdpDatagramSize; }
		}

		public long ReceivedBytes {
			get { return _recvBytes; }
		}

		public long SentBytes {
			get { return _sentBytes; }
		}

		public long ReceivedDatagrams {
			get { return _recvDgrams; }
		}

		public long SentDatagrams {
			get { return _sentDgrams; }
		}

		#endregion

		#region IDisposable Members

		public void Dispose ()
		{
			Close ();
		}

		#endregion
	}
}
