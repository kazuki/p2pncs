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
using System.Text;
using System.Threading;
using p2pncs.Threading;
using System.Net;
using System.Net.Sockets;

namespace p2pncs.Net.Overlay.DFS.MMLC
{
	public class FileInfoCrawler : IDisposable
	{
		ITcpListener _listener;
		MMLC _mmlc;
		IntervalInterrupter _int;
		List<ThreadInfo> _connections = new List<ThreadInfo> ();
		bool _crawling = false;

		public FileInfoCrawler (ITcpListener listener, MMLC mmlc, IntervalInterrupter interrupter)
		{
			_listener = listener;
			_mmlc = mmlc;
			_int = interrupter;
			_listener.RegisterAcceptHandler (typeof (Request), Accepted);
			_int.AddInterruption (StartCrawling);
		}

		void StartCrawling ()
		{
			lock (_connections) {
				if (_crawling)
					return;
				_crawling = true;
			}
			Thread thrd = ThreadTracer.CreateThread (CrawlingThread, "FileInfoCrawler.CrawlingThread");
			thrd.Start ();
		}

		void CrawlingThread ()
		{
			Socket sock = null;
			ThreadInfo ti = new ThreadInfo (false, null, Thread.CurrentThread);
			lock (_connections) {
				_connections.Add (ti);
			}
			try {
				NodeHandle[] nodes = _mmlc.KeyBasedRouter.RoutingAlgorithm.GetRandomNodes (1);
				if (nodes == null || nodes.Length == 0)
					return;
				sock = new Socket (AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
				ti.Socket = sock;
				sock.ReceiveTimeout = sock.SendTimeout = 3000;
				sock.Connect (new IPEndPoint (((IPEndPoint)nodes[0].EndPoint).Address, nodes[0].TcpPort));
				_listener.SendMessage (sock, new Request ());
				Response res = _listener.ReceiveMessage (sock, 1024 * 1024 * 8) as Response;
				if (res != null)
					_mmlc.AppendHeader (res.Headers);
			} catch {
			} finally {
				if (sock != null) {
					try {
						sock.Close ();
					} catch {}
				}
				lock (_connections) {
					_crawling = false;
					_connections.Remove (ti);
				}
			}
		}

		void Accepted (object sender, TcpListenerAcceptedEventArgs args)
		{
			Thread thrd = ThreadTracer.CreateThread (Accepted_Request, "FileInfoCrawler Send Thread");
			ThreadInfo ti = new ThreadInfo (true, args.Socket, thrd);
			lock (_connections) {
				_connections.Add (ti);
			}
			thrd.Start (ti);
		}

		void Accepted_Request (object o)
		{
			ThreadInfo ti = (ThreadInfo)o;
			try {
				_listener.SendMessage (ti.Socket, new Response (_mmlc.GetRandomHeaders (256)));
			} catch {
			} finally {
				try {
					ti.Socket.Close ();
				} catch {}
				lock (_connections) {
					_connections.Remove (ti);
				}
			}
		}

		public void Dispose ()
		{
			_int.RemoveInterruption (StartCrawling);

			Thread[] thrds;
			lock (_connections) {
				thrds = new Thread[_connections.Count];
				for (int i = 0; i < _connections.Count; i++) {
					thrds[i] = _connections[i].Thread;
					try {
						_connections[i].Socket.Close ();
					} catch {}
				}
			}

			for (int i = 0; i < thrds.Length; i ++) {
				if (!thrds[i].IsAlive)
					continue;
				try {
					if (!thrds[i].Join (100))
						thrds[i].Abort ();
				} catch {}
			}
		}

		[SerializableTypeId (0x413)]
		class Request
		{
		}

		[SerializableTypeId (0x414)]
		class Response
		{
			[SerializableFieldId (0)]
			MergeableFileHeader[] _headers;

			public Response (MergeableFileHeader[] headers)
			{
				_headers = headers;
			}

			public MergeableFileHeader[] Headers {
				get { return _headers; }
			}
		}

		class ThreadInfo
		{
			public bool IsAcceptSocket;
			public Socket Socket;
			public Thread Thread;
			public DateTime Start;

			public ThreadInfo (bool isAcceptSocket, Socket sock, Thread thrd)
			{
				this.IsAcceptSocket = isAcceptSocket;
				this.Socket = sock;
				this.Thread = thrd;
				this.Start = DateTime.Now;
			}
		}
	}
}
