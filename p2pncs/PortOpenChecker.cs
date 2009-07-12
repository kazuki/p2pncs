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
using p2pncs.Net;
using p2pncs.Net.Overlay;

namespace p2pncs
{
	class PortOpenChecker : IDisposable
	{
		IMessagingSocket _msock;
		List<EndPoint> _eps = new List<EndPoint> ();
		HashSet<EndPoint> _sentEPs = new HashSet<EndPoint> ();
		IKeyBasedRouter _kbr;
		Thread _thrd = null;
		ManualResetEvent _waitHandle = new ManualResetEvent (false);
		bool _portOK = false, _trying = false;
		const string ACK = "ACK";
		const int CheckRequestSendNodes = 3;
		static readonly TimeSpan TIMEOUT = TimeSpan.FromSeconds (2);

		public PortOpenChecker (IKeyBasedRouter kbr)
		{
			_msock = kbr.MessagingSocket;
			_kbr = kbr;
			_msock.AddInquiredHandler (typeof (PortCheckStartMessage), Inquired_PortCheckStartMessage);
			_msock.AddInquiredHandler (typeof (PortCheckRequest), Inquired_PortCheckRequest);
			_msock.AddInquiredHandler (typeof (PortCheckResponse), Inquired_PortCheckResponse);
		}

		public void Join (EndPoint ep)
		{
			Join (new EndPoint[] {ep});
		}

		public void Join (EndPoint[] eps)
		{
			if (_portOK) {
				_kbr.Join (eps);
				return;
			}

			lock (_eps) {
				for (int i = 0; i < eps.Length; i ++) {
					if (_eps.Contains (eps[i]))
						continue;
					_eps.Add (eps[i]);
				}
				if (!_trying)
					TryNext ();
			}
		}

		void TryNext ()
		{
			EndPoint select = null;
			if (_eps.Count > 0) {
				select = _eps[0];
				_eps.RemoveAt (0);
				_trying = true;
				_sentEPs.Add (select);
			}
			if (select != null)
				_msock.BeginInquire (new PortCheckStartMessage (), select, Join_Callback, select);
		}

		void Success ()
		{
			_waitHandle.Set ();
			EndPoint[] eps1;
			EndPoint[] eps2;
			lock (_eps) {
				_portOK = true;
				eps1 = _eps.ToArray ();
				_eps.Clear ();
				eps2 = new EndPoint[_sentEPs.Count];
				_sentEPs.CopyTo (eps2);
			}
			_kbr.Join (eps1);
			_kbr.Join (eps2);
		}

		void Join_Callback (IAsyncResult ar)
		{
			object response;
			try {
				response = _msock.EndInquire (ar);
			} catch {
				response = null;
			}
			lock (_eps) {
				if (_portOK)
					return;
				if (response == null) {
					TryNext ();
					return;
				}
				if (response is PortCheckResponse) {
					Success ();
				} else {
					_thrd = new Thread (CallbackWaitThread);
					_thrd.Start ();
				}
			}
		}

		void CallbackWaitThread ()
		{
			if (!_waitHandle.WaitOne (TIMEOUT)) {
				lock (_eps) {
					if (_portOK)
						return;
					_thrd = null;
					_trying = false;
					TryNext ();
				}
				Logger.Log (LogLevel.Error, this, "UDPポートが適切に開放されていない可能性があります");
				return;
			}

			lock (_eps) {
				_thrd = null;
				_trying = false;
			}
		}

		void Inquired_PortCheckStartMessage (object sender, InquiredEventArgs args)
		{
			NodeHandle[] nodes = _kbr.RoutingAlgorithm.GetRandomNodes (CheckRequestSendNodes);
			if (nodes == null || nodes.Length == 0) {
				_msock.StartResponse (args, new PortCheckResponse ());
			} else {
				_msock.StartResponse (args, ACK);
				PortCheckRequest req = new PortCheckRequest (args.EndPoint);
				for (int i = 0; i < nodes.Length; i ++)
					_msock.BeginInquire (req, nodes[i].EndPoint, null, null);
			}
		}

		void Inquired_PortCheckRequest (object sender, InquiredEventArgs args)
		{
			_msock.StartResponse (args, ACK);
			_msock.BeginInquire (new PortCheckResponse (), (args.InquireMessage as PortCheckRequest).EndPoint, null, null);
		}

		void Inquired_PortCheckResponse (object sender, InquiredEventArgs args)
		{
			_msock.StartResponse (args, ACK);
			lock (_eps) {
				if (_sentEPs.Contains (args.EndPoint))
					return; // ignore
			}

			Success ();
		}

		public void Dispose ()
		{
			_msock.RemoveInquiredHandler (typeof (PortCheckStartMessage), Inquired_PortCheckStartMessage);
			_msock.RemoveInquiredHandler (typeof (PortCheckRequest), Inquired_PortCheckRequest);
			lock (_eps) {
				_portOK = true;
				_eps.Clear ();
			}
			_waitHandle.Close ();
			if (_thrd != null) {
				try {
					if (!_thrd.Join (100))
						_thrd.Abort ();
				} catch {}
			}
		}

		[SerializableTypeId (0x1005)]
		class PortCheckStartMessage
		{
		}

		[SerializableTypeId (0x1006)]
		class PortCheckRequest
		{
			[SerializableFieldId (0)]
			EndPoint _ep;

			public PortCheckRequest (EndPoint ep)
			{
				_ep = ep;
			}

			public EndPoint EndPoint {
				get { return _ep; }
			}
		}

		[SerializableTypeId (0x1007)]
		class PortCheckResponse
		{
		}
	}
}
