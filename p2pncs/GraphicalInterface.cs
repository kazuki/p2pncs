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
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace p2pncs
{
	class GraphicalInterface
	{
		public GraphicalInterface ()
		{
		}

		public void Run (Program prog)
		{
			ManualResetEvent startupDone = new ManualResetEvent (false);
			using (NotifyIconWrapper tray = new NotifyIconWrapper (prog)) {
				Thread thrd = p2pncs.Threading.ThreadTracer.CreateThread (delegate () {
					try {
						prog.Run ();
					} catch (ConfigFileInitializedException) {
						MessageBox.Show ("設定ファイルを保存しました。" + Environment.NewLine +
								"README.txt を参考に設定ファイルを編集してください。", "確認",
								MessageBoxButtons.OK, MessageBoxIcon.Information);
					} catch (DllNotFoundException) {
						MessageBox.Show ("必要なDLLが見つかりませんでした。再インストールしてみてください");
					}
					startupDone.Set ();
					Application.Exit ();
					return;
				}, "GUI Background Thread");
				thrd.IsBackground = true;
				thrd.Start ();
				prog.StartupWaitHandle.WaitOne ();
				if (!prog.Running) {
					startupDone.WaitOne ();
					return;
				}
				prog.Node.PortOpenChecker.UdpPortError += new EventHandler (delegate (object sender, EventArgs args) {
					tray.NotifyIcon.ShowBalloonTip (10000, "エラー", "UDPポートが開放されていない可能性があります", ToolTipIcon.Error);
				});
				try {
					Process.Start (prog.Url);
				} catch (Exception exception) {
					MessageBox.Show ("エラー:\r\n" + exception.Message);
				}
				tray.Run ();
			}
		}

		public static bool Check ()
		{
			try {
				Application.EnableVisualStyles ();
				Application.SetCompatibleTextRenderingDefault (false);
				return true;
			} catch {
				return false;
			}
		}
	}
}
