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

namespace p2pncs.Net.Overlay.Anonymous
{
	public class DatagramEventSocketWrapper : IDatagramEventSocket
	{
		public event DatagramReceiveEventHandler Received;
		IAnonymousSocket _sock = null;
		Queue<byte[]> _queue = new Queue<byte[]> ();
		long _sentBytes = 0, _sentDgrams = 0, _recvBytes = 0, _recvDgrams = 0;
		public static EndPoint DummyEP = new IPEndPoint (IPAddress.Any, 0);

		public DatagramEventSocketWrapper ()
		{
		}

		public void ReceivedHandler (object sender, DatagramReceiveEventArgs e)
		{
			if (Received == null) {
				byte[] data = new byte[e.Size];
				Buffer.BlockCopy (e.Buffer, 0, data, 0, e.Size);
				_queue.Enqueue (data);
				return;
			}
			if (_queue.Count > 0) {
				lock (_queue) {
					while (_queue.Count > 0) {
						byte[] data = _queue.Dequeue ();
						Received (this, new DatagramReceiveEventArgs (data, data.Length, DummyEP));
					}
				}
			}

			Received (this, new DatagramReceiveEventArgs (e.Buffer, e.Size, DummyEP));
		}

		public void Bind (EndPoint bindEP)
		{
		}

		public void Close ()
		{
			if (_sock != null)
				_sock.Close ();
		}

		public void SendTo (byte[] buffer, EndPoint remoteEP)
		{
			SendTo (buffer, 0, buffer.Length, null);
		}

		public void SendTo (byte[] buffer, int offset, int size, EndPoint remoteEP)
		{
			Interlocked.Add (ref _sentBytes, size);
			Interlocked.Increment (ref _sentDgrams);
			_sock.Send (buffer, offset, size);
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

		public IAnonymousSocket Socket {
			get { return _sock; }
			set { _sock = value;}
		}

		public void Dispose ()
		{
			if (_sock != null)
				_sock.Dispose ();
		}
	}
}
