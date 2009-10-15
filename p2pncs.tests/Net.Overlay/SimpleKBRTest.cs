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
using NUnit.Framework;
using p2pncs.Net.Overlay;

namespace p2pncs.tests.Net.Overlay
{
	[TestFixture]
	public class SimpleKBRTest
	{
		Key AppId = KBREnvironment.AppId;

		[Test]
		public void Test ()
		{
			using (KBREnvironment env = new KBREnvironment (false, false)) {
				Key[] keys = new Key[] {
					new Key (new byte[]{0x00, 0x80}),
					new Key (new byte[]{0x00, 0x40}),
					new Key (new byte[]{0x00, 0x20}),
					new Key (new byte[]{0x00, 0x10}),
					new Key (new byte[]{0x00, 0x08}),
					new Key (new byte[]{0x00, 0x04}),
					new Key (new byte[]{0x00, 0x02}),
					new Key (new byte[]{0x00, 0x01}),
				};
				env.AddNodes (keys, null);

				Key reqKey = new Key (new byte[] { 0x00, 0x1F });
				IAsyncResult ar = env.KeyBasedRouters[0].BeginRoute (AppId, reqKey, 1, null, null, null);
				RoutingResult rr = env.KeyBasedRouters[0].EndRoute (ar);
				Assert.IsNotNull (rr);
				Assert.IsNotNull (rr.RootCandidates);
				Assert.AreEqual (1, rr.RootCandidates.Length);
				Assert.AreEqual (keys[3], rr.RootCandidates[0].NodeID);
				Assert.AreEqual (env.EndPoints[3], rr.RootCandidates[0].EndPoint);
			}
		}

		[Test]
		public void RootCandidateTest ()
		{
			Random rnd = new Random ();
			int keyBytes = 24;
			using (KBREnvironment env = new KBREnvironment (false, false)) {
				Key[] keys = new Key[1024];
				HashSet<Key> keySet = new HashSet<Key> ();
				for (int i = 0; i < keys.Length; i++) {
					keys[i] = Key.CreateRandom (keyBytes);
					while (!keySet.Add (keys[i]))
						keys[i] = Key.CreateRandom (keyBytes);
				}
				env.AddNodes (keys, null);
				for (int i = 0; i < keys.Length; i ++) {
					env.KeyBasedRouters[i].RoutingAlgorithm.Stabilize (AppId);
					System.Threading.Thread.Sleep (10);
				}
				System.Threading.Thread.Sleep (500);
				
				int numOfRootCandidates = 3, TestCount = 100;
				int[] success_candidates = new int[numOfRootCandidates];
				int success_count = 0;
				for (int testLoop = 0; testLoop < TestCount; testLoop++) {
					Key target = Key.CreateRandom (keys[0].KeyBytes);
					List<IKeyBasedRouter> sorted;
					lock (env) {
						sorted = new List<IKeyBasedRouter> (env.KeyBasedRouters);
					}
					sorted.Sort (delegate (IKeyBasedRouter x, IKeyBasedRouter y) {
						Key diffX = target ^ x.RoutingAlgorithm.SelfNodeHandle.NodeID;
						Key diffY = target ^ y.RoutingAlgorithm.SelfNodeHandle.NodeID;
						return diffX.CompareTo (diffY);
					});

					IKeyBasedRouter kbrNode = env.KeyBasedRouters[0];
					DateTime dt = DateTime.Now;
					RoutingResult result = kbrNode.EndRoute (kbrNode.BeginRoute (AppId, target, numOfRootCandidates, null, null, null));
					Assert.IsNotNull (result);
					Assert.IsNotNull (result.RootCandidates);
					for (int i = 0; i < Math.Min (numOfRootCandidates, result.RootCandidates.Length); i++)
						if (Key.Equals (sorted[i].RoutingAlgorithm.SelfNodeHandle.NodeID, result.RootCandidates[i].NodeID))
							success_count ++;
				}
				Console.WriteLine ("{0} / {1}", success_count, (TestCount * numOfRootCandidates * 90 / 100));
				Assert.IsTrue (success_count >= (TestCount * numOfRootCandidates * 90 / 100));
			}
		}

		[Test]
		public void SelfRootTest ()
		{
			using (KBREnvironment env = new KBREnvironment (false, false)) {
				Key[] keys = new Key[] {
					new Key (new byte[]{0x00, 0x80}),
					new Key (new byte[]{0x00, 0x40}),
					new Key (new byte[]{0x00, 0x20}),
					new Key (new byte[]{0x00, 0x10}),
					new Key (new byte[]{0x00, 0x08}),
					new Key (new byte[]{0x00, 0x04}),
					new Key (new byte[]{0x00, 0x02}),
					new Key (new byte[]{0x00, 0x01}),
				};
				env.AddNodes (keys, null);

				Key reqKey = new Key (new byte[] {0x00, 0x81});
				IAsyncResult ar = env.KeyBasedRouters[0].BeginRoute (AppId, reqKey, 1, null, null, null);
				RoutingResult rr = env.KeyBasedRouters[0].EndRoute (ar);
				Assert.IsNotNull (rr, "#1");
				Assert.IsNotNull (rr.RootCandidates, "#2");
				Assert.AreEqual (1, rr.RootCandidates.Length, "#3");
				Assert.AreEqual (keys[0], rr.RootCandidates[0].NodeID, "#4");
				Assert.IsNull (rr.RootCandidates[0].EndPoint, "#5");

				reqKey = new Key (new byte[] {0x00, 0x81});
				ar = env.KeyBasedRouters[0].BeginRoute (AppId, reqKey, 2, null, null, null);
				rr = env.KeyBasedRouters[0].EndRoute (ar);
				Assert.IsNotNull (rr, "#6");
				Assert.IsNotNull (rr.RootCandidates, "#7");
				Assert.AreEqual (2, rr.RootCandidates.Length, "#8");
				Assert.AreEqual (keys[0], rr.RootCandidates[0].NodeID, "#9");
				Assert.AreEqual (keys[7], rr.RootCandidates[1].NodeID, "#a");
				Assert.IsNull (rr.RootCandidates[0].EndPoint, "#b");
				Assert.AreEqual (env.EndPoints[7], rr.RootCandidates[1].EndPoint, "#c");

				reqKey = new Key (new byte[] {0x00, 0x80});
				ar = env.KeyBasedRouters[0].BeginRoute (AppId, reqKey, 2, null, null, null);
				rr = env.KeyBasedRouters[0].EndRoute (ar);
				Assert.IsNotNull (rr, "#d");
				Assert.IsNotNull (rr.RootCandidates, "#e");
				Assert.AreEqual (2, rr.RootCandidates.Length, "#f");
				Assert.AreEqual (keys[0], rr.RootCandidates[0].NodeID, "#10");
				Assert.AreEqual (keys[7], rr.RootCandidates[1].NodeID, "#11");
				Assert.IsNull (rr.RootCandidates[0].EndPoint, "#12");
				Assert.AreEqual (env.EndPoints[7], rr.RootCandidates[1].EndPoint, "#13");
			}
		}
	}
}
