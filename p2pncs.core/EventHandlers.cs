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
using System.Threading;

namespace p2pncs
{
	public class EventHandlers<TKey, TEventArgs> where TEventArgs : EventArgs
	{
		EventHandler<TEventArgs> _unknownKeyHandlers = null;
		Dictionary<TKey, EventHandler<TEventArgs>> _dic = new Dictionary<TKey,EventHandler<TEventArgs>> ();

		public void Add (TKey key, EventHandler<TEventArgs> handler)
		{
			EventHandler<TEventArgs> entry;
			lock (_dic) {
				if (_dic.TryGetValue (key, out entry)) {
					entry -= handler;
					entry += handler;
				} else {
					_dic.Add (key, handler);
				}
			}
		}

		public void AddUnknownKeyHandler (EventHandler<TEventArgs> handler)
		{
			lock (_dic) {
				_unknownKeyHandlers -= handler;
				_unknownKeyHandlers += handler;
			}
		}

		public void Remove (TKey key)
		{
			lock (_dic) {
				_dic.Remove (key);
			}
		}

		public void Remove (TKey key, EventHandler<TEventArgs> handler)
		{
			EventHandler<TEventArgs> entry;
			lock (_dic) {
				if (!_dic.TryGetValue (key, out entry))
					return;
				entry -= handler;
				if (entry == null)
					_dic.Remove (key);
			}
		}

		public void RemoveUnknownKeyHandler (EventHandler<TEventArgs> handler)
		{
			lock (_dic) {
				_unknownKeyHandlers -= handler;
			}
		}

		public EventHandler<TEventArgs> Get (TKey key)
		{
			EventHandler<TEventArgs> handler;
			lock (_dic) {
				if (!_dic.TryGetValue (key, out handler))
					handler = _unknownKeyHandlers;
			}
			return handler;
		}

		public EventHandler<TEventArgs> UnknownKeyHandler {
			get { return _unknownKeyHandlers; }
		}

		public void Clear ()
		{
			lock (_dic) {
				_unknownKeyHandlers = null;
				_dic.Clear ();
			}
		}

		public void Invoke (TKey key, object sender, TEventArgs e)
		{
			EventHandler<TEventArgs> handler = Get (key);
			if (handler == null)
				return;
			handler (sender, e);
		}

#if false
		public IAsyncResult BeginInvoke (TKey key, object sender, TEventArgs e, AsyncCallback callback, object @object)
		{
			EventHandler<TEventArgs> handler = Get (key);
			if (handler == null)
				throw new KeyNotFoundException ();
			return new AsyncResultWrapper (handler, handler.BeginInvoke (sender, e, callback, @object));
		}

		public void EndInvoke (IAsyncResult result)
		{
			AsyncResultWrapper wrapper = result as AsyncResultWrapper;
			if (wrapper == null)
				throw new ArgumentNullException ();
			wrapper.EndInvoke ();
		}

		sealed class AsyncResultWrapper : IAsyncResult
		{
			EventHandler<TEventArgs> _handler;
			IAsyncResult _result;

			public AsyncResultWrapper (EventHandler<TEventArgs> handler, IAsyncResult result)
			{
				_handler = handler;
				_result = result;
			}

			public void EndInvoke ()
			{
				_handler.EndInvoke (_result);
			}

			#region IAsyncResult Members

			public object AsyncState {
				get { return _result.AsyncState; }
			}

			public WaitHandle AsyncWaitHandle {
				get { return _result.AsyncWaitHandle; }
			}

			public bool CompletedSynchronously {
				get { return _result.CompletedSynchronously; }
			}

			public bool IsCompleted {
				get { return _result.IsCompleted; }
			}

			#endregion
		}
#endif
	}
}
