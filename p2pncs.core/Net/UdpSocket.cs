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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace p2pncs.Net
{
	public class UdpSocket : ISocket
	{
		int _max_datagram_size, _header_size;
		Socket _sock;
		IPAddress _loopbackAdrs, _noneAdrs;
		IPEndPoint _localEP;
		long _recvBytes = 0, _sentBytes = 0, _recvDgrams = 0, _sentDgrams = 0;
		IPublicIPAddressVotingBox _pubIpVotingBox;
		byte[] _sendBuffer = new byte[ConstantParameters.MaxUdpDatagramSize];
		Thread _recvThread;
		bool _active = true;
		EventHandlers<Type, ReceivedEventArgs> _received = new EventHandlers<Type,ReceivedEventArgs> ();

		#region Constructor
		UdpSocket (AddressFamily addressFamily)
		{
			_pubIpVotingBox = new SimplePublicIPAddressVotingBox (addressFamily);
			_loopbackAdrs = IPAddressUtility.GetLoopbackAddress (addressFamily);
			_noneAdrs = IPAddressUtility.GetNoneAddress (addressFamily);
			_sock = new Socket (addressFamily, SocketType.Dgram, ProtocolType.Udp);
			_header_size = (addressFamily == AddressFamily.InterNetwork ? 4 : 16) + 2;
			_max_datagram_size = ConstantParameters.MaxUdpDatagramSize - _header_size;
			_recvThread = new Thread (ReceiveThread);
			_recvThread.Start ();
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

		#region ISocket Members

#pragma warning disable 67
		public event EventHandler<AcceptingEventHandler> Accepting;
		public event EventHandler<AcceptedEventHandler> Accepted;
#pragma warning restore 67

		public void Bind (EndPoint localEP)
		{
			if (!(localEP is IPEndPoint))
				throw new ArgumentException ();
			_localEP = localEP as IPEndPoint;
			_sock.Bind (localEP);
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
			int bytes;
			byte[] sendBuffer = _sendBuffer;
			lock (_sendBuffer) {
				using (MemoryStream ms = new MemoryStream (sendBuffer, _header_size, _sendBuffer.Length - _header_size, true)) {
					Serializer.Instance.Serialize (ms, message);
					bytes = (int)ms.Position;
				}

				IPEndPoint remoteIPEP = (IPEndPoint)remoteEP;
				if (remoteIPEP.Port == _localEP.Port && remoteIPEP.Address.Equals (_pubIpVotingBox.CurrentPublicIPAddress)) {
					// 自分自身が宛先の場合はループバックアドレスに書き換える
					remoteEP = new IPEndPoint (_loopbackAdrs, _localEP.Port);
				}
#if !DEBUG
				else if (IPAddressUtility.IsPrivate (remoteIPEP.Address)) {
					// リリースビルド時には、プライベートアドレス宛のパケットを破棄する
					throw new Exception ("Destination address is private");
				}
#endif

				sendBuffer[0] = (byte)(ConstantParameters.ProtocolVersion >> 8);
				sendBuffer[1] = (byte)(ConstantParameters.ProtocolVersion & 0xFF);
				byte[] adrs_bytes = ((IPEndPoint)remoteEP).Address.GetAddressBytes ();
				Buffer.BlockCopy (adrs_bytes, 0, sendBuffer, 2, adrs_bytes.Length);

				if (!_sock.Poll (-1, SelectMode.SelectWrite))
					throw new Exception ("Polling failed");
				int ret = _sock.SendTo (_sendBuffer, 0, _header_size + bytes, SocketFlags.None, remoteEP) - _header_size;
				if (ret != bytes) {
					Logger.Log (LogLevel.Fatal, this, "Sent size is unexpected. except={0}, actual={1}", bytes, ret);
					throw new Exception ("Sent size is unexpected");
				}

				_sentBytes += bytes;
				_sentDgrams ++;
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
			_sock.Close ();

			_received.Clear ();
			_sock = null;
			_pubIpVotingBox = null;
			_sendBuffer = null;
			_noneAdrs = _loopbackAdrs = null;
		}

		public EndPoint LocalEndPoint {
			get { return _localEP; }
		}

		public EndPoint RemoteEndPoint {
			get { throw new SocketException (); }
		}

		#endregion

		#region Receive Thread
		void ReceiveThread ()
		{
			byte[] recvIpBuf = new byte[_loopbackAdrs.AddressFamily == AddressFamily.InterNetwork ? 4 : 16];
			byte[] recvBuffer = new byte[ConstantParameters.MaxUdpDatagramSize];

			EndPoint remoteEP = new IPEndPoint (_loopbackAdrs, 0);
			while (_active) {
				try {
					if (!_sock.Poll (-1, SelectMode.SelectRead)) {
						if (_active)
							Logger.Log (LogLevel.Fatal, this, "SelectRead Polling Failed");
						return;
					}
					if (!_active)
						return;
					int receiveSize = _sock.ReceiveFrom (recvBuffer, 0, recvBuffer.Length, SocketFlags.None, ref remoteEP);
					IPEndPoint remoteIPEP = (IPEndPoint)remoteEP;
#if !DEBUG
					if (remoteIPEP.Port == _localEP.Port && _loopbackAdrs.Equals (remoteIPEP.Address)) {
						IPAddress new_adrs = _pubIpVotingBox.CurrentPublicIPAddress;
						if (_noneAdrs.Equals (new_adrs))
							continue; // パブリックIPが決定していないときはそのパケットを破棄
						// ループバックからのメッセージを受信したらパブリックIPに書き換える
						remoteEP = new IPEndPoint (new_adrs, remoteIPEP.Port);
					} else if (IPAddressUtility.IsPrivate (remoteIPEP.Address)) {
						// プライベートアドレスからのパケットを破棄
						continue;
					}
#endif
					_recvBytes += receiveSize;
					_recvDgrams++;
					ushort ver = (ushort)((recvBuffer[0] << 8) | recvBuffer[1]);
					if (ver != ConstantParameters.ProtocolVersion)
						continue; // drop
					Buffer.BlockCopy (recvBuffer, 2, recvIpBuf, 0, recvIpBuf.Length);
					IPAddress adrs = new IPAddress (recvIpBuf);
					_pubIpVotingBox.Vote ((IPEndPoint)remoteEP, adrs);
					object obj = Serializer.Instance.Deserialize (recvBuffer, _header_size, receiveSize - _header_size);
					Received.Invoke (obj.GetType (), this, new ReceivedEventArgs (obj, remoteEP));
				} catch {}
			}
		}
		#endregion

		#region Properties
		public IPAddress CurrentPublicIPAddress {
			get { return _pubIpVotingBox.CurrentPublicIPAddress; }
		}

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

		#region IDisposable Members

		public void Dispose ()
		{
			Close ();
		}

		#endregion
	}
}
