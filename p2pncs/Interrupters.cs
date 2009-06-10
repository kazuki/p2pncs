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
using p2pncs.Threading;

namespace p2pncs
{
	class Interrupters : IDisposable
	{
		IntervalInterrupter _msgInt, _dhtInt, _kbrInt, _anonInt, _updateCheckInt, _messagingInt, _streamTimeoutInt;

		public Interrupters ()
		{
			_msgInt = new IntervalInterrupter (TimeSpan.FromMilliseconds (100), "MessagingSocket Interval Interrupter");
			_dhtInt = new IntervalInterrupter (TimeSpan.FromSeconds (1), "DHT Timeout Check Interrupter");
			_kbrInt = new IntervalInterrupter (TimeSpan.FromSeconds (10), "KBR Stabilize Interval Interrupter");
			_anonInt = new IntervalInterrupter (TimeSpan.FromMilliseconds (100), "AnonymousRouter Timeout Check Interrupter");
			_updateCheckInt = new IntervalInterrupter (TimeSpan.FromMilliseconds (500), "WebApp UpdateChecker");
			_messagingInt = new IntervalInterrupter (TimeSpan.FromMilliseconds (100), "AnonymousMessagingSocket RetryTimer");
			_streamTimeoutInt = new IntervalInterrupter (TimeSpan.FromMilliseconds (100), "StreamSocket TimeoutTimer");

			_msgInt.Start ();
			_dhtInt.Start ();
			_kbrInt.Start ();
			_anonInt.Start ();
			_updateCheckInt.Start ();
			_messagingInt.Start ();
			_streamTimeoutInt.Start ();
		}

		public IntervalInterrupter MessagingInt {
			get { return _msgInt; }
		}

		public IntervalInterrupter DHTInt {
			get { return _dhtInt; }
		}

		public IntervalInterrupter KBRStabilizeInt {
			get { return _kbrInt; }
		}

		public IntervalInterrupter AnonymousInt {
			get { return _anonInt; }
		}

		public IntervalInterrupter WebAppInt {
			get { return _updateCheckInt; }
		}

		public IntervalInterrupter AnonymousMessagingInt {
			get { return _messagingInt; }
		}

		public IntervalInterrupter StreamSocketTimeoutInt {
			get { return _streamTimeoutInt; }
		}

		public void Dispose ()
		{
			_msgInt.Dispose ();
			_dhtInt.Dispose ();
			_kbrInt.Dispose ();
			_anonInt.Dispose ();
			_updateCheckInt.Dispose ();
			_messagingInt.Dispose ();
			_streamTimeoutInt.Dispose ();
		}
	}
}
