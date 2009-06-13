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
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.DHT;
using p2pncs.Threading;

namespace p2pncs.Evaluation
{
	class MassKeyEval1 : IEvaluator
	{
		public void Evaluate (EvalOptionSet opt)
		{
			const int KEYS = 100;
			DateTime expiry = DateTime.Now + TimeSpan.FromHours (1);
			IntervalInterrupter timer = new IntervalInterrupter (TimeSpan.FromSeconds (1), "TIMER");
			for (int method = 0; method <= 1; method ++) {
				using (EvalEnvironment env = new EvalEnvironment (opt)) {
					env.AddNodes (opt.NumberOfNodes, true);
					for (int i = 0; i < env.Nodes.Count; i ++) env.Nodes[i].KeyBasedRouter.RoutingAlgorithm.Stabilize ();
					for (int i = 0; i < env.Nodes.Count; i++) env.Nodes[i].KeyBasedRouter.RoutingAlgorithm.Stabilize ();
					env.KBRStabilizeTimer.Stop ();
					long lastPacket = env.Network.Packets, lastTraffic = env.Network.TotalTraffic;
					while (true) {
						Thread.Sleep (1000);
						if (env.Network.Packets == lastPacket)
							break;
						lastPacket = env.Network.Packets;
						lastTraffic = env.Network.TotalTraffic;
					}
					List<Key> list = new List<Key> ();
					MassKeyDeliverer[] delivers = new MassKeyDeliverer[env.Nodes.Count];
					Console.Write ("Generating {0} keys...", KEYS * opt.NumberOfNodes);
					for (int i = 0; i < KEYS * opt.NumberOfNodes; i ++)
						list.Add (Key.CreateRandom (env.Nodes[0].NodeID.KeyBytes));
					Console.WriteLine ("ok");
					for (int i = 0; i < env.Nodes.Count; i ++) {
						if (method == 1) {
							delivers[i] = new MassKeyDeliverer (env.Nodes[i].KeyBasedRouter,
								env.Nodes[i].LocalDistributedHashTable as IMassKeyDelivererLocalStore, timer);
							for (int k = 0; k < KEYS; k++) {
								env.Nodes[i].LocalDistributedHashTable.Put (list[i * KEYS + k], 0, expiry, Convert.ToBase64String (list[i * KEYS + k].GetByteArray ()));
							}
						} else {
							WaitHandle[] waits = new WaitHandle[KEYS];
							for (int k = 0; k < KEYS; k ++) {
								IAsyncResult ar = env.Nodes[i].DistributedHashTable.BeginPut (list[i * KEYS + k], TimeSpan.FromHours (1), Convert.ToBase64String (list[i * KEYS + k].GetByteArray ()), null, null);
								waits[k] = ar.AsyncWaitHandle;
							}
							foreach (WaitHandle wh in waits)
								wh.WaitOne ();
							Console.WriteLine ("{0} / {1}", i + 1, env.Nodes.Count);
						}
					}
					if (method == 1)
						timer.Start ();
					long lastPacket2 = env.Network.Packets, lastTraffic2 = env.Network.TotalTraffic;
					while (true) {
						Thread.Sleep (10000);
						if (env.Network.Packets == lastPacket2)
							break;
						lastPacket2 = env.Network.Packets;
						lastTraffic2 = env.Network.TotalTraffic;
					}
					Console.WriteLine ("Method={0}", method);
					Console.WriteLine ("  Total Packets: {0}", lastPacket2 - lastPacket);
					Console.WriteLine ("  Total Traffic: {0}", lastTraffic2 - lastTraffic);
				}
			}
			Console.ReadLine ();
		}
	}
}
