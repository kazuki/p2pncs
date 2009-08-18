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
using System.Net.Sockets;
using p2pncs.Net;
using NUnit.Framework;

namespace p2pncs.tests.Net
{
	[TestFixture]
	public class SimplePublicIPAddressVotingBoxTest
	{
		[Test]
		public void Test ()
		{
			SimplePublicIPAddressVotingBox voting = new SimplePublicIPAddressVotingBox (AddressFamily.InterNetwork);
			IPAddress pubIP1 = IPAddress.Parse ("1.2.3.4");
			IPAddress pubIP2 = IPAddress.Parse ("1.2.3.5");
			IPAddress pubIP3 = IPAddress.Parse ("1.2.3.6");
			Assert.AreEqual (IPAddress.None, voting.CurrentPublicIPAddress, "#0");
			voting.Vote (new IPEndPoint (IPAddress.Parse ("1.1.1.1"), 1000), pubIP1);
			Assert.AreEqual (pubIP1, voting.CurrentPublicIPAddress, "#1");
			voting.Vote (new IPEndPoint (IPAddress.Parse ("1.1.1.2"), 1000), pubIP1);
			Assert.AreEqual (pubIP1, voting.CurrentPublicIPAddress, "#2");
			voting.Vote (new IPEndPoint (IPAddress.Parse ("1.1.1.3"), 1000), pubIP2);
			Assert.AreEqual (pubIP1, voting.CurrentPublicIPAddress, "#3");
			voting.Vote (new IPEndPoint (IPAddress.Parse ("1.1.1.4"), 1000), pubIP2);
			Assert.AreEqual (pubIP2, voting.CurrentPublicIPAddress, "#4");
			voting.Vote (new IPEndPoint (IPAddress.Parse ("1.1.1.4"), 1000), pubIP1);
			Assert.AreEqual (pubIP2, voting.CurrentPublicIPAddress, "#5");
			voting.Vote (new IPEndPoint (IPAddress.Parse ("1.1.1.4"), 1000), pubIP1);
			Assert.AreEqual (pubIP2, voting.CurrentPublicIPAddress, "#6");
			voting.Vote (new IPEndPoint (IPAddress.Parse ("1.1.1.4"), 1000), pubIP1);
			Assert.AreEqual (pubIP2, voting.CurrentPublicIPAddress, "#7");
			voting.Vote (new IPEndPoint (IPAddress.Parse ("1.1.1.5"), 1000), pubIP3);
			Assert.AreEqual (pubIP2, voting.CurrentPublicIPAddress, "#8");
			voting.Vote (new IPEndPoint (IPAddress.Parse ("1.1.1.6"), 1000), pubIP1);
			Assert.AreEqual (pubIP2, voting.CurrentPublicIPAddress, "#9");
			voting.Vote (new IPEndPoint (IPAddress.Parse ("1.1.1.7"), 1000), pubIP1);
			Assert.AreEqual (pubIP1, voting.CurrentPublicIPAddress, "#10");
		}
	}
}
