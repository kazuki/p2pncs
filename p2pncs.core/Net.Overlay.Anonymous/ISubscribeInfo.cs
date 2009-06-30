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

using openCrypto.EllipticCurve;

namespace p2pncs.Net.Overlay.Anonymous
{
	public interface ISubscribeInfo
	{
		event AcceptingEventHandler Accepting;
		event AcceptedEventHandler Accepted;

		Key Key { get; }
		ECKeyPair PrivateKey { get; }
		SubscribeRouteStatus Status { get; }
		IAnonymousRouter AnonymousRouter { get; }

		/// <remarks>
		/// IMessagingSocketインターフェイスを持つが、動作は通常のIMessagingSocketと異なり、
		/// BeginInquireで送信したメッセージは、IAnonymousRouter.AddBoundaryNodeReceivedEventHandlerで
		/// 登録したハンドラによって処理される。
		/// また、Sendで送信したメッセージは始点・終点間での再送処理は行われないが、
		/// 中継ノード間では行われ、利用する経路数は強制的に1になる。
		/// こちらも同様にIAnonymousRouter.AddBoundaryNodeReceivedEventHandlerで登録したハンドラによって
		/// 処理される。(但し、BoundaryNodeReceivedEventArgs.NeedsResponse = falseとなっている)
		/// 
		/// BoundaryNodeReceivedEventArgs.Send によって送信された始点へのメッセージは、
		/// 通常通りIMessagingSocket.AddReceivedHandlerによって登録されたハンドラで処理される。
		/// </remarks>
		IMessagingSocket MessagingSocket { get; }
	}
}
