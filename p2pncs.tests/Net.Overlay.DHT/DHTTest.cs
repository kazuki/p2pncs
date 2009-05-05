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
using System.Net;
using NUnit.Framework;
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.DHT;

namespace p2pncs.tests.Net.Overlay.DHT
{
	[TestFixture]
	public class DHTTest
	{
		[Test]
		public void Test ()
		{
			using (KBREnvironment env = new KBREnvironment (true, false)) {
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
				SimpleDHT[] dhts = new SimpleDHT[env.DistributedHashTables.Count];
				for (int i = 0; i < dhts.Length; i ++) {
					dhts[i] = (SimpleDHT)env.DistributedHashTables[i];
					dhts[i].RegisterTypeID (typeof (string), 0);
					dhts[i].NumberOfReplicas = 1;
				}

				Key reqKey = new Key (new byte[] { 0xb4, 0x07 });
				HashSet<object> expectedSet = new HashSet<object> ();
				expectedSet.Add ("HELLO");
				dhts[0].EndPut (dhts[0].BeginPut (reqKey, TimeSpan.FromHours (1), "HELLO", null, null));
				Assert.IsNull (dhts[0].LocalHashTable.Get (reqKey, 0));
				Assert.IsNull (dhts[1].LocalHashTable.Get (reqKey, 0));
				Assert.IsNull (dhts[2].LocalHashTable.Get (reqKey, 0));
				Assert.IsNull (dhts[3].LocalHashTable.Get (reqKey, 0));
				Assert.IsNull (dhts[4].LocalHashTable.Get (reqKey, 0));
				Assert.IsTrue (expectedSet.SetEquals (dhts[5].LocalHashTable.Get (reqKey, 0)));
				Assert.IsNull (dhts[6].LocalHashTable.Get (reqKey, 0));
				Assert.IsNull (dhts[7].LocalHashTable.Get (reqKey, 0));
				GetResult ret = dhts[0].EndGet (dhts[0].BeginGet (reqKey, 0, null, null));
				Assert.IsNotNull (ret);
				Assert.IsTrue (expectedSet.SetEquals (ret.Values));

				dhts[0].EndPut (dhts[0].BeginPut (reqKey, TimeSpan.FromHours (1), "HOGE", null, null));
				expectedSet.Add ("HOGE");
				Assert.IsNull (dhts[0].LocalHashTable.Get (reqKey, 0));
				Assert.IsNull (dhts[1].LocalHashTable.Get (reqKey, 0));
				Assert.IsNull (dhts[2].LocalHashTable.Get (reqKey, 0));
				Assert.IsNull (dhts[3].LocalHashTable.Get (reqKey, 0));
				Assert.IsNull (dhts[4].LocalHashTable.Get (reqKey, 0));
				Assert.IsTrue (expectedSet.SetEquals (dhts[5].LocalHashTable.Get (reqKey, 0)));
				Assert.IsNull (dhts[6].LocalHashTable.Get (reqKey, 0));
				Assert.IsNull (dhts[7].LocalHashTable.Get (reqKey, 0));
				ret = dhts[0].EndGet (dhts[0].BeginGet (reqKey, 0, null, null));
				Assert.IsNotNull (ret);
				Assert.IsTrue (expectedSet.SetEquals (ret.Values));

				// Put to local only test
				dhts[5].EndPut (dhts[5].BeginPut (reqKey, TimeSpan.FromHours (1), "LOCAL PUT", null, null));
				expectedSet.Add ("LOCAL PUT");
				Assert.IsNull (dhts[0].LocalHashTable.Get (reqKey, 0));
				Assert.IsNull (dhts[1].LocalHashTable.Get (reqKey, 0));
				Assert.IsNull (dhts[2].LocalHashTable.Get (reqKey, 0));
				Assert.IsNull (dhts[3].LocalHashTable.Get (reqKey, 0));
				Assert.IsNull (dhts[4].LocalHashTable.Get (reqKey, 0));
				Assert.IsTrue (expectedSet.SetEquals (dhts[5].LocalHashTable.Get (reqKey, 0)));
				Assert.IsNull (dhts[6].LocalHashTable.Get (reqKey, 0));
				Assert.IsNull (dhts[7].LocalHashTable.Get (reqKey, 0));
				ret = dhts[0].EndGet (dhts[0].BeginGet (reqKey, 0, null, null));
				Assert.IsNotNull (ret);
				Assert.IsTrue (expectedSet.SetEquals (ret.Values));
			}
		}

		[Test]
		public void IPutterEndPointStoreTest ()
		{
			using (KBREnvironment env = new KBREnvironment (true, false)) {
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
				SimpleDHT[] dhts = new SimpleDHT[env.DistributedHashTables.Count];
				for (int i = 0; i < dhts.Length; i++) {
					dhts[i] = (SimpleDHT)env.DistributedHashTables[i];
					dhts[i].RegisterTypeID (typeof (EPStore), 0);
					dhts[i].NumberOfReplicas = 1;
				}

				object[] ary;
				Key reqKey = new Key (new byte[] { 0xb4, 0x07 });
				dhts[0].EndPut (dhts[0].BeginPut (reqKey, TimeSpan.FromHours (1), new EPStore ("HELLO"), null, null));
				ary = dhts[5].LocalHashTable.Get (reqKey, 0);
				Assert.IsNotNull (ary);
				Assert.AreEqual (1, ary.Length);
				Assert.AreEqual ("HELLO", ((EPStore)ary[0]).Message);
				Assert.AreEqual (env.EndPoints[0], ((EPStore)ary[0]).EndPoint);
				GetResult ret = dhts[0].EndGet (dhts[0].BeginGet (reqKey, 0, null, null));
				Assert.IsNotNull (ret);
				ary = ret.Values;
				Assert.AreEqual ("HELLO", ((EPStore)ary[0]).Message);
				Assert.AreEqual (env.EndPoints[0], ((EPStore)ary[0]).EndPoint);
				dhts[5].LocalHashTable.Clear ();

				dhts[5].EndPut (dhts[5].BeginPut (reqKey, TimeSpan.FromHours (1), new EPStore ("HOGE"), null, null));
				ary = dhts[5].LocalHashTable.Get (reqKey, 0);
				Assert.IsNotNull (ary);
				Assert.AreEqual (1, ary.Length);
				Assert.AreEqual ("HOGE", ((EPStore)ary[0]).Message);
				Assert.IsNull (((EPStore)ary[0]).EndPoint);
				ret = dhts[0].EndGet (dhts[0].BeginGet (reqKey, 0, null, null));
				Assert.IsNotNull (ret);
				ary = ret.Values;
				Assert.AreEqual ("HOGE", ((EPStore)ary[0]).Message);
				Assert.AreEqual (env.EndPoints[5], ((EPStore)ary[0]).EndPoint);
			}
		}

		[Serializable]
		class EPStore : IPutterEndPointStore
		{
			EndPoint _ep = null;
			string _msg;
			
			public EPStore (string msg)
			{
				_msg = msg;
			}

			public string Message {
				get { return _msg; }
			}

			public EndPoint EndPoint {
				get { return _ep; }
				set { _ep = value;}
			}
		}
	}
}
