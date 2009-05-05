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
using NUnit.Framework;
using p2pncs.Net.Overlay;

namespace p2pncs.tests.Net.Overlay
{
	[TestFixture]
	public class SimpleKBRTest
	{
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
				IAsyncResult ar = env.KeyBasedRouters[0].BeginRoute (reqKey, null, 1, 3, null, null);
				RoutingResult rr = env.KeyBasedRouters[0].EndRoute (ar);
				Assert.IsNotNull (rr);
				Assert.IsNotNull (rr.RootCandidates);
				Assert.AreEqual (1, rr.RootCandidates.Length);
				Assert.AreEqual (keys[3], rr.RootCandidates[0].NodeID);
				Assert.AreEqual (env.EndPoints[3], rr.RootCandidates[0].EndPoint);
			}
		}
	}
}
