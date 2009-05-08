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
using System.Net;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;

namespace p2pncs
{
	public class Serializer : IFormatter
	{
		CompactBinarySerializer.Serializer _internal = new CompactBinarySerializer.Serializer ();
		static Serializer _instance = new Serializer ();

		Serializer ()
		{
			_internal.Search ();
			Utility.SerializeHelper.RegisterCustomHandler (this);
		}

		public static Serializer Instance {
			get { return _instance; }
		}

		public object Deserialize (Stream serializationStream)
		{
			return _internal.Deserialize (serializationStream);
		}

		public void Serialize (Stream serializationStream, object graph)
		{
			_internal.Serialize (serializationStream, graph);
		}

		public void AddCustomHandler (Type type, int typeId, CompactBinarySerializer.SerializeHandler serializer, CompactBinarySerializer.DeserializeHandler deserializer)
		{
			_internal.AddMapping (type, typeId, serializer, deserializer);
		}

		public SerializationBinder Binder {
			get { return _internal.Binder; }
			set { _internal.Binder = value; }
		}

		public StreamingContext Context {
			get { return _internal.Context; }
			set { _internal.Context = value; }
		}

		public ISurrogateSelector SurrogateSelector {
			get { return _internal.SurrogateSelector; }
			set { _internal.SurrogateSelector = value; }
		}
	}
}
