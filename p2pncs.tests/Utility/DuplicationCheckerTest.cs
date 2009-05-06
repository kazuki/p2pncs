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

using NUnit.Framework;
using p2pncs.Utility;

namespace p2pncs.tests.Utility
{
	[TestFixture]
	public class DuplicationCheckerTest
	{
		[Test]
		public void DupCheckTest ()
		{
			DuplicationChecker<int> checker = new DuplicationChecker<int> (5);
			Assert.IsTrue (checker.Check (0));
			Assert.IsFalse (checker.Check (0));
			Assert.IsTrue (checker.Check (1));
			Assert.IsTrue (checker.Check (2));
			Assert.IsTrue (checker.Check (3));
			Assert.IsFalse (checker.Check (2));
		}

		[Test]
		public void SizeLimitTest ()
		{
			DuplicationChecker<int> checker = new DuplicationChecker<int> (5);
			for (int i = 0; i < 5; i ++)
				Assert.IsTrue (checker.Check (i));
			Assert.IsTrue (checker.Check (5));
			for (int i = 1; i < 6; i ++)
				Assert.IsFalse (checker.Check (i));
			Assert.IsTrue (checker.Check (0));
		}
	}
}
