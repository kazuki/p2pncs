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
using p2pncs.Threading;
using p2pncs.Utility;

namespace p2pncs.Net
{
	public class StreamSocket : IStreamSocket
	{
		const int HeaderSize = 1/*type*/ + 4/*seq*/ + 2/*size*/;
		const int MaxSegments = 32;
		const int MaxRetries = 8;
		static readonly TimeSpan TimeoutRTO = TimeSpan.FromSeconds (16);
		static readonly TimeSpan MaxRTO = TimeSpan.FromSeconds (8);
		bool _active = true, _shutdown = false;
		IDatagramEventSocket _sock;
		int _mss;
		EndPoint _remoteEP;
		Packet[] _sendBuffer = new Packet[MaxSegments];
		int _sendBufferFilled = 0;
		uint _sendIndex = 0, _sendSequence = 0, _recvNextSequence = 0;
		DateTime _lastReceived = DateTime.Now;
		List<ReceiveSegment> _recvBuffer = new List<ReceiveSegment> ();
		AutoResetEvent _sendWaitHandle = new AutoResetEvent (true);
		AutoResetEvent _recvWaitHandle = new AutoResetEvent (false);
		ManualResetEvent _shutdownWaitHandle = new ManualResetEvent (false);
		IntervalInterrupter _timeoutCheckInt;
		object _lock = new object ();
		int _srtt, _rttvar, _rto;

		public StreamSocket (IDatagramEventSocket sock, EndPoint remoteEP, int max_datagram_size, IntervalInterrupter timeoutCheckInt)
			: this (sock, remoteEP, max_datagram_size, TimeSpan.FromSeconds (3), timeoutCheckInt)
		{
		}

		public StreamSocket (IDatagramEventSocket sock, EndPoint remoteEP, int max_datagram_size, TimeSpan init_RTT, IntervalInterrupter timeoutCheckInt)
		{
			_sock = sock;
			_mss = max_datagram_size - HeaderSize;
			_remoteEP = remoteEP;
			for (int i = 0; i < _sendBuffer.Length; i ++)
				_sendBuffer[i] = new Packet (max_datagram_size);
			_sock.Received += new DatagramReceiveEventHandler (Socket_Received);
			_timeoutCheckInt = timeoutCheckInt;
			_timeoutCheckInt.AddInterruption (CheckTimeout);

			_srtt = (int)init_RTT.TotalMilliseconds;
			_rttvar = _srtt / 2;
			_rto = _srtt + 4 * _rttvar;
		}

		void Update_RTO (float r)
		{
			// RFC 2988 (実験的に導入)
			const float alpha = 1.0f / 8.0f;
			const float beta = 1.0f / 4.0f;
			_rttvar = (int)((1 - beta) * _rttvar + beta * Math.Abs (_srtt - r));
			_srtt = (int)((1 - alpha) * _srtt + alpha * r);
			_rto = (int)(_srtt + 4 * _rttvar);
		}

		void CheckTimeout ()
		{
			lock (_lock) {
				for (int i = 0; i < _sendBuffer.Length; i++) {
					if (_sendBuffer[i].State == PacketState.AckWaiting && _sendBuffer[i].RTO <= DateTime.Now) {
						if (_sendBuffer[i].Retries >= MaxRetries) {
							Dispose ();
							return;
						}
						_sendBuffer[i].RTO_Interval = TimeSpan.FromMilliseconds (_sendBuffer[i].RTO_Interval.TotalMilliseconds * 1.3);
						if (_sendBuffer[i].RTO_Interval > MaxRTO)
							_sendBuffer[i].RTO_Interval = MaxRTO;
						_sendBuffer[i].RTO = DateTime.Now + _sendBuffer[i].RTO_Interval;
						_sendBuffer[i].Retries ++;
						_sock.SendTo (_sendBuffer[i].Data, 0, _sendBuffer[i].Filled, _remoteEP);
					}
				}
				if (_shutdown && _sendBufferFilled == 0)
					_shutdownWaitHandle.Set ();
			}
		}

		void Socket_Received (object sender, DatagramReceiveEventArgs e)
		{
			switch (e.Buffer[0]) {
				case 0: /* Packet */
					byte[] buf = new byte[5];
					buf[0] = 1;
					Buffer.BlockCopy (e.Buffer, 1, buf, 1, buf.Length - 1); // Copy sequence
					uint rseq = ((uint)buf[1] << 24) | ((uint)buf[2] << 16) | ((uint)buf[3] << 8) | ((uint)buf[4]);
					_sock.SendTo (buf, _remoteEP);
					lock (_recvBuffer) {
						_lastReceived = DateTime.Now;
						_recvBuffer.Add (new ReceiveSegment (rseq, e.Buffer.CopyRange (HeaderSize, e.Size - HeaderSize)));
						_recvWaitHandle.Set ();
					}
					break;
				case 1: /* ACK */
					uint seq = ((uint)e.Buffer[1] << 24) | ((uint)e.Buffer[2] << 16) | ((uint)e.Buffer[3] << 8) | ((uint)e.Buffer[4]);
					lock (_lock) {
						uint lowest_sendidx = uint.MaxValue, acked_sendidx = 0;
						int lowest_idx = int.MaxValue, acked_idx = -1;
						for (int i = 0; i < _sendBuffer.Length; i ++) {
							if (_sendBuffer[i].State == PacketState.AckWaiting && _sendBuffer[i].Sequence < seq) {
								if (lowest_sendidx > _sendBuffer[i].RetransmitIndex) {
									lowest_sendidx = _sendBuffer[i].RetransmitIndex;
									lowest_idx = i;
								}
							}
						}
						for (int i = 0; i < _sendBuffer.Length; i ++) {
							if (_sendBuffer[i].State == PacketState.AckWaiting && _sendBuffer[i].Sequence == seq) {
								if (_sendBuffer[i].Retries == 0)
									Update_RTO ((float)(DateTime.Now - _sendBuffer[i].Start).TotalMilliseconds);
								acked_idx = i;
								acked_sendidx = _sendBuffer[i].RetransmitIndex;
								_sendBuffer[i].Reset ();
								_sendBufferFilled--;
								_sendWaitHandle.Set ();
								break;
							}
						}
						if (lowest_idx != int.MaxValue && acked_idx >= 0 && acked_sendidx - lowest_sendidx >= 3) {
							Packet p = _sendBuffer[lowest_idx];
							if (p.RTO - p.RTO_Interval + TimeSpan.FromMilliseconds (_rto) <= DateTime.Now) {
								p.RTO = DateTime.Now;
								p.RetransmitIndex = acked_sendidx;
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
				if (!_sendWaitHandle.WaitOne () || !_active)
					throw new SocketException ();
				lock (_lock) {
					for (int i = 0; i < _sendBuffer.Length; i ++) {
						if (_sendBuffer[i].State != PacketState.Empty)
							continue;
						_sendBufferFilled ++;
						_sendBuffer[i].Setup (buffer, offset, copy_size, _sendSequence, _sendIndex, TimeSpan.FromMilliseconds (_rto), PacketState.AckWaiting);
						_sock.SendTo (_sendBuffer[i].Data, 0, _sendBuffer[i].Filled, _remoteEP);
						_sendSequence += (uint)copy_size;
						_sendIndex ++;
						offset += copy_size;
						size -= copy_size;
						break;
					}
					if (_sendBufferFilled < MaxSegments)
						_sendWaitHandle.Set ();
				}
			}
		}

		public int Receive (byte[] buffer, int offset, int size)
		{
			while (true) {
				ReceiveSegment seg = null;
				lock (_recvBuffer) {
					for (int i = 0; i < _recvBuffer.Count; i ++) {
						if (_recvBuffer[i].Sequence == _recvNextSequence) {
							seg = _recvBuffer[i];
							if (seg.Count <= size) {
								_recvBuffer.RemoveAt (i);
								_recvNextSequence = seg.NextSequence;
								break;
							} else {
								Buffer.BlockCopy (seg.Array, seg.Offset, buffer, offset, size);
								seg.Offset += size;
								seg.Count -= size;
								return size;
							}
						}
					}
				}
				if (seg == null) {
					if (!_recvWaitHandle.WaitOne (TimeSpan.FromSeconds (1)) && DateTime.Now.Subtract (_lastReceived) >= TimeoutRTO)
						throw new SocketException ();
					continue;
				}
				Buffer.BlockCopy (seg.Array, seg.Offset, buffer, offset, seg.Count);
				return seg.Count;
			}
		}

		public int CheckAvailableBytes ()
		{
			lock (_recvBuffer) {
				for (int i = 0; i < _recvBuffer.Count; i++) {
					if (_recvBuffer[i].Sequence == _recvNextSequence)
						return _recvBuffer[i].Count;
				}
			}
			return 0;
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
			if (_active) {
				_active = false;
				_time_wait_start = DateTime.Now;
				_timeoutCheckInt.AddInterruption (TimeWait);
				Logger.Log (LogLevel.Trace, this, "Enter to TIME_WAIT");
			}
		}

		DateTime _time_wait_start;
		TimeSpan _time_wait = TimeSpan.FromSeconds (30);
		void TimeWait ()
		{
			if (DateTime.Now.Subtract (_time_wait_start) >= _time_wait) {
				_sendWaitHandle.Set ();
				_shutdownWaitHandle.Set ();
				_sendWaitHandle.Close ();
				_shutdownWaitHandle.Close ();
				_timeoutCheckInt.RemoveInterruption (CheckTimeout);
				_timeoutCheckInt.RemoveInterruption (TimeWait);
				_sock.Dispose ();
				Logger.Log (LogLevel.Trace, this, "Disposed");
			}
		}

		#endregion

		class Packet
		{
			public byte[] Data;
			public int Filled;
			public uint Sequence;
			public DateTime Start;
			public DateTime RTO;
			public TimeSpan RTO_Interval;
			public int Retries;
			public PacketState State;
			public uint Index;
			public uint RetransmitIndex;

			public Packet (int max_segment_size)
			{
				Data = new byte[max_segment_size];
				Reset ();
			}

			public void Reset ()
			{
				Setup (null, 0, 0, 0, 0, TimeSpan.Zero, PacketState.Empty);
			}

			public void Setup (byte[] src, int offset, int size, uint sequence, uint index, TimeSpan rto_interval, PacketState state)
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
				Index = index;
				RetransmitIndex = index;
				RTO = DateTime.Now + rto_interval;
				RTO_Interval = rto_interval;
				Retries = 0;
				State = state;
				Start = DateTime.Now;
			}
		}

		enum PacketState : byte
		{
			Empty,
			AckWaiting
		}

		class ReceiveSegment
		{
			public ReceiveSegment (uint seq, byte[] array)
			{
				this.Sequence = seq;
				this.Array = array;
				this.Offset = 0;
				this.Count = array.Length;
				this.NextSequence = seq + (uint)array.Length;
			}

			public uint Sequence;
			public byte[] Array;
			public int Offset;
			public int Count;
			public uint NextSequence;
		}
	}
}
