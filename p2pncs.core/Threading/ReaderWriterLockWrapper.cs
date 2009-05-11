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
using System.Threading;

namespace p2pncs.Threading
{
	public class ReaderWriterLockWrapper : IDisposable
	{
		ReaderWriterLockSlim _lock;

		public ReaderWriterLockWrapper ()
		{
			_lock = new ReaderWriterLockSlim (LockRecursionPolicy.NoRecursion);
		}

		public IDisposable EnterReadLock ()
		{
			return new ReadLockCookie (this);
		}

		public IDisposable EnterUpgradeableReadLock ()
		{
			return new UpgradeableReadLockCookie (this);
		}

		public IDisposable EnterWriteLock ()
		{
			return new WriteLockCookie (this);
		}

		public void Dispose ()
		{
			if (_lock != null) {
				_lock.Dispose ();
				_lock = null;
			}
		}

		class ReadLockCookie : IDisposable
		{
			ReaderWriterLockWrapper _owner;

			public ReadLockCookie (ReaderWriterLockWrapper owner)
			{
				_owner = owner;
				_owner._lock.EnterReadLock ();
			}

			public void Dispose ()
			{
				_owner._lock.ExitReadLock ();
			}
		}

		class UpgradeableReadLockCookie : IDisposable
		{
			ReaderWriterLockWrapper _owner;

			public UpgradeableReadLockCookie (ReaderWriterLockWrapper owner)
			{
				_owner = owner;
				_owner._lock.EnterUpgradeableReadLock ();
			}

			public void Dispose ()
			{
				_owner._lock.ExitUpgradeableReadLock ();
			}
		}

		class WriteLockCookie : IDisposable
		{
			ReaderWriterLockWrapper _owner;

			public WriteLockCookie (ReaderWriterLockWrapper owner)
			{
				_owner = owner;
				_owner._lock.EnterWriteLock ();
			}

			public void Dispose ()
			{
				_owner._lock.ExitWriteLock ();
			}
		}
	}
}
