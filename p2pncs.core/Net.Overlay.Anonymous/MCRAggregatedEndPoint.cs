﻿/*
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
using System.Net;

namespace p2pncs.Net.Overlay.Anonymous
{
	[SerializableTypeId (0x411)]
	public class MCRAggregatedEndPoint : EndPoint
	{
		[SerializableFieldId (0)]
		MCREndPoint[] _eps;

		public MCRAggregatedEndPoint (MCREndPoint[] eps)
		{
			_eps = eps;
		}

		public MCREndPoint[] EndPoints {
			get { return _eps; }
		}
	}
}