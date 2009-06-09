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
using System.Threading;
using System.Net;
using p2pncs.Net;

namespace p2pncs.Simulation.VirtualNet
{
	public class VirtualDatagramEventSocket : IDatagramEventSocket
	{
		VirtualNetwork _vnet;
		VirtualNetwork.VirtualNetworkNode _vnet_node = null;
		EndPoint _bindPubEP;
		IPAddress _pubIP;
		long _recvBytes = 0, _sentBytes = 0, _recvDgrams = 0, _sentDgrams = 0;

		public VirtualDatagramEventSocket (VirtualNetwork vnet, IPAddress publicIPAddress)
		{
			if (vnet == null)
				throw new ArgumentNullException ();
			_vnet = vnet;
			_pubIP = publicIPAddress;
		}

		internal VirtualNetwork.VirtualNetworkNode VirtualNetworkNodeInfo {
			get { return _vnet_node; }
		}

		public VirtualNetwork VirtualNetwork {
			get { return _vnet; }
		}

		public IPAddress PublicIPAddress {
			get { return _pubIP; }
		}

		public EndPoint BindedPublicEndPoint {
			get { return _bindPubEP; }
		}

		#region IDatagramEventSocket Members

		public void Bind (EndPoint bindEP)
		{
			_bindPubEP = new IPEndPoint (_pubIP, ((IPEndPoint)bindEP).Port);
			_vnet_node = _vnet.AddVirtualNode (this, _bindPubEP);
		}

		public void Close ()
		{
			if (_vnet != null) {
				_vnet.RemoveVirtualNode (_vnet_node);
				_vnet_node = null;
				_vnet = null;
				_pubIP = IPAddress.None;
			}
		}

		public void SendTo (byte[] buffer, EndPoint remoteEP)
		{
			SendTo (buffer, 0, buffer.Length, remoteEP);
		}

		public void SendTo (byte[] buffer, int offset, int size, EndPoint remoteEP)
		{
			if (_vnet == null)
				return;
			if (size > MaxDatagramSize)
				throw new System.Net.Sockets.SocketException ();
			_vnet.AddSendQueue (_bindPubEP, remoteEP, buffer, offset, size);
			Interlocked.Add (ref _sentBytes, size);
			Interlocked.Increment (ref _sentDgrams);
		}

		public event DatagramReceiveEventHandler Received;

		internal void InvokeReceivedEvent (object sender, DatagramReceiveEventArgs e)
		{
			Interlocked.Add (ref _recvBytes, e.Size);
			Interlocked.Increment (ref _recvDgrams);
			if (Received != null) {
				try {
					Received (sender, e);
				} catch {}
			}
		}

		public int MaxDatagramSize {
			get { return 1000; }
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
