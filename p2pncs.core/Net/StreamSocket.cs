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
using System.Net;
using System.Net.Sockets;
using System.Threading;
using p2pncs.Threading;

namespace p2pncs.Net
{
	public class StreamSocket : IStreamSocket
	{
		const int HeaderSize = 1/*type*/ + 4/*seq*/ + 2/*size*/;
		const int MinSegments = 8;
		const int MaxSegments = 512;
		const int MaxRetries = 8;
		bool _active = true, _shutdown = false;
		IDatagramEventSocket _sock;
		int _mss, _currentSegments = MinSegments;
		EndPoint _remoteEP;
		Packet[] _sendBuffer = new Packet[MaxSegments];
		int _sendBufferFilled = 0;
		uint _sendSequence = 0x01020304;
		AutoResetEvent _sendWaitHandle = new AutoResetEvent (true);
		ManualResetEvent _shutdownWaitHandle = new ManualResetEvent (false);
		TimeSpan _rto = TimeSpan.FromSeconds (2);
		IntervalInterrupter _timeoutCheckInt;
		object _lock = new object ();

		public StreamSocket (IDatagramEventSocket sock, EndPoint remoteEP, int max_datagram_size, IntervalInterrupter timeoutCheckInt)
		{
			_sock = sock;
			_mss = max_datagram_size - HeaderSize;
			_remoteEP = remoteEP;
			for (int i = 0; i < _sendBuffer.Length; i ++)
				_sendBuffer[i] = new Packet (max_datagram_size);
			_sock.Received += new DatagramReceiveEventHandler (Socket_Received);
			_timeoutCheckInt = timeoutCheckInt;
			_timeoutCheckInt.AddInterruption (CheckTimeout);
		}

		void CheckTimeout ()
		{
			if (!_active)
				return;

			lock (_lock) {
				bool hasTimeoutPacket = false;
				for (int i = 0; i < _sendBuffer.Length; i++) {
					if (_sendBuffer[i].State == PacketState.AckWaiting && _sendBuffer[i].RTO <= DateTime.Now) {
						if (_sendBuffer[i].Retries >= MaxRetries) {
							Dispose ();
							return;
						}
						_sendBuffer[i].RTO_Interval += _sendBuffer[i].RTO_Interval;
						_sendBuffer[i].RTO = DateTime.Now + _sendBuffer[i].RTO_Interval;
						_sendBuffer[i].Retries++;
						_sock.SendTo (_sendBuffer[i].Data, 0, _sendBuffer[i].Filled, _remoteEP);
						hasTimeoutPacket = true;
					}
				}
				if (hasTimeoutPacket) {
					_currentSegments /= 2;
					if (_currentSegments < MinSegments)
						_currentSegments = MinSegments;
				}
				if (_shutdown && _sendBufferFilled == 0)
					_shutdownWaitHandle.Set ();
			}
		}

		void Socket_Received (object sender, DatagramReceiveEventArgs e)
		{
			if (!_active)
				return;

			switch (e.Buffer[0]) {
				case 0: /* Packet */
					byte[] buf = new byte[5];
					buf[0] = 1;
					Buffer.BlockCopy (e.Buffer, 1, buf, 1, 4); // Copy sequence
					_sock.SendTo (buf, _remoteEP);
					break;
				case 1: /* ACK */
					uint seq = ((uint)e.Buffer[1] << 24) | ((uint)e.Buffer[2] << 16) | ((uint)e.Buffer[3] << 8) | ((uint)e.Buffer[4]);
					lock (_lock) {
						for (int i = 0; i < _sendBuffer.Length; i ++) {
							if (_sendBuffer[i].State == PacketState.AckWaiting && _sendBuffer[i].Sequence == seq) {
								_currentSegments += 8;
								_sendBuffer[i].Reset ();
								_sendBufferFilled--;
								_sendWaitHandle.Set ();
								break;
							}
						}
					}
					break;
			}
		}

		#region IStreamSocket Members

		public void Send (byte[] buffer, int offset, int size)
		{
			if (!_active || _shutdown)
				throw new SocketException ();

			while (size > 0) {
				int copy_size = Math.Min (_mss, size);
				if (!_sendWaitHandle.WaitOne ())
					throw new SocketException ();
				lock (_lock) {
					for (int i = 0; i < _sendBuffer.Length; i ++) {
						if (_sendBuffer[i].State != PacketState.Empty)
							continue;
						_sendBufferFilled ++;
						_sendBuffer[i].Setup (buffer, offset, copy_size, _sendSequence, DateTime.Now + _rto, _rto, PacketState.AckWaiting);
						_sock.SendTo (_sendBuffer[i].Data, 0, _sendBuffer[i].Filled, _remoteEP);
						_sendSequence += (uint)copy_size;
						offset += copy_size;
						size -= copy_size;
						break;
					}
					if (_sendBufferFilled < _currentSegments)
						_sendWaitHandle.Set ();
				}
			}
		}

		public int Receive (byte[] buffer, int offset, int size)
		{
			throw new NotImplementedException ();
		}

		public void Shutdown ()
		{
			if (_shutdown) return;
			if (!_active) throw new SocketException ();
			_shutdown = true;
			lock (_lock) {
				if (_sendBufferFilled == 0)
					_shutdownWaitHandle.Set ();
			}
			_shutdownWaitHandle.WaitOne ();
		}

		#endregion

		#region IDisposable Members

		public void Dispose ()
		{
			_active = false;
			_sock.Dispose ();
			_sendWaitHandle.Close ();
			_shutdownWaitHandle.Close ();
			_timeoutCheckInt.RemoveInterruption (CheckTimeout);
		}

		#endregion

		class Packet
		{
			public byte[] Data;
			public int Filled;
			public uint Sequence;
			public DateTime RTO;
			public TimeSpan RTO_Interval;
			public int Retries;
			public PacketState State;

			public Packet (int max_segment_size)
			{
				Data = new byte[max_segment_size];
				Reset ();
			}

			public void Reset ()
			{
				Setup (null, 0, 0, 0, DateTime.MaxValue, TimeSpan.MaxValue, PacketState.Empty);
			}

			public void Setup (byte[] src, int offset, int size, uint sequence, DateTime rto, TimeSpan rto_interval, PacketState state)
			{
				if (src != null) {
					Data[1] = (byte)(sequence >> 24);
					Data[2] = (byte)(sequence >> 16);
					Data[3] = (byte)(sequence >> 8);
					Data[4] = (byte)(sequence);
					Data[5] = (byte)(size >> 8);
					Data[6] = (byte)(size);
					Buffer.BlockCopy (src, offset, Data, HeaderSize, size);
					Filled = HeaderSize + size;
				} else {
					Filled = 0;
				}
				Sequence = sequence;
				RTO = rto;
				RTO_Interval = rto_interval;
				Retries = 0;
				State = state;
			}
		}

		enum PacketState : byte
		{
			Empty,
			AckWaiting
		}
	}
}
