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
using System.Net.Sockets;
using System.Threading;

namespace p2pncs.Net
{
	public class UdpSocket : IDatagramEventSocket
	{
		const int MAX_DATAGRAM_SIZE = 1000;
		Socket _sock;
		IPAddress _receiveAdrs;
		long _recvBytes = 0, _sentBytes = 0, _recvDgrams = 0, _sentDgrams = 0;

		static Thread _receiveThread = null;
		static Dictionary<Socket, UdpSocket> _socketMap = new Dictionary<Socket, UdpSocket> ();
		static List<Socket> _sockets = new List<Socket> ();

		#region Constructor
		UdpSocket (AddressFamily addressFamily)
		{
			_receiveAdrs = (addressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any);
			_sock = new Socket (addressFamily, SocketType.Dgram, ProtocolType.Udp);

			lock (_sockets) {
				_sockets.Add (_sock);
				_socketMap.Add (_sock, this);
				if (_receiveThread == null) {
					_receiveThread = new Thread (ReceiveThread);
					_receiveThread.Name = "UdpSocket.ReceiveThread";
					_receiveThread.Start ();
				}
			}
		}
		public static UdpSocket CreateIPv4 ()
		{
			return new UdpSocket (AddressFamily.InterNetwork);
		}
		public static UdpSocket CreateIPv6 ()
		{
			return new UdpSocket (AddressFamily.InterNetworkV6);
		}
		#endregion

		#region Receive Process
		static void ReceiveThread ()
		{
			List<Socket> list = new List<Socket> ();
			byte[] recvBuffer = new byte[MAX_DATAGRAM_SIZE];
			while (true) {
				list.Clear ();
				lock (_sockets) {
					if (_sockets.Count == 0) {
						_receiveThread = null;
						return;
					}
					list.AddRange (_sockets);
				}
				Socket.Select (list, null, null, 1 * 1000000);
				lock (_sockets) {
					for (int i = 0; i < list.Count; i ++) {
						UdpSocket usock;
						if (!_socketMap.TryGetValue (list[i], out usock)) {
							continue;
						}
						if (usock.Received == null) {
							continue;
						}
						try {
							EndPoint remoteEP = new IPEndPoint (usock._receiveAdrs, 0);
							int receiveSize = usock._sock.ReceiveFrom (recvBuffer, 0, recvBuffer.Length, SocketFlags.None, ref remoteEP);
							usock._recvBytes += receiveSize;
							usock._recvDgrams ++;
							byte[] recvData = new byte[receiveSize];
							Buffer.BlockCopy (recvBuffer, 0, recvData, 0, recvData.Length);
							DatagramReceiveEventArgs e = new DatagramReceiveEventArgs (recvData, receiveSize, remoteEP);
							ThreadPool.QueueUserWorkItem (new InvokeHelper (usock, e).Invoke, null);
						} catch {}
					}
				}
			}
		}
		class InvokeHelper
		{
			UdpSocket _sock;
			DatagramReceiveEventArgs _e;

			public InvokeHelper (UdpSocket sock, DatagramReceiveEventArgs e)
			{
				_sock = sock;
				_e = e;
			}

			public void Invoke (object o)
			{
				try {
					_sock.Received (_sock, _e);
				} catch {}
			}
		}
		#endregion

		#region IDatagramEventSocket Members

		public void Bind (EndPoint bindEP)
		{
			_sock.Bind (bindEP);
		}

		public void Close ()
		{
			lock (_sockets) {
				_sockets.Remove (_sock);
				_socketMap.Remove (_sock);
			}
			try {
				_sock.Close ();
			} catch {}
		}

		public void SendTo (byte[] buffer, EndPoint remoteEP)
		{
			SendTo (buffer, 0, buffer.Length, remoteEP);
		}

		public void SendTo (byte[] buffer, int offset, int size, EndPoint remoteEP)
		{
			if (size > MAX_DATAGRAM_SIZE) {
				Logger.Log (LogLevel.Fatal, this, "Send data-size is too big. ({0} bytes)", size);
				throw new SocketException ();
			}
			if (!_sock.Poll (-1, SelectMode.SelectWrite))
				throw new Exception ("Polling failed");
			int ret = _sock.SendTo (buffer, offset, size, SocketFlags.None, remoteEP);
			if (ret != size) {
				Logger.Log (LogLevel.Fatal, this, "Sent size is unexpected. except={0}, actual={1}", size, ret);
				throw new Exception ("Sent size is unexpected");
			}
			Interlocked.Add (ref _sentBytes, size);
			Interlocked.Increment (ref _sentDgrams);
		}

		public event DatagramReceiveEventHandler Received;

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
