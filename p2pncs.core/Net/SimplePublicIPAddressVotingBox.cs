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
using System.Net;
using System.Net.Sockets;

namespace p2pncs.Net
{
	public class SimplePublicIPAddressVotingBox : IPublicIPAddressVotingBox
	{
		const int HISTORY_SIZE = 2;
		IPAddress _cur;
		Queue<KeyValuePair<IPAddress, IPAddress>> _history = new Queue<KeyValuePair<IPAddress, IPAddress>> (HISTORY_SIZE + 1);

		public SimplePublicIPAddressVotingBox (AddressFamily family)
		{
			_cur = IPAddressUtility.GetNoneAddress (family);
		}

		public void Vote (IPEndPoint voter, IPAddress ip)
		{
			lock (_history) {
				int equals = 0;
				bool equals2 = false;
				if (_history.Count > 0) {
					IPAddress cur_value = _history.Peek ().Value;
					foreach (KeyValuePair<IPAddress, IPAddress> entry in _history) {
						if (entry.Key.Equals (voter.Address))
							return;
						if (!cur_value.Equals (entry.Value)) {
							if (equals != 1) {
								equals = -1;
								break;
							}
						} else {
							equals++;
						}
						cur_value = entry.Value;
					}
					equals2 = cur_value.Equals (ip);
					if (_history.Count == HISTORY_SIZE)
						_history.Dequeue ();
				}
				_history.Enqueue (new KeyValuePair<IPAddress, IPAddress> (voter.Address, ip));
				if (_history.Count == 1) {
					_cur = ip;
					Logger.Log (LogLevel.Info, this, "Update PublicIP to {0}", ip);
				} else {
					if (equals == -1)
						return;
					if (equals2 && !_cur.Equals (ip)) {
						_cur = ip;
						Logger.Log (LogLevel.Info, this, "Update PublicIP to {0}", ip);
					}
				}
			}
		}

		public IPAddress CurrentPublicIPAddress {
			get { return _cur; }
		}
	}
}
