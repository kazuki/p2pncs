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
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using p2pncs.Utility;
using p2pncs.Threading;

namespace p2pncs.Net
{
	public class UdpSocket : IDatagramEventSocket
	{
		const int MAX_DATAGRAM_SIZE = 1000;
		int _max_datagram_size, _header_size;
		Socket _sock;
		IPAddress _receiveAdrs, _loopbackAdrs, _noneAdrs;
		ushort _bindPort;
		long _recvBytes = 0, _sentBytes = 0, _recvDgrams = 0, _sentDgrams = 0;
		IPublicIPAddressVotingBox _pubIpVotingBox;
		byte[] _sendBuffer = new byte[MAX_DATAGRAM_SIZE];

		static Thread _receiveThread = null;
		static Dictionary<Socket, UdpSocket> _socketMap = new Dictionary<Socket, UdpSocket> ();
		static List<Socket> _sockets = new List<Socket> ();

		#region Constructor
		UdpSocket (AddressFamily addressFamily)
		{
			_receiveAdrs = IPAddressUtility.GetAnyAddress (addressFamily);
			_pubIpVotingBox = new SimplePublicIPAddressVotingBox (addressFamily);
			_loopbackAdrs = IPAddressUtility.GetLoopbackAddress (addressFamily);
			_noneAdrs = IPAddressUtility.GetNoneAddress (addressFamily);
			_sock = new Socket (addressFamily, SocketType.Dgram, ProtocolType.Udp);
			_header_size = (addressFamily == AddressFamily.InterNetwork ? 4 : 16) + 2;
			_max_datagram_size = MAX_DATAGRAM_SIZE - _header_size;

			lock (_sockets) {
				_sockets.Add (_sock);
				_socketMap.Add (_sock, this);
				if (_receiveThread == null) {
					_receiveThread = ThreadTracer.CreateThread (ReceiveThread, "UdpSocket.ReceiveThread");
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
							IPEndPoint remoteIPEP = (IPEndPoint)remoteEP;
#if !DEBUG
							if (remoteIPEP.Port == usock._bindPort && usock._loopbackAdrs.Equals (remoteIPEP.Address)) {
								IPAddress new_adrs = usock._pubIpVotingBox.CurrentPublicIPAddress;
								if (usock._noneAdrs.Equals (new_adrs))
									continue; // パブリックIPが決定していないときはそのパケットを破棄
								remoteEP = new IPEndPoint (new_adrs, remoteIPEP.Port);
							}
							else if (IPAddressUtility.IsPrivate (remoteIPEP.Address)) {
								// リリースビルド時はプライベートアドレスからのパケットを破棄
								continue;
							}
#endif
							usock._recvBytes += receiveSize;
							usock._recvDgrams ++;
							ushort ver = (ushort)((recvBuffer[0] << 8) | recvBuffer[1]);
							if (ver != ProtocolVersion.Version)
								continue; // drop
							byte[] recvData = new byte[receiveSize - usock._header_size];
							Buffer.BlockCopy (recvBuffer, usock._header_size, recvData, 0, recvData.Length);
							IPAddress adrs = new IPAddress (recvBuffer.CopyRange (2, usock._header_size - 2));
							usock._pubIpVotingBox.Vote ((IPEndPoint)remoteEP, adrs);
							DatagramReceiveEventArgs e = new DatagramReceiveEventArgs (recvData, recvData.Length, remoteEP);
							ThreadTracer.QueueToThreadPool (new InvokeHelper (usock, e).Invoke, "Handling Received UDP Datagram");
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
			_bindPort = (ushort)((IPEndPoint)bindEP).Port;
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
			if (size > _max_datagram_size) {
				Logger.Log (LogLevel.Fatal, this, "Send data-size is too big. ({0} bytes)", size);
				throw new SocketException ();
			}
			IPEndPoint remoteIPEP = (IPEndPoint)remoteEP;
			if (remoteIPEP.Port == _bindPort && remoteIPEP.Address.Equals (_pubIpVotingBox.CurrentPublicIPAddress)) {
				remoteEP = new IPEndPoint (_loopbackAdrs, _bindPort);
			}
#if !DEBUG
			else if (IPAddressUtility.IsPrivate (remoteIPEP.Address)) {
				// リリースビルド時には、プライベートアドレス宛のパケットを破棄する
				throw new Exception ("Destination address is private");
			}
#endif
			if (!_sock.Poll (-1, SelectMode.SelectWrite))
				throw new Exception ("Polling failed");
			int ret;
			byte[] adrs_bytes = ((IPEndPoint)remoteEP).Address.GetAddressBytes ();
			lock (_sendBuffer) {
				_sendBuffer[0] = (byte)(ProtocolVersion.Version >> 8);
				_sendBuffer[1] = (byte)(ProtocolVersion.Version & 0xFF);
				Buffer.BlockCopy (adrs_bytes, 0, _sendBuffer, 2, adrs_bytes.Length);
				Buffer.BlockCopy (buffer, offset, _sendBuffer, _header_size, size);
				ret = _sock.SendTo (_sendBuffer, 0, _header_size + size, SocketFlags.None, remoteEP) - _header_size;
			}
			if (ret != size) {
				Logger.Log (LogLevel.Fatal, this, "Sent size is unexpected. except={0}, actual={1}", size, ret);
				throw new Exception ("Sent size is unexpected");
			}
			Interlocked.Add (ref _sentBytes, size);
			Interlocked.Increment (ref _sentDgrams);
		}

		public event DatagramReceiveEventHandler Received;

		public int MaxDatagramSize {
			get { return _max_datagram_size; }
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

		#region Properties
		public IPAddress CurrentPublicIPAddress {
			get { return _pubIpVotingBox.CurrentPublicIPAddress; }
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
