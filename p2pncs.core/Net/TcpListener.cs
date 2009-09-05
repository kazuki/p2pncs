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
using p2pncs.Threading;
using Socket = System.Net.Sockets.Socket;
using SocketException = System.Net.Sockets.SocketException;

namespace p2pncs.Net
{
	public class TcpListener : ITcpListener
	{
		Dictionary<Type, EventHandler<TcpListenerAcceptedEventArgs>> _handlers = new Dictionary<Type,EventHandler<TcpListenerAcceptedEventArgs>> ();
		ReaderWriterLockWrapper _lock = new ReaderWriterLockWrapper ();
		bool _active = false;
		Socket _listener;
		Thread _recvThread;
		long _recvBytes = 0, _sentBytes = 0;
		ManualResetEvent _done = new ManualResetEvent (false);
		List<Socket> _recvWaits = new List<Socket> ();
		Dictionary<Socket, SocketInfo> _sockMap = new Dictionary<Socket,SocketInfo> ();
		const int MaxFirstMessageSize = 1024 * 1024; // 1MB

		public TcpListener ()
		{
			_listener = new Socket (System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
			_recvWaits.Add (_listener);
		}
		
		public void Bind (IPEndPoint bindEP)
		{
			_listener.Bind (bindEP);
		}

		public void ListenStart ()
		{
			if (_active)
				return;
			_active = true;
			_recvThread = ThreadTracer.CreateThread (RecvThread, "TcpListener ReceiveThread");
			_listener.Listen (16);
			_recvThread.Start ();
		}

		public void SendMessage (Socket sock, object msg)
		{
			byte[] raw_firstMsg = Serializer.Instance.Serialize (msg);
			byte[] data = new byte[4 + raw_firstMsg.Length];
			data[0] = (byte)(raw_firstMsg.Length >> 24);
			data[1] = (byte)(raw_firstMsg.Length >> 16);
			data[2] = (byte)(raw_firstMsg.Length >> 8);
			data[3] = (byte)(raw_firstMsg.Length);
			Buffer.BlockCopy (raw_firstMsg, 0, data, 4, raw_firstMsg.Length);
			int sent = 0;
			while (sent < data.Length) {
				if (!sock.Poll (-1, System.Net.Sockets.SelectMode.SelectWrite))
					throw new SocketException ();
				sent += sock.Send (data, sent, data.Length - sent, System.Net.Sockets.SocketFlags.None);
			}
			Interlocked.Add (ref _sentBytes, sent);
		}

		public object ReceiveMessage (Socket sock, int max_size)
		{
			byte[] data = new byte[4];
			int recv = 0;
			while (recv < data.Length) {
				if (!sock.Poll (-1, System.Net.Sockets.SelectMode.SelectRead))
					throw new SocketException ();
				int ret = sock.Receive (data, recv, data.Length - recv, System.Net.Sockets.SocketFlags.None);
				if (ret == 0)
					break;
				recv += ret;
			}
			if (recv != data.Length)
				throw new SocketException ();

			int size = (data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3];
			if (size > max_size)
				throw new OutOfMemoryException ();
			data = new byte[size];
			recv = 0;
			while (recv < data.Length) {
				if (!sock.Poll (-1, System.Net.Sockets.SelectMode.SelectRead))
					throw new SocketException ();
				int ret = sock.Receive (data, recv, data.Length - recv, System.Net.Sockets.SocketFlags.None);
				if (ret == 0)
					break;
				ret += ret;
			}
			if (recv != data.Length)
				throw new SocketException ();
			Interlocked.Add (ref _recvBytes, recv);
			return Serializer.Instance.Deserialize (data);
		}

		void RecvThread ()
		{
			List<Socket> list = new List<Socket> ();
			while (_active) {
				try {
					list.Clear ();
					list.AddRange (_recvWaits);
					Socket.Select (list, null, null, 50000 /* 50ms */);
					for (int i = 0; i < list.Count; i ++) {
						try {
							if (list[i] == _listener) {
								Socket client = _listener.Accept ();
								_recvWaits.Add (client);
								_sockMap.Add (client, new SocketInfo (client));
							} else {
								SocketInfo info = _sockMap[list[i]];
								int ret = info.Socket.Receive (info.Buffer, info.Received, info.Buffer.Length - info.Received, System.Net.Sockets.SocketFlags.None);
								if (ret == 0)
									throw new SocketException ();
								Interlocked.Add (ref _recvBytes, ret);
								info.Received += ret;
								if (info.Received == info.Buffer.Length) {
									if (info.FirstMessageSize == -1) {
										info.FirstMessageSize = (info.Buffer[0] << 24) | (info.Buffer[1] << 16) | (info.Buffer[2] << 8) | info.Buffer[3];
										if (info.FirstMessageSize > MaxFirstMessageSize)
											throw new OutOfMemoryException ();
										info.Buffer = new byte[info.FirstMessageSize];
										info.Received = 0;
									} else {
										object firstMsg = Serializer.Instance.Deserialize (info.Buffer);
										using (_lock.EnterReadLock ()) {
											_handlers[firstMsg.GetType ()].BeginInvoke (this, new TcpListenerAcceptedEventArgs (list[i], firstMsg), null, null);
										}
										_sockMap.Remove (list[i]);
										_recvWaits.Remove (list[i]);
									}
								}
							}
						} catch {
							if (list[i] != _listener) {
								try {
									list[i].Close ();
								} catch {}
								_sockMap.Remove (list[i]);
								_recvWaits.Remove (list[i]);
							}
						}
					}
				} catch {}
			}
		}

		public void RegisterAcceptHandler (Type firstMessageType, EventHandler<TcpListenerAcceptedEventArgs> handler)
		{
			using (_lock.EnterWriteLock ()) {
				_handlers.Add (firstMessageType, handler);
			}
		}

		public void UnregisterAcceptHandler (Type firstMessageType)
		{
			using (_lock.EnterWriteLock ()) {
				_handlers.Remove (firstMessageType);
			}
		}

		public long ReceivedBytes {
			get { return _recvBytes; }
		}

		public long SentBytes {
			get { return _sentBytes; }
		}

		public void Dispose ()
		{
			if (_active) {
				_active = false;
				using (_lock.EnterWriteLock ()) {
					_handlers.Clear ();
				}
				_lock.Dispose ();
				try {
					_listener.Close ();
				} catch { }
			}
		}

		class SocketInfo
		{
			public Socket Socket;
			public int FirstMessageSize;
			public byte[] Buffer;
			public int Received;

			public SocketInfo (Socket sock)
			{
				this.Socket = sock;
				FirstMessageSize = -1;
				Buffer = new byte[4];
				Received = 0;
			}
		}
	}
}
