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
using p2pncs.Net;
using p2pncs.Threading;

namespace p2pncs.Simulation.VirtualNet
{
	public class VirtualMessagingSocket : MessagingSocketBase
	{
		public VirtualMessagingSocket (VirtualDatagramEventSocket baseSock, bool ownSocket,
			IntervalInterrupter interrupter, TimeSpan timeout, int maxRetry, int retryBufferSize, int inquiryDupCheckSize)
			: base (baseSock, ownSocket, interrupter, timeout, maxRetry, retryBufferSize, inquiryDupCheckSize)
		{
			baseSock.VirtualNetwork.AddVirtualMessagingSocketToVirtualNode (baseSock, this);
		}

		internal void Deliver (EndPoint remoteEP, object obj)
		{
			if (!IsActive)
				return;

			if (obj is RequestWrapper) {
				RequestWrapper req = (RequestWrapper)obj;
				InquiredEventArgs args = new InquiredResponseState (req.Message, remoteEP, req.ID);
				InvokeInquired (this, args);
			} else if (obj is ResponseWrapper) {
				ResponseWrapper res = (ResponseWrapper)obj;
				InquiredAsyncResultBase ar = RemoveFromRetryList (res.ID, remoteEP);
				if (ar == null)
					return;
				ar.Complete (res.Message, this);
				InvokeInquirySuccess (this, new InquiredEventArgs (ar.Request, res.Message, remoteEP));
			} else if (obj is OneWayMessage) {
				InvokeReceived (this, new ReceivedEventArgs ((obj as OneWayMessage).Message, remoteEP));
			}
		}

		#region IMessagingSocket/MessagingSocketBase Members

		public override void Send (object obj, EndPoint remoteEP)
		{
			if (remoteEP == null)
				throw new ArgumentNullException ();

			VirtualDatagramEventSocket vsock = (VirtualDatagramEventSocket)_sock;
			try {
				if (vsock.VirtualNetwork != null)
					vsock.VirtualNetwork.AddSendQueue (vsock.BindedPublicEndPoint, remoteEP, new OneWayMessage (obj));
			} catch {}
		}

		protected override void StartResponse_Internal (MessagingSocketBase.InquiredResponseState state, object response)
		{
			try {
				VirtualDatagramEventSocket vsock = (VirtualDatagramEventSocket)_sock;
				if (vsock.VirtualNetwork != null)
					vsock.VirtualNetwork.AddSendQueue (vsock.BindedPublicEndPoint, state.EndPoint, new ResponseWrapper (response, state.ID));
			} catch {}
		}

		protected override InquiredAsyncResultBase CreateInquiredAsyncResult (uint id, object obj, EndPoint remoteEP, TimeSpan timeout, int maxRetry, AsyncCallback callback, object state)
		{
			return new InquiredAsyncResult (obj, remoteEP, id, timeout, maxRetry, callback, state);
		}

		#endregion

		#region Internal Class
		class RequestWrapper
		{
			object _msg;
			uint _id;

			public RequestWrapper (object msg, uint id)
			{
				_msg = msg;
				_id = id;
			}

			public object Message {
				get { return _msg; }
			}

			public uint ID {
				get { return _id; }
			}
		}
		class ResponseWrapper
		{
			object _msg;
			uint _id;

			public ResponseWrapper (object msg, uint id)
			{
				_msg = msg;
				_id = id;
			}

			public object Message {
				get { return _msg; }
			}

			public uint ID {
				get { return _id; }
			}
		}
		class OneWayMessage
		{
			object _msg;

			public OneWayMessage (object msg)
			{
				_msg = msg;
			}

			public object Message {
				get { return _msg; }
			}
		}
		class InquiredAsyncResult : InquiredAsyncResultBase
		{
			public InquiredAsyncResult (object req, EndPoint remoteEP, uint id, TimeSpan timeout, int maxRetry, AsyncCallback callback, object state)
				: base (req, remoteEP, id, timeout, maxRetry, callback, state)
			{
			}

			protected override void Transmit_Internal (IDatagramEventSocket sock)
			{
				VirtualDatagramEventSocket vsock = (VirtualDatagramEventSocket)sock;
				try {
					if (vsock.VirtualNetwork != null)
						vsock.VirtualNetwork.AddSendQueue (vsock.BindedPublicEndPoint, _remoteEP, new RequestWrapper (_req, _id));
				} catch {}
			}
		}
		#endregion
	}
}
