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

namespace p2pncs.Evaluation
{
	class DHTEval1 : IEvaluator
	{
		public void Evaluate (EvalOptionSet opt)
		{
			using (EvalEnvironment env = new EvalEnvironment (opt)) {
				env.AddNodes (opt.NumberOfNodes, true);
				env.StartChurn ();

				VirtualNode testNode = env.Nodes[0];
				List<Key> list = new List<Key> ();
				for (int i = 0; i < opt.Tests; i++) {
					Key key = Key.CreateRandom (testNode.NodeID.KeyBytes);
					list.Add (key);
					testNode.DistributedHashTable.BeginPut (key, TimeSpan.FromHours (1), Convert.ToBase64String (key.GetByteArray ()), null, null);
					Thread.Sleep (TimeSpan.FromSeconds (0.2));
				}

				int returned = 0, successed = 0;
				ManualResetEvent getDone = new ManualResetEvent (false);
				for (int i = 0; i < opt.Tests; i++) {
					testNode.DistributedHashTable.BeginGet (list[i], typeof (string), delegate (IAsyncResult ar) {
						GetResult result = testNode.DistributedHashTable.EndGet (ar);
						string expected = ar.AsyncState as string;
						if (result != null && result.Values != null && result.Values.Length > 0) {
							if (expected.Equals (result.Values[0] as string)) {
								Interlocked.Increment (ref successed);
								Console.Write ("*");
							} else {
								Console.Write ("=");
							}
						} else {
							Console.Write ("?");
						}
						if (Interlocked.Increment (ref returned) == opt.Tests)
							getDone.Set ();
					}, Convert.ToBase64String (list[i].GetByteArray ()));
					Thread.Sleep (TimeSpan.FromSeconds (0.2));
				}
				getDone.WaitOne ();
				Console.WriteLine ("{0}/{1}", successed, returned);
			}
		}
	}
}
