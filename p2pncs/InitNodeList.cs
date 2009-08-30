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

using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using p2pncs.Net.Overlay;

namespace p2pncs
{
	class InitNodeList
	{
		const int MaxNodeListSize = 64;
		const string NodeListFileName = "nodelist.txt";
		PortOpenChecker _checker;

		public InitNodeList (PortOpenChecker checker)
		{
			_checker = checker;
		}

		public void Load ()
		{
			if (!File.Exists (NodeListFileName))
				return;

			try {
				List<EndPoint> list = new List<EndPoint> ();
				using (StreamReader reader = new StreamReader (NodeListFileName, Encoding.ASCII)) {
					string line;
					while ((line = reader.ReadLine ()) != null) {
						try {
							list.Add (EndPointObfuscator.Decode (line));
						} catch { }
					}
				}
				_checker.Join (list.ToArray ());
			} catch {}
		}

		public void Save ()
		{
			NodeHandle[] nodes = _checker.KeyBasedRouter.RoutingAlgorithm.GetRandomNodes (MaxNodeListSize);
			using (StreamWriter writer = new StreamWriter (NodeListFileName, false, Encoding.ASCII)) {
				for (int i = 0; i < nodes.Length; i ++) {
					writer.WriteLine (EndPointObfuscator.Encode (nodes[i].EndPoint));
				}
			}
		}
	}
}
