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
using System.Linq;
using System.Text;
using p2pncs.Net;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using p2pncs.Threading;

namespace p2pncs.Simulation.VirtualNet
{
	public class VirtualTcpConnectionDispatcher : IConnectionDispatcher, VirtualNetwork.ISocketDeliver
	{
		VirtualNetwork _vnet;
		VirtualNetwork.VirtualNetworkNode _bindedVNode;
		IPAddress _pubIP;
		IPEndPoint _bindPubEP;
		bool _bypassSerializer;
		Dictionary<Type, EventHandler<AcceptedEventArgs>> _handlers = new Dictionary<Type, EventHandler<AcceptedEventArgs>> ();
		Dictionary<EndPoint, AcceptedTcpSocket> _acceptedSockets = new Dictionary<EndPoint, AcceptedTcpSocket> ();

		public VirtualTcpConnectionDispatcher (VirtualNetwork vnet, IPAddress publicIPAddress, bool bypassSerializer)
		{
			_vnet = vnet;
			_pubIP = publicIPAddress;
			_bypassSerializer = bypassSerializer;
		}

		#region IConnectionDispatcher Members

		public void Bind (EndPoint bindEP)
		{
			_bindPubEP = new IPEndPoint (_pubIP, ((IPEndPoint)bindEP).Port);
		}

		public void ListenStart ()
		{
			_bindedVNode = _vnet.AddVirtualNode (this, _bindPubEP);
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
			AsyncConnectInfo info = new AsyncConnectInfo (remoteEP, callback, state);
			if (callback != null) {
				ThreadTracer.QueueToThreadPool (delegate (object o) {
					callback (info);
				}, "VirtualTcpConnectionDispatcher.AsyncConnect");
			}
			return info;
		}

		public ISocket EndConnect (IAsyncResult ar)
		{
			AsyncConnectInfo info = (AsyncConnectInfo)ar;
			VirtualNetwork.VirtualNetworkNode node = _vnet.EstablishTcpConnection (info.RemoteEndPoint, _pubIP, delegate (VirtualNetwork.VirtualNetworkNode remoteNode) {
				return new InitiatorTcpSocket (_vnet, remoteNode);
			});
			InitiatorTcpSocket sock = node.Socket as InitiatorTcpSocket;
			sock.SetLocalNode (node);
			((VirtualTcpConnectionDispatcher)sock.RemoteNode.Socket).Establish (node.BindedPublicEndPoint);
			return sock;
		}

		void Establish (EndPoint remoteEP)
		{
			AcceptedTcpSocket sock = new AcceptedTcpSocket (this, _bindPubEP, remoteEP);
			lock (_acceptedSockets) {
				_acceptedSockets.Add (sock.RemoteEndPoint, sock);
			}
		}
		#endregion

		#region ISocketDeliver Members

		void VirtualNetwork.ISocketDeliver.Deliver (EndPoint remoteEP, object msg)
		{
			AcceptedTcpSocket sock;
			lock (_acceptedSockets) {
				_acceptedSockets.TryGetValue (remoteEP, out sock);
			}
			if (sock == null) {
				VirtualNetwork.VirtualNetworkNode remoteNode = _vnet.LookupVirtualNode (remoteEP);
				if (remoteNode == null)
					return;
				((InitiatorTcpSocket)remoteNode.Socket).ClosedByRemoteHost ();
				return;
			}
			sock.Deliver (remoteEP, msg);
		}

		void VirtualNetwork.ISocketDeliver.Deliver (EndPoint remoteEP, byte[] buf, int offset, int size)
		{
			(this as VirtualNetwork.ISocketDeliver).Deliver (remoteEP, Serializer.Instance.Deserialize (buf, offset, size));
		}

		void VirtualNetwork.ISocketDeliver.FailedDeliver (EndPoint remoteEP)
		{
			AcceptedTcpSocket sock;
			lock (_acceptedSockets) {
				_acceptedSockets.TryGetValue (remoteEP, out sock);
			}
			if (sock != null)
				sock.FailedDeliver (remoteEP);
		}

		#endregion

		#region IDisposable Members

		public void Dispose ()
		{
			VirtualNetwork.VirtualNetworkNode node = _bindedVNode;
			lock (this) {
				if (_bindedVNode == null)
					return;
				_bindedVNode = null;
			}
			_vnet.RemoveVirtualNode (node);
		}

		#endregion

		#region Internal Class
		class AsyncConnectInfo : IAsyncResult
		{
			EndPoint _ep;
			AsyncCallback _callback;
			object _state;
			ManualResetEvent _done = new ManualResetEvent (false);
			bool _completed = false;

			public AsyncConnectInfo (EndPoint remoteEP, AsyncCallback callback, object state)
			{
				_ep = remoteEP;
				_callback = callback;
				_state = state;
			}

			public EndPoint RemoteEndPoint {
				get { return _ep; }
			}

			#region IAsyncResult Members

			public object AsyncState {
				get { return _state; }
			}

			public WaitHandle AsyncWaitHandle {
				get { return _done; }
			}

			public bool CompletedSynchronously {
				get { return false; }
			}

			public bool IsCompleted {
				get { return _completed; }
			}

			#endregion
		}
		class InitiatorTcpSocket : ISocket, VirtualNetwork.ISocketDeliver
		{
			VirtualNetwork _vnet;
			VirtualNetwork.VirtualNetworkNode _remoteNode, _localNode;
			EventHandlers<Type, ReceivedEventArgs> _received = new EventHandlers<Type,ReceivedEventArgs> ();
			List<SequencedMessage> _recvBuffer = new List<SequencedMessage> ();
			int _nextRecvSeq = 1, _sendSeq = 0;
			bool _closed = false;

			public InitiatorTcpSocket (VirtualNetwork vnet, VirtualNetwork.VirtualNetworkNode remoteNode)
			{
				_vnet = vnet;
				_remoteNode = remoteNode;
			}

			public void SetLocalNode (VirtualNetwork.VirtualNetworkNode node)
			{
				_localNode = node;
			}

			public void ClosedByRemoteHost ()
			{
				_closed = true;
			}

			public VirtualNetwork.VirtualNetworkNode LocalNode {
				get { return _localNode; }
			}

			public VirtualNetwork.VirtualNetworkNode RemoteNode {
				get { return _remoteNode; }
			}

			#region ISocket Members

			public event EventHandler<AcceptingEventArgs> Accepting;

			public event EventHandler<AcceptedEventArgs> Accepted;

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
				if (_closed)
					throw new SocketException ();
				_vnet.AddSendQueue (_localNode.BindedPublicEndPoint, _remoteNode.BindedPublicEndPoint, new SequencedMessage (Interlocked.Increment (ref _sendSeq), message), true);
			}

			public void SendTo (object message, EndPoint remoteEP)
			{
				Send (message);
			}

			public EventHandlers<Type, ReceivedEventArgs> Received {
				get { return _received; }
			}

			public void Close ()
			{
				_vnet.RemoveVirtualNode (_localNode);
			}

			public EndPoint LocalEndPoint {
				get { return _localNode.BindedPublicEndPoint; }
			}

			public EndPoint RemoteEndPoint {
				get { return _remoteNode.BindedPublicEndPoint; }
			}

			#endregion

			#region IDisposable Members

			public void Dispose ()
			{
				Close ();
			}

			#endregion

			#region ISocketDeliver Members

			public void Deliver (EndPoint remoteEP, object msg)
			{
				SequencedMessage smsg = (SequencedMessage)msg;
				smsg.RemoteEndPoint = remoteEP;
				msg = smsg.Message;
				Type type = msg.GetType ();

				lock (_recvBuffer) {
					if (_nextRecvSeq > smsg.Sequence)
						return; // drop
					if (_nextRecvSeq != smsg.Sequence) {
						for (int i = 0; i < _recvBuffer.Count; i++)
							if (_recvBuffer[i].Sequence > smsg.Sequence) {
								_recvBuffer.Insert (i, smsg);
								return;
							}
						_recvBuffer.Add (smsg);
						return;
					}
					_nextRecvSeq++;
					_received.Invoke (type, this, new ReceivedEventArgs (msg, remoteEP));
				}

				while (true) {
					lock (_recvBuffer) {
						if (_recvBuffer.Count == 0)
							break;
						if (_recvBuffer[0].Sequence > _nextRecvSeq)
							return;
						if (_recvBuffer[0].Sequence < _nextRecvSeq) {
							_recvBuffer.RemoveAt (0);
							Console.WriteLine ("BUG: AcceptedTcpSocket.Deliver");
							continue;
						}
						smsg = _recvBuffer[0];
						_recvBuffer.RemoveAt (0);
						_nextRecvSeq++;
						_received.Invoke (smsg.Message.GetType (), this, new ReceivedEventArgs (smsg.Message, smsg.RemoteEndPoint));
					}
				}
			}

			public void Deliver (EndPoint remoteEP, byte[] buf, int offset, int size)
			{
				Deliver (remoteEP, Serializer.Instance.Deserialize (buf, offset, size));
			}

			public void FailedDeliver (EndPoint remoteEP)
			{
				ClosedByRemoteHost ();
			}

			#endregion
		}
		class AcceptedTcpSocket : ISocket, VirtualNetwork.ISocketDeliver
		{
			EndPoint _localEP, _remoteEP;
			EventHandlers<Type, ReceivedEventArgs> _received = new EventHandlers<Type,ReceivedEventArgs> ();
			VirtualTcpConnectionDispatcher _dispatcher;
			List<SequencedMessage> _recvBuffer = new List<SequencedMessage> ();

			object _lock = new object ();
			bool _waitingFirstMessage = true;
			bool _closed = false;

			int _nextRecvSeq = 1, _sendSeq = 0;

			public AcceptedTcpSocket (VirtualTcpConnectionDispatcher dispatcher, EndPoint localEP, EndPoint remoteEP)
			{
				_dispatcher = dispatcher;
				_localEP = localEP;
				_remoteEP = remoteEP;
			}

			#region ISocket Members

			public event EventHandler<AcceptingEventArgs> Accepting;

			public event EventHandler<AcceptedEventArgs> Accepted;

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
				if (_closed)
					throw new SocketException ();
				_dispatcher._vnet.AddSendQueue (_localEP, _remoteEP, new SequencedMessage (Interlocked.Increment (ref _sendSeq), message), true);
			}

			public void SendTo (object message, EndPoint remoteEP)
			{
				Send (message);
			}

			public EventHandlers<Type, ReceivedEventArgs> Received {
				get { return _received; }
			}

			public void Close ()
			{
				_closed = true;
				lock (_dispatcher._acceptedSockets) {
					_dispatcher._acceptedSockets.Remove (_remoteEP);
				}
			}

			public EndPoint LocalEndPoint {
				get { return _localEP; }
			}

			public EndPoint RemoteEndPoint {
				get { return _remoteEP; }
			}

			#endregion

			#region IDisposable Members

			public void Dispose ()
			{
				Close ();
			}

			#endregion

			#region ISocketDeliver Members

			public void Deliver (EndPoint remoteEP, object msg)
			{
				SequencedMessage smsg = (SequencedMessage)msg;
				smsg.RemoteEndPoint = remoteEP;
				msg = smsg.Message;
				Type type = msg.GetType ();

				lock (_recvBuffer) {
					if (_nextRecvSeq > smsg.Sequence)
						return; // drop
					if (_nextRecvSeq != smsg.Sequence) {
						for (int i = 0; i < _recvBuffer.Count; i ++)
							if (_recvBuffer[i].Sequence > smsg.Sequence) {
								_recvBuffer.Insert (i, smsg);
								return;
							}
						_recvBuffer.Add (smsg);
						return;
					}
					_nextRecvSeq ++;

					bool isFirstMsg;
					lock (_lock) {
						isFirstMsg = _waitingFirstMessage;
						_waitingFirstMessage = false;
						if (isFirstMsg) {
							EventHandler<AcceptedEventArgs> handler;
							_dispatcher._handlers.TryGetValue (type, out handler);
							if (handler == null) {
								Close ();
							} else {
								handler (_dispatcher, new AcceptedEventArgs (this, msg));
							}
							return;
						}
					}

					_received.Invoke (type, this, new ReceivedEventArgs (msg, remoteEP));
				}

				while (true) {
					lock (_recvBuffer) {
						if (_recvBuffer.Count == 0)
							break;
						if (_recvBuffer[0].Sequence > _nextRecvSeq)
							return;
						if (_recvBuffer[0].Sequence < _nextRecvSeq) {
							_recvBuffer.RemoveAt (0);
							Console.WriteLine ("BUG: AcceptedTcpSocket.Deliver");
							continue;
						}
						smsg = _recvBuffer[0];
						_recvBuffer.RemoveAt (0);
						_nextRecvSeq ++;
					}
					_received.Invoke (smsg.Message.GetType (), this, new ReceivedEventArgs (smsg.Message, smsg.RemoteEndPoint));
				}
			}

			public void Deliver (EndPoint remoteEP, byte[] buf, int offset, int size)
			{
				throw new NotSupportedException ();
			}

			public void FailedDeliver (EndPoint remoteEP)
			{
				_closed = true;
			}

			#endregion
		}
		class SequencedMessage
		{
			int _seq;
			object _msg;

			public SequencedMessage (int seq, object msg)
			{
				_seq = seq;
				_msg = msg;
			}

			public int Sequence {
				get { return _seq; }
			}

			public object Message {
				get { return _msg; }
			}

			public EndPoint RemoteEndPoint { get; set; }

			public override string ToString ()
			{
				return string.Format ("{0}: {1}", _seq, _msg);
			}
		}
		#endregion
	}
}
