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
using System.Threading;
using p2pncs.Net.Overlay;

namespace p2pncs.Evaluation
{
	class KBREval1 : IEvaluator
	{
		public void Evaluate (EvalOptionSet opt)
		{
			using (EvalEnvironment env = new EvalEnvironment (opt)) {
				env.AddNodes (opt.NumberOfNodes, true);
				env.StartChurn ();

				VirtualNode testNode = env.Nodes[0];

				int returned = 0;
				int[] candidates = new int[3];
				ManualResetEvent reqDone = new ManualResetEvent (false);
				for (int i = 0; i < opt.Tests; i++) {
					Key target = Key.CreateRandom (testNode.NodeID.KeyBytes);
					testNode.KeyBasedRouter.BeginRoute (target, null, candidates.Length, 4, delegate (IAsyncResult ar) {
						Key target2 = ar.AsyncState as Key;
						RoutingResult result = testNode.KeyBasedRouter.EndRoute (ar);
						if (result != null && result.RootCandidates != null && result.RootCandidates.Length > 0) {
							VirtualNode[] nodes;
							lock (env.Nodes) {
								nodes = env.Nodes.ToArray ();
							}
							Array.Sort<VirtualNode> (nodes, delegate (VirtualNode x, VirtualNode y) {
								Key diff1 = testNode.KeyBasedRouter.RoutingAlgorithm.ComputeDistance (target2, x.NodeID);
								Key diff2 = testNode.KeyBasedRouter.RoutingAlgorithm.ComputeDistance (target2, y.NodeID);
								return diff1.CompareTo (diff2);
							});
							for (int k = 0; k < Math.Min (candidates.Length, result.RootCandidates.Length); k ++) {
								if (nodes[k].NodeID.Equals (result.RootCandidates[k].NodeID))
									candidates[k] ++;
							}
						}
						int ret = Interlocked.Increment (ref returned);
						if (ret == opt.Tests)
							reqDone.Set ();
						Console.Write ("*");
					}, target);
					Thread.Sleep (TimeSpan.FromSeconds (0.2));
				}
				reqDone.WaitOne ();
				for (int i = 0; i < candidates.Length; i ++)
					Console.Write ("{0}/", candidates[i]);
				Console.WriteLine ("{0}", returned);
			}
		}
	}
}
