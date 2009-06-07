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

namespace p2pncs.Simulation
{
	public class StandardDeviation
	{
		List<float> _values = null;
		int _count = 0;
		double _total = 0.0, _total2 = 0.0;
		float _min = float.MaxValue, _max = float.MinValue;

		public StandardDeviation (bool highPrecisionMode)
		{
			if (highPrecisionMode)
				_values = new List<float> ();
		}

		public void AddSample (float value)
		{
			if (_values != null)
				_values.Add (value);
			_count ++;
			_min = Math.Min (_min, value);
			_max = Math.Max (_max, value);
			_total += value;
			_total2 += value * value;
		}

		public double Average {
			get { return _count == 0 ? 0.0 : _total / _count; }
		}

		public float Minimum {
			get { return _min; }
		}

		public float Maximum {
			get { return _max; }
		}

		public int NumberOfSamples {
			get { return _count; }
		}

		public double ComputeStandardDeviation ()
		{
			if (_count == 0) {
				return 0.0;
			}

			double avg = _total / _count;
			if (_values == null)
				return Math.Sqrt ((_total2 / _count) - (avg * avg));

			double tmp = 0.0;
			for (int i = 0; i < _count; i++)
				tmp += (avg - _values[i]) * (avg - _values[i]);
			return Math.Sqrt (tmp / _count);
		}

		public IList<float> Samples {
			get { return _values.AsReadOnly (); }
		}

		public void Clear ()
		{
			if (_values != null)
				_values.Clear ();
			_count = 0;
			_total = 0.0;
			_total2 = 0.0;
		}
	}
}
