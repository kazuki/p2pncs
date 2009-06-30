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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using Kazuki.Net.HttpServer;
using openCrypto.EllipticCurve;
using p2pncs.Net;
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.DFS.MMLC;
using p2pncs.Security.Captcha;
using p2pncs.Security.Cryptography;
using p2pncs.Simulation;
using p2pncs.Simulation.VirtualNet;
using p2pncs.Threading;
using XmlConfigLibrary;

namespace p2pncs
{
	class DebugProgram : IDisposable
	{
		const int NODES = 100;
		RandomIPAddressGenerator _ipgen = new RandomIPAddressGenerator ();
		VirtualNetwork _network;
		Interrupters _ints;
		IntervalInterrupter _churnInt;
		XmlConfig _config = new XmlConfig ();
		List<DebugNode> _list = new List<DebugNode> ();
		List<IPEndPoint> _eps = new List<IPEndPoint> ();
		IPEndPoint[] _init_nodes;
		int _nodeIdx = -1;
		Random _rnd = new Random ();
		AuthNode _authNode;

		public DebugProgram ()
		{
			_ints = new Interrupters ();
			{
				ECKeyPair tmpKey;
				Key tmpPubKey;
				string tmpName;
				Program.LoadConfig (_config, out tmpKey, out tmpPubKey, out tmpName);
			}
			_churnInt = new IntervalInterrupter (TimeSpan.FromSeconds (500.0 / NODES), "Churn Timer");
			Directory.CreateDirectory ("db");
		}

		public void Run ()
		{
			int base_port = _config.GetValue<int> ("gw/bind/port");

			_network = new VirtualNetwork (LatencyTypes.Constant (20), 5, PacketLossType.Constant (0.05), Environment.ProcessorCount);

			{
				const ushort port = 51423;
				_authNode = new AuthNode (_ints, port);
				AuthServerInfo info = new AuthServerInfo (_authNode.PublicKey, new IPEndPoint (IPAddress.Loopback, port));
				Console.WriteLine ("AuthServerInfo: {0}", info.ToParsableString ());
			}

			for (int i = 0; i < NODES; i++) {
				AddNode (base_port);
				if (i == 10) base_port = -1;
			}
			Console.WriteLine ("{0} Nodes Inserted", NODES);

			_churnInt.AddInterruption (delegate () {
				lock (_list) {
					int idx = _rnd.Next (10, _list.Count);
					DebugNode removed = _list[idx];
					try {
						removed.Dispose ();
					} catch {}
					_list.RemoveAt (idx);
					_eps.RemoveAt (idx);
					AddNode (-1);
				}
			});
			_churnInt.Start ();

			_list[0].WaitOne ();
		}

		DebugNode AddNode (int base_port)
		{
			IPAddress adrs = _ipgen.Next ();
			VirtualDatagramEventSocket sock = new VirtualDatagramEventSocket (_network, adrs);
			IPEndPoint pubEP = new IPEndPoint (adrs, 10000);
			sock.Bind (new IPEndPoint (IPAddress.Any, pubEP.Port));
			DebugNode node = new DebugNode (Interlocked.Increment (ref _nodeIdx));
			node.Prepare (_ints, sock, base_port, pubEP);
			lock (_list) {
				_list.Add (node);
				_eps.Add (pubEP);
			}
			if (_init_nodes == null) {
				node.Node.KeyBasedRouter.Join (_eps.ToArray ());
				_eps.Add (pubEP);
				if (_eps.Count == 4)
					_init_nodes = _eps.ToArray ();
			} else {
				node.Node.KeyBasedRouter.Join (_init_nodes);
			}
			return node;
		}

		public void Dispose ()
		{
			_churnInt.Dispose ();
			_ints.Dispose ();
			_network.Close ();
			_authNode.Dispose ();
			lock (_list) {
				for (int i = 0; i < _list.Count; i++) {
					try {
						_list[i].Dispose ();
					} catch {}
				}
			}
		}

		class DebugNode : IDisposable
		{
			ECKeyPair _imPrivateKey;
			Key _imPublicKey;
			string _name;
			IHttpServer _server = null;
			WebApp _app;
			Node _node;
			int _idx;

			public DebugNode (int idx)
			{
				_idx = idx;
				_imPrivateKey = ECKeyPair.Create (DefaultAlgorithm.ECDomainName);
				_imPublicKey = Key.Create (_imPrivateKey);
				_name = "Node-" + idx.ToString ("x");
			}

			public void Prepare (Interrupters ints, IDatagramEventSocket bindedDgramSock, int base_port, IPEndPoint ep)
			{
				_name = _name + " (" + ep.ToString () + ")";
				_node = new Node (ints, bindedDgramSock, string.Format ("db{0}{1}.sqlite", Path.DirectorySeparatorChar, _idx));
				_app = new WebApp (_node, _imPublicKey, _imPrivateKey, _name, ints);
				if (base_port >= 0)
					_server = HttpServer.CreateEmbedHttpServer (_app, null, true, true, false, base_port + _idx, 16);
			}

			public void WaitOne ()
			{
				_app.ExitWaitHandle.WaitOne ();
			}

			public Node Node {
				get { return _node; }
			}

			public void Dispose ()
			{
				lock (this) {
					if (_server != null) _server.Dispose ();
					if (_app != null) _app.Dispose ();
					if (_node != null) _node.Dispose ();
					_server = null;
					_app = null;
					_node = null;
				}
			}
		}
		class AuthNode : IDisposable
		{
			ECKeyPair _private;
			Key _pubKey;
			ICaptchaAuthority _captcha;
			Socket _listener;

			public AuthNode (Interrupters ints, int port)
			{
				_private = ECKeyPair.CreatePrivate (DefaultAlgorithm.ECDomainName,
						Convert.FromBase64String ("u8capbI/9NXcJJJkGtdt8LtpBWDeMhcQ0sGNvNO9nmA="));
				_pubKey = Key.Create (_private);
				_captcha = new SimpleCaptcha (new openCrypto.EllipticCurve.Signature.ECDSA (_private));
				_listener = new Socket (AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
				_listener.Bind (new IPEndPoint (IPAddress.Loopback, port));
				_listener.Listen (8);
				new Thread (AcceptThread).Start ();
			}

			void AcceptThread ()
			{
				while (_listener != null) {
					try {
						Socket client = _listener.Accept ();
						new Thread (Process).Start (client);
					} catch {}
				}
			}

			void Process (object o)
			{
				Socket client = (Socket)o;
				try {
					client.SendTimeout = 2000;
					client.ReceiveTimeout = 2000;

					byte[] buf = new byte[2];
					client.Receive (buf, 0, 2, SocketFlags.None);
					int size = (buf[0] << 8) | buf[1];
					buf = new byte[size];
					int recv = 0;
					while (recv < size) {
						int ret = client.Receive (buf, recv, size - recv, SocketFlags.None);
						recv += ret;
					}
					object obj_req = CaptchaContainer.Decrypt (_private, buf);
					object response = null;
					byte[] req_iv, req_key;
					if (obj_req is CaptchaChallengeRequest) {
						CaptchaChallengeRequest req = (CaptchaChallengeRequest)obj_req;
						req_iv = req.IV;
						req_key = req.Key;
						response = _captcha.GetChallenge (req.Hash);
						CaptchaChallengeData data = _captcha.GetChallenge (req.Hash);
					} else if (obj_req is CaptchaAnswer) {
						CaptchaAnswer ans = (CaptchaAnswer)obj_req;
						req_iv = ans.IV;
						req_key = ans.Key;
						response = new CaptchaVerifyResult (_captcha.Verify (ans.Hash, ans.Token, ans.Answer));
					} else {
						client.Send (new byte[2]);
						client.Shutdown (SocketShutdown.Send);
						return;
					}

					byte[] plain = Serializer.Instance.Serialize (response);
					SymmetricKey key = new SymmetricKey (SymmetricAlgorithmType.Camellia, req_iv, req_key, openCrypto.CipherModePlus.CTR, PaddingMode.ISO10126, false);
					byte[] encrypted = key.Encrypt (plain, 0, plain.Length);

					buf[0] = (byte)(encrypted.Length >> 8);
					buf[1] = (byte)(encrypted.Length);
					client.Send (buf, 0, 2, SocketFlags.None);
					int sent = 0;
					while (sent < encrypted.Length) {
						int ret = client.Send (encrypted, sent, encrypted.Length - sent, SocketFlags.None);
						sent += ret;
					}
					client.Shutdown (SocketShutdown.Send);
				} catch {
				} finally {
					try {
						client.Close ();
					} catch {}
				}
			}

			public Key PublicKey {
				get { return _pubKey; }
			}

			public void Dispose ()
			{
				if (_listener != null) {
					_listener.Close ();
					_listener = null;
				}
			}
		}
	}
}
