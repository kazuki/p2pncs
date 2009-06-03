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
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NDesk.Options;
using p2pncs.Simulation.VirtualNet;

namespace p2pncs.Evaluation
{
	class EvalOptionSet
	{
		OptionSet _set;

		public EvalOptionSet ()
		{
			// デフォルト値
			PacketLossRate = 0.05;
			Latency = 40;
			NumberOfNodes = 1000;
			ChurnInterval = 500;
			AnonymousRouteRelays = 3;
			AnonymousRouteRoutes = 2;
			AnonymousRouteBackupRoutes = 1;
			Tests = 100;
			UseNewKeyBasedRouter = true;
			NewKBRStrictMode = true;
			UseNewAnonymousRouter = true;
			BypassMessagingSerializer = true;
			EvalutionType = EvaluationTypes.AnonymousRouter;
			ShowEvalutionTypes = false;

			_set = new OptionSet () {
					{"nodes=", "ノード数", (int v) => NumberOfNodes = v},
					{"churn=", "ノードの離脱/参加を行う間隔 (ミリ秒)", (int v) => ChurnInterval = v},
					{"latency=", "UDPの配送遅延 (ミリ秒)", (int v) => Latency = v},
					{"loss=", "UDPの損失率を指定 (0.0～1.0)", (double v) => {if (v >= 0.0 && v < 1.0) PacketLossRate = v; else throw new ArgumentOutOfRangeException ();}},
					{"new-kbr", "新しいKeyBasedRouter実装を利用する", v => UseNewKeyBasedRouter = v != null},
					{"strict", "新しいKeyBasedRouter実装においてStrictモードを利用する", v => NewKBRStrictMode = v != null},
					{"new-ar", "新しいAnonymousRouter実装を利用する", v => UseNewAnonymousRouter = v != null},
					{"ar_relays=", "匿名多重暗号経路の中継ノード数", (int v) => AnonymousRouteRelays = v},
					{"ar_routes=", "匿名多重暗号経路の同時送信経路数", (int v) => AnonymousRouteRoutes = v},
					{"ar_backups=", "匿名多重暗号経路のバックアップ経路数", (int v) => AnonymousRouteBackupRoutes = v},
					{"bypass-serializer", "メッセージングソケットにおいてシリアライザをバイパスする", v => BypassMessagingSerializer = v != null},
					{"eval=", "実行する評価プログラム", (EvaluationTypes v) => EvalutionType = v},
					{"eval-list", "利用可能な評価プログラムの一覧を表示する", v => ShowEvalutionTypes = v != null},
					{"tests=", "選択した評価プログラム内で実行するテスト数", (int v) => Tests = v},
					{"h|help|?", "ヘルプを表示する", v => ShowHelp = v != null},
				};
		}

		public bool Parse (string[] args)
		{
			try {
				List<string> extra = _set.Parse (args);
				if (extra.Count > 0) {
					Console.Write ("Unknown option{0}: {1}", extra.Count == 1 ? string.Empty : "s", string.Join (" ", extra.ToArray ()));
					return false;
				}
				if (ShowHelp)
					throw new ArgumentException ();
				return true;
			} catch {
				_set.WriteOptionDescriptions (Console.Out);
				return false;
			}
		}

		public void WriteOptions (TextWriter writer, string indent)
		{
			PropertyInfo[] properties = typeof (EvalOptionSet).GetProperties (BindingFlags.Instance | BindingFlags.Public);
			for (int i = 0; i < properties.Length; i++) {
				writer.WriteLine ("{0}{1}={2}", indent, properties[i].Name, properties[i].GetValue (this, null));
			}
		}

		public void ShowEvalutionList (TextWriter writer, string indent)
		{
			string[] names = Enum.GetNames (typeof (EvaluationTypes));
			foreach (string name in names) {
				writer.WriteLine ("{0}{1}", indent, name);
			}
		}

		public double PacketLossRate { get; set; }
		public int Latency { get; set; }
		public int NumberOfNodes { get; set; }
		public int ChurnInterval { get; set; }
		public bool UseNewKeyBasedRouter { get; set; }
		public bool NewKBRStrictMode { get; set; }
		public bool UseNewAnonymousRouter { get; set; }
		public bool BypassMessagingSerializer { get; set; }
		public EvaluationTypes EvalutionType { get; set; }
		public int AnonymousRouteRelays { get; set; }
		public int AnonymousRouteRoutes { get; set; }
		public int AnonymousRouteBackupRoutes { get; set; }
		public int Tests { get; set; }

		internal bool ShowEvalutionTypes { get; set; }
		private bool ShowHelp { get; set; }

		public IPacketLossRate GetPacketLossRate ()
		{
			if (PacketLossRate > 0.0)
				return PacketLossType.Constant (PacketLossRate);
			return PacketLossType.Lossless ();
		}

		public ILatency GetLatency ()
		{
			return LatencyTypes.Constant (Latency);
		}
	}
}
