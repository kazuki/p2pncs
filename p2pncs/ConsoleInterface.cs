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

namespace p2pncs
{
	class ConsoleInterface
	{
		public ConsoleInterface ()
		{
		}

		public void Run (Program prog)
		{
			try {
				prog.Started += delegate (object sender, EventArgs args) {
					Console.WriteLine ("正常に起動しました。");
					Console.WriteLine ("ブラウザで {0} を開いてください。", prog.Url);
					Console.WriteLine ();
					Console.WriteLine ("注意: このコマンドプロンプトウィンドウは閉じないでください。");
					Console.WriteLine ("プログラムを終了するときは、左側のメニューから[ネットワーク]→[終了]を選ぶか、");
					Console.WriteLine ("{0}net/exit を開いて、\"終了する\"ボタンを押してください。", prog.Url);
				};
				prog.Run ();
			} catch (ConfigFileInitializedException) {
				Console.WriteLine ("設定ファイルを保存しました。");
				Console.WriteLine ("README.txt を参考に設定ファイルを編集してください。");
				Console.WriteLine ();
				Console.WriteLine ("エンターキーを押すと終了します");
				Console.ReadLine ();
				return;
			} catch (DllNotFoundException) {
				Console.WriteLine ("起動に必要なDLLが見つかりません。再インストールしてみてください。");
				return;
			}
		}
	}
}
