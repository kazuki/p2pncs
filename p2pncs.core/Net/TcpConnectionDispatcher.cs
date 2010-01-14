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
using System.Net.Sockets;
using System.Threading;
using p2pncs.Threading;

namespace p2pncs.Net
{
	public class TcpConnectionDispatcher : IConnectionDispatcher
	{
		Dictionary<Type, EventHandler<AcceptedEventArgs>> _handlers = new Dictionary<Type, EventHandler<AcceptedEventArgs>> ();
		bool _active = false;
		Socket _listener;
		Thread _recvThread;
		long _recvBytes = 0, _sentBytes = 0;
		object _lock = new object (); // _recvWaits, _sockMap用のロックオブジェクト
		List<Socket> _recvWaits = new List<Socket> ();
		Dictionary<Socket, TcpSocket> _sockMap = new Dictionary<Socket, TcpSocket> ();
		const int DefaultAllocSize = 1024 * 64; // 64KB
		const int MaxFirstMessageSize = MaxMessageSize;
		const int MaxMessageSize = 1024 * 1024; // 1MB

		public TcpConnectionDispatcher ()
		{
			_listener = new Socket (AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			_recvWaits.Add (_listener);
		}

		#region IConnectionDispatcher Members

		public void Bind (EndPoint bindEP)
		{
			_listener.Bind (bindEP);
		}

		public void ListenStart ()
		{
			if (_active)
				return;
			_active = true;
			_recvThread = ThreadTracer.CreateThread (RecvThread, "TcpConnectionDispatcher ReceiveThread");
			_listener.Listen (16);
			_recvThread.Start ();
		}

		public void Register (Type firstMessageType, EventHandler<AcceptedEventArgs> handler)
		{
			lock (_handlers) {
				_handlers.Add (firstMessageType, handler);
			}
		}

		public void Unregister (Type firstMessageType)
		{
			lock (_handlers) {
				_handlers.Remove (firstMessageType);
			}
		}

		public IAsyncResult BeginConnect (EndPoint remoteEP, AsyncCallback callback, object state)
		{
			Socket sock = new Socket (AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			IAsyncResult baseAR = sock.BeginConnect (remoteEP, null, state);
			return new AsyncConnectInfo (sock, baseAR, callback);
		}

		public ISocket EndConnect (IAsyncResult ar)
		{
			AsyncConnectInfo info = (AsyncConnectInfo)ar;
			Socket raw_sock = info.EndConnect ();
			TcpSocket sock = new TcpSocket (this, raw_sock);
			sock.IsFirstMessage = false;
			AddSocket (sock);
			return sock;
		}

		#endregion		

		void RecvThread ()
		{
			List<Socket> list = new List<Socket> ();
			while (_active) {
				try {
					list.Clear ();
					lock (_lock) {
						list.AddRange (_recvWaits);
					}
					Socket.Select (list, null, null, 50000 /* 50ms */);
					for (int i = 0; i < list.Count; i ++) {
						try {
							if (list[i] == _listener) {
								Socket client = _listener.Accept ();
								AddSocket (new TcpSocket (this, client));
							} else {
								TcpSocket info;
								lock (_lock) {
									if (!_sockMap.TryGetValue (list[i], out info))
										continue;
								}
								int size = (info.NextMessageSize == -1 ? 4 : info.NextMessageSize);
								int ret = info.Socket.Receive (info.Buffer, info.BufferFilled, size - info.BufferFilled, SocketFlags.None);
								if (ret <= 0)
									throw new SocketException ();
								Interlocked.Add (ref _recvBytes, ret);
								info.BufferFilled += ret;
								if (info.BufferFilled == size) {
									if (info.NextMessageSize == -1) {
										int maxMsgSize = (info.IsFirstMessage ? MaxFirstMessageSize : MaxMessageSize);
										info.NextMessageSize = (info.Buffer[0] << 24) | (info.Buffer[1] << 16) | (info.Buffer[2] << 8) | info.Buffer[3];
										if (info.NextMessageSize > maxMsgSize)
											throw new OutOfMemoryException ();
										if (info.Buffer.Length < info.NextMessageSize)
											Array.Resize<byte> (ref info.Buffer, info.NextMessageSize);
									} else {
										object msg = Serializer.Instance.Deserialize (info.Buffer);
										if (info.IsFirstMessage) {
											info.IsFirstMessage = false;
											EventHandler<AcceptedEventArgs> handler;
											lock (_handlers) {
												handler = _handlers[msg.GetType ()];
											}
											RemoveSocket (info.Socket);
											ThreadTracer.QueueToThreadPool (delegate (object o) {
												handler (this, new AcceptedEventArgs (info, msg));
												if (info.IsActive)
													AddSocket (info);
											}, "TCP: Handling FirstMessage (" + msg.GetType ().ToString () + ")");
										} else {
											info.ReceiveMessage (msg);
										}
										info.NextMessageSize = -1;
									}
									info.BufferFilled = 0;
								}
							}
						} catch {
							if (list[i] != _listener) {
								try {
									list[i].Close ();
								} catch {}
								RemoveSocket (list[i]);
							}
						}
					}
				} catch {}
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
				lock (_handlers) {
					_handlers.Clear ();
				}
				try {
					_listener.Close ();
				} catch {}
			}
		}

		void AddSocket (TcpSocket sock)
		{
			lock (_lock) {
				_sockMap.Add (sock.Socket, sock);
				_recvWaits.Add (sock.Socket);
			}
		}

		void RemoveSocket (Socket sock)
		{
			lock (_lock) {
				_sockMap.Remove (sock);
				_recvWaits.Remove (sock);
			}
		}

		class TcpSocket : ISocket
		{
			public Socket Socket;
			public bool IsFirstMessage = true;
			public int NextMessageSize = -1;
			public byte[] Buffer = new byte[DefaultAllocSize];
			public int BufferFilled = 0;

			TcpConnectionDispatcher _dispatcher;
			bool _active = true;
			EventHandlers<Type, ReceivedEventArgs> _received = new EventHandlers<Type, ReceivedEventArgs> ();

			public TcpSocket (TcpConnectionDispatcher parent, Socket sock)
			{
				_dispatcher = parent;
				Socket = sock;
			}

			public void ReceiveMessage (object msg)
			{
				Type type = msg.GetType ();
				ThreadTracer.QueueToThreadPool (delegate (object o) {
					_received.Invoke (type, this, new ReceivedEventArgs (msg, null));
				}, "TCP: Handling " + type.ToString ());
			}

			public bool IsActive {
				get { return _active; }
			}

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
				byte[] raw_firstMsg = Serializer.Instance.Serialize (message);
				byte[] data = new byte[4 + raw_firstMsg.Length];
				data[0] = (byte)(raw_firstMsg.Length >> 24);
				data[1] = (byte)(raw_firstMsg.Length >> 16);
				data[2] = (byte)(raw_firstMsg.Length >> 8);
				data[3] = (byte)(raw_firstMsg.Length);
				System.Buffer.BlockCopy (raw_firstMsg, 0, data, 4, raw_firstMsg.Length);
				int sent = 0;
				lock (Socket) {
					while (sent < data.Length) {
						if (!Socket.Poll (-1, System.Net.Sockets.SelectMode.SelectWrite))
							throw new SocketException ();
						sent += Socket.Send (data, sent, data.Length - sent, System.Net.Sockets.SocketFlags.None);
					}
				}
				Interlocked.Add (ref _dispatcher._sentBytes, sent);
			}

			public void SendTo (object message, EndPoint remoteEP)
			{
				throw new NotSupportedException ();
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
				try {
					Socket.Shutdown (SocketShutdown.Both);
					Socket.Close ();
				} catch {}
				_dispatcher.RemoveSocket (Socket);
			}

			public EndPoint LocalEndPoint {
				get { return Socket.LocalEndPoint; }
			}

			public EndPoint RemoteEndPoint {
				get { return Socket.RemoteEndPoint; }
			}

			#endregion

			#region IDisposable Members

			public void Dispose ()
			{
				Close ();
			}

			#endregion
		}

		class AsyncConnectInfo : IAsyncResult
		{
			Socket _sock;
			IAsyncResult _base;
			AsyncCallback _callback;

			public AsyncConnectInfo (Socket sock, IAsyncResult ar, AsyncCallback callback)
			{
				_sock = sock;
				_base = ar;
				_callback = callback;
			}

			public Socket EndConnect ()
			{
				try {
					_sock.EndConnect (_base);
				} catch {
					_sock.Close ();
					throw;
				}
				return _sock;
			}

			#region IAsyncResult Members

			public object AsyncState {
				get { return _base.AsyncState; }
			}

			public WaitHandle AsyncWaitHandle {
				get { return _base.AsyncWaitHandle; }
			}

			public bool CompletedSynchronously {
				get { return _base.CompletedSynchronously; }
			}

			public bool IsCompleted {
				get { return _base.IsCompleted; }
			}

			#endregion
		}
	}
}
