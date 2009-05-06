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
using System.Diagnostics;
using System.Net;
using System.Threading;
using p2pncs.Net;
using p2pncs.Threading;

namespace p2pncs.Simulation.VirtualNet
{
	public class VirtualNetwork
	{
		bool _active = true;
		Thread _managementThread;
		Thread[] _invokeThreads;
		AutoResetEvent[] _invokeStartHandles;
		AutoResetEvent[] _invokeEndHandles;
		List<DatagramInfo> _dgramList = new List<DatagramInfo> (1 << 14);
		int _dgramListIndex = 0;

		int _delayPrec;
		List<DatagramInfo>[] _dgramRingBuffer;
		int _dgramRingOffset = 0;

		ReaderWriterLockSlim _nodeLock = new ReaderWriterLockSlim (LockRecursionPolicy.NoRecursion);
		Dictionary<EndPoint, VirtualNetworkNode> _mapping = new Dictionary<EndPoint, VirtualNetworkNode> ();
		List<VirtualNetworkNode> _nodes = new List<VirtualNetworkNode> ();

		long _packets = 0, _noDestPackets = 0;
		long _totalTraffic = 0;

		int _minJitter = int.MaxValue, _maxJitter = int.MinValue, _jitterPackets = 0;
		double _avgJitter = 0.0;
		object _jitterLock = new object ();

		ILatency _latency;
		IPacketLossRate _packetLossrate;
		Random _rnd = new Random ();

		public VirtualNetwork (ILatency latency, int delayPrecision, IPacketLossRate lossrate, int invokeThreads)
		{
			_latency = latency;
			_packetLossrate = lossrate;
			_delayPrec = delayPrecision;
			_dgramRingBuffer = new List<DatagramInfo> [(int)Math.Max (1, _latency.MaxLatency.TotalMilliseconds / delayPrecision)];
			for (int i = 0; i < _dgramRingBuffer.Length; i ++)
				_dgramRingBuffer[i] = new List<DatagramInfo> (1 << 14);

			_managementThread = new Thread (ManagementThread);
			_managementThread.Name = "VirtualNetwork.ManagementThread";
			_invokeThreads = new Thread[invokeThreads];
			_invokeStartHandles = new AutoResetEvent[_invokeThreads.Length];
			_invokeEndHandles = new AutoResetEvent[_invokeThreads.Length];
			for (int i = 0; i < _invokeThreads.Length; i ++) {
				_invokeThreads[i] = new Thread (InvokeThread);
				_invokeThreads[i].Name = "VirtualNetwork.InvokeThread" + i.ToString ();
				_invokeStartHandles[i] = new AutoResetEvent (false);
				_invokeEndHandles[i] = new AutoResetEvent (false);
			}
			_managementThread.Start ();
			for (int i = 0; i < _invokeThreads.Length; i ++)
				_invokeThreads[i].Start (i);
		}

		void ManagementThread ()
		{
			Stopwatch timer = new Stopwatch ();
			List<DatagramInfo> list;

			while (_active) {
				timer.Reset ();
				timer.Start ();

				lock (_dgramRingBuffer) {
					list = _dgramRingBuffer[_dgramRingOffset];
					_dgramRingBuffer[_dgramRingOffset] = _dgramList;
					_dgramRingOffset = (_dgramRingOffset + 1) % _dgramRingBuffer.Length;
					_dgramList = list;
				}
				Interlocked.Exchange (ref _dgramListIndex, -1);
				_nodeLock.EnterReadLock ();
				for (int i = 0; i < _invokeStartHandles.Length; i ++)
					_invokeStartHandles[i].Set ();
				WaitHandle.WaitAll (_invokeEndHandles);
				_nodeLock.ExitReadLock ();
				list.Clear ();

				timer.Stop ();
				int waitMS = (int)(_delayPrec - timer.ElapsedMilliseconds);
				if (waitMS > 0)
					Thread.Sleep (waitMS);
			}
		}

		void InvokeThread (object o)
		{
			int idx = (int)o;

			while (_active) {
				_invokeStartHandles[idx].WaitOne ();
				if (!_active) return;

				while (true) {
					int i = Interlocked.Increment (ref _dgramListIndex);
					if (i >= _dgramList.Count)
						break;
					DatagramInfo dgram = _dgramList[i];
					if (IsLossPacket (dgram.SourceEndPoint, dgram.DestinationEndPoint))
						continue;
					VirtualNetworkNode node;
					if (!_mapping.TryGetValue (dgram.DestinationEndPoint, out node)) {
						Interlocked.Increment (ref _noDestPackets);
						continue;
					}

					int jitter = (int)((DateTime.Now.Subtract (dgram.StartDateTime) - dgram.ExpectedDelay).TotalMilliseconds);
					lock (_jitterLock) {
						_minJitter = Math.Min (_minJitter, jitter);
						_maxJitter = Math.Max (_maxJitter, jitter);
						_avgJitter = ((_avgJitter * _jitterPackets) + jitter) / (_jitterPackets + 1);
						_jitterPackets ++;
						Interlocked.Increment (ref _packets);
					}

					if (dgram.Datagram != null) {
						DatagramReceiveEventArgs eventArgs = new DatagramReceiveEventArgs (dgram.Datagram, dgram.Datagram.Length, dgram.SourceEndPoint);
						node.DatagramSocket.InvokeReceivedEvent (node.DatagramSocket, eventArgs);
						Interlocked.Add (ref _totalTraffic, dgram.Datagram.Length);
					} else {
						node.MessagingSocket.Deliver (dgram.SourceEndPoint, dgram.Message);
					}
				}
				_invokeEndHandles[idx].Set ();
			}
		}

		internal VirtualNetworkNode AddVirtualNode (VirtualDatagramEventSocket sock, EndPoint bindEP)
		{
			VirtualNetworkNode node = new VirtualNetworkNode (sock, bindEP);
			_nodeLock.EnterWriteLock ();
			_mapping[bindEP] = node;
			_nodes.Add (node);
			_nodeLock.ExitWriteLock ();
			return node;
		}

		internal void AddVirtualMessagingSocketToVirtualNode (VirtualDatagramEventSocket sock, VirtualMessagingSocket msock)
		{
			_nodeLock.EnterReadLock ();
			VirtualNetworkNode node;
			if (_mapping.TryGetValue (sock.BindedPublicEndPoint, out node)) {
				node.MessagingSocket = msock;
			}
			_nodeLock.ExitReadLock ();
		}

		internal void RemoveVirtualNode (VirtualNetworkNode node)
		{
			_nodeLock.EnterWriteLock ();
			_mapping.Remove (node.BindedPublicEndPoint);
			_nodes.Remove (node);
			_nodeLock.ExitWriteLock ();
		}

		void MessageScheduling (DatagramInfo dgram)
		{
			dgram.ExpectedDelay = _latency.ComputeLatency (dgram.SourceEndPoint, dgram.DestinationEndPoint);

			// とりあえず、最短遅延で配送をスケジューリング
			lock (_dgramRingBuffer) {
				_dgramRingBuffer[(_dgramRingOffset + (int)(dgram.ExpectedDelay.TotalMilliseconds / _delayPrec - 1)) % _dgramRingBuffer.Length].Add (dgram);
			}
		}

		internal void AddSendQueue (EndPoint srcEP, EndPoint destEP, byte[] buffer, int offset, int size)
		{
			MessageScheduling (new DatagramInfo (srcEP, destEP, buffer, offset, size));
		}

		internal void AddSendQueue (EndPoint srcEP, EndPoint destEP, object msg)
		{
			MessageScheduling (new DatagramInfo (srcEP, destEP, msg));
		}

		public long Packets {
			get { return Interlocked.Read (ref _packets); }
		}

		public long LostPacketsBecauseNoDestination {
			get { return Interlocked.Read (ref _noDestPackets); }
		}

		public long TotalTraffic {
			get { return Interlocked.Read (ref _totalTraffic); }
		}

		public void GetAndResetJitterHistory (out int min, out double avg, out int max)
		{
			lock (_jitterLock) {
				min = _minJitter;
				avg = _avgJitter;
				max = _maxJitter;
				_minJitter = int.MaxValue;
				_maxJitter = int.MinValue;
				_avgJitter = 0;
				_jitterPackets = 0;
			}
		}

		bool IsLossPacket (EndPoint src, EndPoint dst)
		{
			double rate = _packetLossrate.ComputePacketLossRate (src, dst);
			lock (_rnd) {
				return _rnd.NextDouble () < rate;
			}
		}

		public void CloseAllSockets ()
		{
			_nodeLock.EnterWriteLock ();
			_mapping.Clear ();
			_nodes.Clear ();
			_nodeLock.ExitWriteLock ();
		}

		public void CloseSockets (IList<VirtualDatagramEventSocket> list)
		{
			_nodeLock.EnterWriteLock ();
			for (int i = 0; i < list.Count; i ++) {
				VirtualNetworkNode vnode = list[i].VirtualNetworkNodeInfo;
				_mapping.Remove (vnode.BindedPublicEndPoint);
				_nodes.Remove (vnode);
			}
			_nodeLock.ExitWriteLock ();
		}

		public void Close ()
		{
			_active = false;
			for (int i = 0; i < _invokeStartHandles.Length; i ++) {
				_invokeStartHandles[i].Set ();
				_invokeEndHandles[i].Set ();
			}
			_managementThread.Join ();
			for (int i = 0; i < _invokeThreads.Length; i ++)
				_invokeThreads[i].Join ();
		}

		#region Internal Classes
		internal class VirtualNetworkNode
		{
			VirtualDatagramEventSocket _sock;
			VirtualMessagingSocket _msock;
			EndPoint _bindEP;

			public VirtualNetworkNode (VirtualDatagramEventSocket sock, EndPoint bindEP)
			{
				_sock = sock;
				_bindEP = bindEP;
			}

			public EndPoint BindedPublicEndPoint {
				get { return _bindEP; }
			}

			public VirtualDatagramEventSocket DatagramSocket {
				get { return _sock; }
			}

			public VirtualMessagingSocket MessagingSocket {
				get { return _msock; }
				set { _msock = value;}
			}
		}

		class DatagramInfo
		{
			EndPoint _destEP, _srcEP;
			byte[] _buffer;
			object _msg;
			DateTime _start;
			TimeSpan _expectedDelay;

			DatagramInfo (EndPoint srcEP, EndPoint destEP)
			{
				_srcEP = srcEP;
				_destEP = destEP;
				_start = DateTime.Now;
			}

			public DatagramInfo (EndPoint srcEP, EndPoint destEP, byte[] buffer, int offset, int size)
				: this (srcEP, destEP)
			{
				if (offset != 0 || size != buffer.Length) {
					_buffer = new byte[size];
					Buffer.BlockCopy (buffer, offset, _buffer, 0, size);
				} else {
					_buffer = buffer;
				}
			}

			public DatagramInfo (EndPoint srcEP, EndPoint destEP, object msg)
				: this (srcEP, destEP)
			{
				_msg = msg;
			}

			public EndPoint SourceEndPoint {
				get { return _srcEP; }
			}

			public EndPoint DestinationEndPoint {
				get { return _destEP; }
			}

			public byte[] Datagram {
				get { return _buffer; }
			}

			public object Message {
				get { return _msg; }
			}

			public DateTime StartDateTime {
				get { return _start; }
			}

			public TimeSpan ExpectedDelay {
				get { return _expectedDelay; }
				set { _expectedDelay = value;}
			}
		}
		#endregion
	}
}
