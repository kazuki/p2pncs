﻿/*
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
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace p2pncs
{
	class SystemTray
	{
		public static void Run (Program prog)
		{
			Application.EnableVisualStyles ();
			Application.SetCompatibleTextRenderingDefault (false);

			NotifyIcon notify = new NotifyIcon ();
			notify.Icon = new System.Drawing.Icon (new MemoryStream (Convert.FromBase64String (IconBase64)));
			notify.Visible = true;
			notify.ContextMenuStrip = new ContextMenuStrip ();
			notify.ContextMenuStrip.Items.Add ("ブラウザで開く(&O)", null, delegate (object sender, EventArgs args) {
				try {
					Process.Start (prog.Url);
				} catch (Exception exception) {
					MessageBox.Show ("エラー:\r\n" + exception.Message);
				}
			});
			notify.ContextMenuStrip.Items.Add ("-");
			notify.ContextMenuStrip.Items.Add ("終了(&X)", null, delegate (object sender, EventArgs args) {
				prog.Exit ();
			});

			Thread thrd = new Thread (delegate () {
				try {
					prog.Run ();
				} catch { }
				Application.Exit ();
			});
			thrd.IsBackground = true;
			thrd.Start ();
			prog.StartupWaitHandle.WaitOne ();

			try {
				Process.Start (prog.Url);
			} catch (Exception exception) {
				MessageBox.Show ("エラー:\r\n" + exception.Message);
			}

			prog.Node.PortOpenChecker.UdpPortError += new EventHandler(delegate (object sender, EventArgs args) {
				notify.ShowBalloonTip (10000, "エラー", "UDPポートが開放されていない可能性があります", ToolTipIcon.Error);
			});

			try {
				Application.Run ();
			} catch {
			} finally {
				notify.Visible = false;
				notify.Dispose ();
			}
		}

		const string IconBase64 = "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" +
			"/eEg2/3hINv94SDb/eEg2/3hINv/t7e3/f4SC/9nZ2f/Z2dn/2dnZ/9nZ2f/Z2dn/f4SC/3+Egv/u7u7/eEg2/49jSf+QYkn/kGJJ/5BiSf94SDb/7e3t/3+Egv/r6+v/6+vr/+vr6/" +
			"/r6+v/7e3t/3+Egv9/hIL/7u7u/3hINv+oh3X/mnJd/5NpU/+LXkb/eEg2/+7u7v9/hIL/f4SC/3+Egv9/hIL/f4SC/3+Egv9/hIJ9f4SC/+7u7v94SDb/poRy/6WEcv+mhHL/poRy/" +
			"3hINv/u7u7/f4SC/+3t7f/t7e3/7e3t/+3t7f/W1tb/f4SC/3+Egv/t7e3/eEg2/3hINv94SDb/eEg2/3hINv94SDb/8PDw/3+Egv94SDb/eEg2/3hINv94SDb/7e3t/3+Egv9/hIL/" +
			"x8jI/+3t7f/t7e3/7e3t/+3t7f/t7e3/7e3t/9bW1v9/hIL/kGJJ/5BiSf+QYkn/eEg2/+3t7f9/hIL/f4SCrH+Egv9/hIL/f4SC/3+Egv9/hIL/f4SC/3+Egv9/hIL/j4V8/5pyXf+" +
			"TaVP/i15G/3hINv/u7u7/f4SC/wAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAH+Egv/u7u7/eEg2/6aEcv+lhHL/poRy/6aEcv94SDb/7u7u/3+Egv8AAAAAAAAAAAAAAAAAAAAAAAAAAA" +
			"AAAAB/hIL/7e3t/3hINv94SDb/eEg2/3hINv94SDb/eEg2//Dw8P9/hIL/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAf4SC/8fIyP/t7e3/7e3t/+3t7f/t7e3/7e3t/+3t7f/W1tb/f" +
			"4SC/wAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAH+Egqx/hIL/f4SC/3+Egv9/hIL/f4SC/3+Egv9/hIL/f4SC/3+EgpcAP6xBAD+sQQA/rEEAP6xBAACsQQAArEEAAKxBAACsQQAArEEA" +
			"AKxBAACsQQAArEH8AKxB/ACsQfwArEH8AKxB";
	}
}
