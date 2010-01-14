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
using System.Collections.Generic;
using openCrypto.EllipticCurve;
using p2pncs.Security.Cryptography;

namespace p2pncs.Net.Overlay.Anonymous
{
	public partial class AnonymousRouter
	{
		partial class AnonymousEndPoint : IAnonymousEndPoint
		{
			AnonymousRouter _router;
			AnonymousEndPointOptions _opts;
			Key _address;
			ECKeyPair _privateKey;
			bool _active = true;

			HashSet<MCRStartInfo> _establishedList = new HashSet<MCRStartInfo> ();
			HashSet<MCRStartInfo> _establishingList = new HashSet<MCRStartInfo> ();

			public AnonymousEndPoint (AnonymousRouter router, Key address, ECKeyPair privateKey, AnonymousEndPointOptions opts)
			{
				_router = router;
				_address = address;
				_privateKey = privateKey;
				_opts = opts;

				// adjustment
				if (opts.MinNumberOfRelays < 0)
					opts.MinNumberOfRelays = 0;
				if (opts.MaxNumberOfRelays < opts.MinNumberOfRelays)
					opts.MaxNumberOfRelays = opts.MinNumberOfRelays;
				if (opts.MinNumberOfRelays == 0 && opts.MaxNumberOfRelays == 0) {
					opts.NumberOfRoutes = 1;
					opts.Factor = 1.0f;
				}
			}

			public void Init ()
			{
				CheckRoutes ();
			}

			public void Close ()
			{
				_active = false;
				using (_router._routesLock.EnterWriteLock ()) {
					lock (_establishedList) {
						_establishedList.UnionWith (_establishingList);
						foreach (MCRStartInfo startInfo in _establishedList) {
							_router._routes.Remove (new MCREndPoint (startInfo.RelayNodes[0].EndPoint, startInfo.Label));
							startInfo.Close ();
						}
						_establishedList.Clear ();
						_establishingList.Clear ();
					}
				}
				_router.RemoveEndPoint (this);
			}

			public void CheckRoutes ()
			{
				if (!_active)
					return;

				lock (_establishedList) {
					if (_establishedList.Count < _opts.NumberOfRoutes) {
						Random rnd = new Random ();
						int expectedCount = (int)Math.Ceiling ((_opts.NumberOfRoutes - _establishedList.Count) * _opts.Factor);
						int count = expectedCount - _establishingList.Count;
						while (count-- > 0) {
							NodeHandle[] relays = _router._kbrAlgo.GetRandomNodes (_opts.AppId, rnd.Next (_opts.MinNumberOfRelays, _opts.MaxNumberOfRelays + 1));
							if (relays.Length < _opts.MinNumberOfRelays) {
								Logger.Log (LogLevel.Trace, this, "Relay node selection failed");
								return;
							}

							if (relays.Length == 0) {
								// Non-Anonymous Mode
								_establishedList.Add (new MCRStartInfo (this, new NodeHandle[0], new SymmetricKey[0], 0));
							} else {
								SymmetricKey[] relayKeys = new SymmetricKey[relays.Length];
								byte[] encrypted = MCRCipherUtility.CreateEstablishMessageData (relays, relayKeys, _address, _privateKey.DomainName, SymmetricCryptOption, EstablishingPayloadSize);
								MCRStartInfo routeInfo;
								using (_router._routesLock.EnterWriteLock ()) {
									while (true) {
										MCREndPoint ep = new MCREndPoint (relays[0].EndPoint, GenerateRouteLabel ());
										if (!_router._routes.ContainsKey (ep)) {
											routeInfo = new MCRStartInfo (this, relays, relayKeys, ep.Label);
											_router._routes.Add (ep, routeInfo);
											break;
										}
									}
								}
								_establishingList.Add (routeInfo);
								object msg = new EstablishRouteMessage (routeInfo.Label, encrypted);
								_router._sock.BeginInquire (msg, routeInfo.RelayNodes[0].EndPoint, delegate (IAsyncResult ar) {
									if (_router._sock.EndInquire (ar) != null)
										return;
									using (_router._routesLock.EnterWriteLock ()) {
										_router._routes.Remove (new MCREndPoint (routeInfo.RelayNodes[0].EndPoint, routeInfo.Label));
									}
									Closed (routeInfo);
								}, null);
							}
						}
					}
				}
			}

			void Received (MCRStartInfo startInfo, object msg)
			{
				if (!_active)
					return;

				if (msg is EstablishedRouteMessage) {
					Received (startInfo, (EstablishedRouteMessage)msg);
					return;
				}
			}

			void Received (MCRStartInfo startInfo, EstablishedRouteMessage msg)
			{
				startInfo.Established = true;
				lock (_establishedList) {
					if (!_establishingList.Remove (startInfo))
						return;
					_establishedList.Add (startInfo);
				}
				Logger.Log (LogLevel.Trace, _router, "MCR: Established");
			}

			void Closed (MCRStartInfo startInfo)
			{
				lock (_establishedList) {
					_establishingList.Remove (startInfo);
					_establishedList.Remove (startInfo);
				}
				CheckRoutes ();
			}

			#region IAnonymousEndPoint Members

			public IAsyncResult BeginConnect (Key dest, object payload, object opts, AsyncCallback callback, object state)
			{
				throw new NotImplementedException ();
			}

			public IAnonymousSocket EndConnect (IAsyncResult ar)
			{
				throw new NotImplementedException ();
			}

			public IAsyncResult BeginDelegateTask (object msg, IDelegateTaskOption opts, AsyncCallback callback, object state)
			{
				throw new NotImplementedException ();
			}

			public object EndDelegateTask (IAsyncResult ar)
			{
				throw new NotImplementedException ();
			}

			public Key Address {
				get { return _address; }
			}

			public ECKeyPair PrivateKey {
				get { return _privateKey; }
			}

			public object Options {
				get { return _opts; }
			}

			#endregion
		}

		public class AnonymousEndPointOptions
		{
			public AnonymousEndPointOptions ()
			{
				NumberOfRelays = 2;
				NumberOfRoutes = 2;
				Factor = 1.0f;
			}

			public int NumberOfRoutes { get; set; }
			public int MinNumberOfRelays { get; set; }
			public int MaxNumberOfRelays { get; set; }
			public float Factor { get; set; }
			public int NumberOfRelays {
				get { return (MinNumberOfRelays + MaxNumberOfRelays) / 2; }
				set { MinNumberOfRelays = MaxNumberOfRelays = value; }
			}
			public Key AppId { get; set; }
		}
	}
}
