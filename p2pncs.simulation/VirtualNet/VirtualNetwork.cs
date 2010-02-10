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
using System.Diagnostics;
using System.Net;
using System.Threading;
using p2pncs.Net;
using p2pncs.Threading;
using p2pncs.Utility;

namespace p2pncs.Simulation.VirtualNet
{
	public class VirtualNetwork : IDisposable
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

		ReaderWriterLockWrapper _nodeLock = new ReaderWriterLockWrapper ();
		Dictionary<EndPoint, VirtualNetworkNode> _mapping = new Dictionary<EndPoint, VirtualNetworkNode> ();
		List<VirtualNetworkNode> _nodes = new List<VirtualNetworkNode> ();

		long _packets = 0, _noDestPackets = 0;
		long _totalTraffic = 0;

		object _jitterLock = new object ();
		StandardDeviation _jitterSD = new StandardDeviation (false);

		ILatency _latency;
		IPacketLossRate _packetLossrate;

		public VirtualNetwork (ILatency latency, int delayPrecision, IPacketLossRate lossrate, int invokeThreads)
		{
			_latency = latency;
			_packetLossrate = lossrate;
			_delayPrec = delayPrecision;
			_dgramRingBuffer = new List<DatagramInfo> [(int)Math.Max (1, _latency.MaxLatency.TotalMilliseconds / delayPrecision)];
			for (int i = 0; i < _dgramRingBuffer.Length; i ++)
				_dgramRingBuffer[i] = new List<DatagramInfo> (1 << 14);

			_managementThread = ThreadTracer.CreateThread (ManagementThread, "VirtualNetwork.ManagementThread");
			_invokeThreads = new Thread[invokeThreads];
			_invokeStartHandles = new AutoResetEvent[_invokeThreads.Length];
			_invokeEndHandles = new AutoResetEvent[_invokeThreads.Length];
			for (int i = 0; i < _invokeThreads.Length; i ++) {
				_invokeThreads[i] = ThreadTracer.CreateThread (InvokeThread, "VirtualNetwork.InvokeThread" + i.ToString ());
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
			OSTimerPrecision.SetCurrentThreadToHighPrecision ();

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
				using (_nodeLock.EnterReadLock ()) {
					for (int i = 0; i < _invokeStartHandles.Length; i ++)
						_invokeStartHandles[i].Set ();
					WaitHandle.WaitAll (_invokeEndHandles);
				}
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
				while (_active) {
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
						_jitterSD.AddSample (jitter);
						Interlocked.Increment (ref _packets);
					}

					if (dgram.Message == null) {
						node.Socket.Deliver (dgram.SourceEndPoint, dgram.Datagram, 0, dgram.Datagram.Length);
					} else {
						node.Socket.Deliver (dgram.SourceEndPoint, dgram.Message);
					}
				}
				_invokeEndHandles[idx].Set ();
			}
		}

		internal VirtualNetworkNode AddVirtualNode (VirtualUdpSocket sock, EndPoint bindEP)
		{
			VirtualNetworkNode node = new VirtualNetworkNode (sock, bindEP);
			using (_nodeLock.EnterWriteLock ()) {
				_mapping[bindEP] = node;
				_nodes.Add (node);
			}
			return node;
		}

		internal void RemoveVirtualNode (VirtualNetworkNode node)
		{
			using (_nodeLock.EnterWriteLock ()) {
				_mapping.Remove (node.BindedPublicEndPoint);
				_nodes.Remove (node);
			}
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

		public void GetAndResetJitterHistory (out int min, out double avg, out double sd, out int max)
		{
			lock (_jitterLock) {
				min = (int)_jitterSD.Minimum;
				avg = _jitterSD.Average;
				max = (int)_jitterSD.Maximum;
				sd = _jitterSD.ComputeStandardDeviation ();
				_jitterSD.Clear ();
			}
		}

		bool IsLossPacket (EndPoint src, EndPoint dst)
		{
			double rate = _packetLossrate.ComputePacketLossRate (src, dst);
			return ThreadSafeRandom.NextDouble () < rate;
		}

		public void CloseAllSockets ()
		{
			using (_nodeLock.EnterWriteLock ()) {
				_mapping.Clear ();
				_nodes.Clear ();
			}
		}

		public void CloseSockets (IList<VirtualUdpSocket> list)
		{
			using (_nodeLock.EnterWriteLock ()) {
				for (int i = 0; i < list.Count; i ++) {
					VirtualNetworkNode vnode = list[i].VirtualNodeInfo;
					_mapping.Remove (vnode.BindedPublicEndPoint);
					_nodes.Remove (vnode);
				}
			}
		}

		public void Close ()
		{
			_active = false;
			CloseAllSockets ();
			for (int i = 0; i < _invokeStartHandles.Length; i ++) {
				_invokeStartHandles[i].Set ();
				_invokeEndHandles[i].Set ();
			}
			_managementThread.Join ();
			for (int i = 0; i < _invokeThreads.Length; i ++)
				_invokeThreads[i].Join ();
		}

		public void Dispose ()
		{
			Close ();
		}

		#region Internal Classes
		internal class VirtualNetworkNode
		{
			VirtualUdpSocket _sock;
			EndPoint _bindEP;

			public VirtualNetworkNode (VirtualUdpSocket sock, EndPoint bindEP)
			{
				_sock = sock;
				_bindEP = bindEP;
			}

			public EndPoint BindedPublicEndPoint {
				get { return _bindEP; }
			}

			public VirtualUdpSocket Socket {
				get { return _sock; }
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
