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

namespace p2pncs.Evaluation
{
	class Program
	{
		static void Main (string[] args)
		{
			EvalOptionSet options = new EvalOptionSet ();
			if (!options.Parse (args))
				return;
			if (options.ShowEvalutionTypes) {
				Console.WriteLine ("Evalution List:");
				options.ShowEvalutionList (Console.Out, "  ");
				return;
			}

			p2pncs.Simulation.OSTimerPrecision.SetCurrentThreadToHighPrecision ();
			try {
				Console.WriteLine ("p2pncs Evalution Program");
				options.WriteOptions (Console.Out, "  ");

				Dictionary<EvaluationTypes, IEvaluator> evalutions = new Dictionary<EvaluationTypes,IEvaluator> () {
					{EvaluationTypes.AR, new AnonymousRouterEvaluation ()},
					{EvaluationTypes.AR_SimCom, new AnonymousRouterSimultaneouslyCommunicationEvaluator ()},
					{EvaluationTypes.AR_Throughput, new AnonymousHighThroughputEvaluator ()},
					{EvaluationTypes.KBR1, new KBREval1 ()},
					{EvaluationTypes.DHT1, new DHTEval1 ()},
					{EvaluationTypes.MASSKEY1, new MassKeyEval1 ()},
				};
				evalutions[options.EvalutionType].Evaluate (options);
			} finally {
				p2pncs.Simulation.OSTimerPrecision.RevertCurrentThreadPrecision ();
			}
		}
	}
}
