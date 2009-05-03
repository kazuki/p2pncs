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
using System.Net;

namespace p2pncs.Net
{
	/// <summary>
	/// リモートエンドポイントに対して、メッセージを送信したり、
	/// リクエストとレスポンスで構成される問い合わせを行うソケット
	/// </summary>
	public interface IMessagingSocket : IDisposable
	{
		/// <summary>
		/// メッセージを送信します (到達保証なし)
		/// </summary>
		/// <param name="obj">送信するメッセージ</param>
		/// <param name="remoteEP">送信先のエンドポイント</param>
		void Send (object obj, EndPoint remoteEP);

		event ReceivedEventHandler Received;

		/// <summary>
		/// 非同期問い合わせを開始します
		/// </summary>
		/// <param name="obj">リクエストとなるメッセージ</param>
		/// <param name="remoteEP">問い合わせ先のエンドポイント</param>
		/// <param name="callback">問い合わせ完了時に呼び出す<see cref="System.AsyncCallback"/>デリゲート</param>
		/// <param name="state">コールバック時に渡される状態を表す<see cref="System.Object"/></param>
		/// <returns>この非同期問い合わせの状態を表す<see cref="System.IAsyncResult"/></returns>
		IAsyncResult BeginInquire (object obj, EndPoint remoteEP, AsyncCallback callback, object state);

		/// <summary>
		/// 非同期問い合わせを終了します
		/// </summary>
		/// <param name="ar">この非同期問い合わせの状態を表す<see cref="System.IAsyncResult"/></param>
		/// <returns>問い合わせに対するレスポンス。問い合わせに失敗した場合は null が返る</returns>
		object EndInquire (IAsyncResult ar);

		event InquiredEventHandler Inquired;
		void StartResponse (InquiredEventArgs args, object response);

		event InquiredEventHandler InquiryFailure;
		event InquiredEventHandler InquirySuccess;

		IDatagramEventSocket BaseSocket { get; }

		void Close ();
	}
}
