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
	/// データグラム専用のソケット。
	/// ただし、データグラムの受信は
	/// <see cref="System.Net.Sockets.Socket"/>
	/// と異なりReceiveFromメソッドを使うのではなく、
	/// Receivedイベントをハンドリングすることによって行う。
	/// </summary>
	public interface IDatagramEventSocket : IDisposable
	{
		/// <summary>
		/// 関連づけるローカルのエンドポイントを指定します。
		/// </summary>
		/// <param name="bindEP">関連づけるローカルエンドポイント</param>
		void Bind (EndPoint bindEP);

		/// <summary>
		/// ソケットをクローズしすべてのリソースを解放します
		/// </summary>
		void Close ();

		/// <summary>
		/// データグラムを指定されたエンドポイントへ送信します
		/// </summary>
		/// <param name="buffer">送信するデータグラム</param>
		/// <param name="remoteEP">宛先となるエンドポイント</param>
		void SendTo (byte[] buffer, EndPoint remoteEP);

		/// <summary>
		/// データグラムを指定されたエンドポイントへ送信します
		/// </summary>
		/// <param name="buffer">送信するデータグラムが含まれる配列</param>
		/// <param name="offset">送信するデータの開始位置</param>
		/// <param name="size">送信するデータサイズ</param>
		/// <param name="remoteEP">宛先となるエンドポイント</param>
		void SendTo (byte[] buffer, int offset, int size, EndPoint remoteEP);

		/// <summary>
		/// データグラムの受信時に発生するイベント
		/// </summary>
		event DatagramReceiveEventHandler Received;
	}
}
