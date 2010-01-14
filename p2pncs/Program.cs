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
using System.Data;
using System.IO;
using System.Net;
using System.Threading;
using Kazuki.Net.HttpServer;
using Kazuki.Net.HttpServer.Middlewares;
using p2pncs.Net;
using XmlConfigLibrary;

namespace p2pncs
{
	class Program : IDisposable
	{
		const string CONFIG_PATH = "p2pncs.xml";
		XmlConfig _config = new XmlConfig ();
		ManualResetEvent _startupWaitHandle = new ManualResetEvent (false);
		ManualResetEvent _waitHandle = new ManualResetEvent (false);
		WebApp _app;
		bool _running = false;
		string _url;
		Node _node;

		public event EventHandler Started;

		[STAThread]
		static void Main (string[] args)
		{
			using (Program prog = new Program ()) {
				if (GraphicalInterface.Check ()) {
					new GraphicalInterface ().Run (prog);
				} else {
					new ConsoleInterface ().Run (prog);
				}
			}
		}

		public static bool LoadConfig (XmlConfig config)
		{
			bool exists = false;
			config.Define<int> (ConfigFields.NetBindUdp, IntParser.Instance, new IntRangeValidator (1, ushort.MaxValue), new Random ().Next (49152, ushort.MaxValue));
			config.Define<int> (ConfigFields.NetBindTcp, IntParser.Instance, new IntRangeValidator (1, ushort.MaxValue), config.GetValue<int> (ConfigFields.NetBindUdp));
			config.Define<int> (ConfigFields.GwBindTcp, IntParser.Instance, new IntRangeValidator (1, ushort.MaxValue), 8080);
			config.Define<bool> (ConfigFields.GwBindAny, BooleanParser.Instance, null, false);
			try {
				exists = File.Exists (CONFIG_PATH);
				if (exists)
					config.Load (CONFIG_PATH);
			} catch {}

			config.Save (CONFIG_PATH);
			return exists;
		}

		public void Run ()
		{
			_running = true;
			try {
				if (!LoadConfig (_config)) {
					throw new ConfigFileInitializedException ();
				}

				ushort bindUdp = (ushort)_config.GetValue<int> (ConfigFields.NetBindUdp);
				ushort bindTcp = (ushort)_config.GetValue<int> (ConfigFields.NetBindTcp);
				int gwBindTcp = _config.GetValue<int> (ConfigFields.GwBindTcp);
				_url = string.Format ("http://127.0.0.1:{0}/", gwBindTcp);
				using (IDatagramEventSocket dgramSock = UdpSocket.CreateIPv4 ())
				using (TcpListener listener = new TcpListener ()) {
					dgramSock.Bind (new IPEndPoint (IPAddress.Any, bindUdp));
					listener.Bind (new IPEndPoint (IPAddress.Any, bindTcp));
					listener.ListenStart ();
					CreateDatabaseConnectionDelegate create_session_db = delegate () {
						IDbConnection connection = new Mono.Data.Sqlite.SqliteConnection ();
						connection.ConnectionString = "Data Source=http-session.sqlite,DateTimeFormat=Ticks,Pooling=False";
						connection.Open ();
						return connection;
					};
					using (Interrupters ints = new Interrupters ())
					using (Node node = new Node (ints, dgramSock, listener, "database.sqlite", bindUdp, bindTcp))
					using (WebApp app = new WebApp (node, ints))
					using (SessionMiddleware mid1 = new SessionMiddleware (create_session_db, app))
					using (HttpServer.CreateEmbedHttpServer (mid1, null, true, true, _config.GetValue<bool> (ConfigFields.GwBindAny), gwBindTcp, 16)) {
						InitNodeList initNodeList = new InitNodeList (node.PortOpenChecker);
						_app = app;
						_node = node;
						_startupWaitHandle.Set ();
						if (Started != null) {
							try {
								Started (this, EventArgs.Empty);
							} catch {}
						}
						initNodeList.Load ();
						app.ExitWaitHandle.WaitOne ();
						initNodeList.Save ();
						app.CreateStatisticsXML ().Save ("statistics-" + DateTime.Now.ToString ("yyyyMMddHHmmss") + ".xml");
						_waitHandle.Set ();
					}
				}
			} finally {
				_running = false;
				_startupWaitHandle.Set ();
			}
		}

		public void Exit ()
		{
			if (_app != null)
				_app.Exit ();
		}

		public WaitHandle WaitHandle {
			get { return _waitHandle; }
		}

		public WaitHandle StartupWaitHandle {
			get { return _startupWaitHandle; }
		}

		public Node Node {
			get { return _node; }
		}

		public string Url {
			get { return _url; }
		}

		public bool Running {
			get { return _running; }
		}

		public void Dispose ()
		{
		}
	}
}
