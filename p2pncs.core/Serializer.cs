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
		//static IFormatter _instance = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter ();
		static IFormatter _instance = new Serializer ();

		Serializer ()
		{
			Utility.SerializeHelper.RegisterCustomHandler ();
		}

		public static IFormatter Instance {
			get { return _instance; }
		}

		public object Deserialize (Stream serializationStream)
		{
			return SimpleFormatter.Instance.Deserialize (serializationStream);
		}

		public void Serialize (Stream serializationStream, object graph)
		{
			SimpleFormatter.Instance.Serialize (serializationStream, graph);
		}

		public static void AddCustomHandler (Type type, SerializeHandler serializer, DeserializeHandler deserializer)
		{
			SimpleFormatter.Instance.AddCustomHandler (type, serializer, deserializer);
		}

		public static void AddCustomHandler (Type type, int typeId, SerializeHandler serializer, DeserializeHandler deserializer)
		{
			SimpleFormatter.Instance.AddCustomHandler (type, typeId, serializer, deserializer);
		}

		public static void AddMapping (Type type, int id)
		{
			SimpleFormatter.Instance.AddMapping (type, id);
		}

		public SerializationBinder Binder {
			get { throw new NotSupportedException (); }
			set { throw new NotSupportedException (); }
		}

		public StreamingContext Context {
			get { throw new NotSupportedException (); }
			set { throw new NotSupportedException (); }
		}

		public ISurrogateSelector SurrogateSelector {
			get { throw new NotSupportedException (); }
			set { throw new NotSupportedException (); }
		}

		public delegate void SerializeHandler (Stream strm, object obj);
		public delegate object DeserializeHandler (Stream strm);

		public class SimpleFormatter
		{
			static SimpleFormatter _instance;
			static Type SerializableTypeIdType = typeof (SerializableTypeIdAttribute);
			static Type SerializableFieldIndexType = typeof (SerializableFieldIndexAttribute);

			Dictionary<Type, int> _mapping = new Dictionary<Type,int> ();
			Dictionary<Type, MemberInfo[]> _fieldCache = new Dictionary<Type, MemberInfo[]> ();
			Dictionary<MemberInfo, int> _fieldIdCache = new Dictionary<MemberInfo,int> ();
			Dictionary<Type, Dictionary<int, MemberInfo>> _fieldIdReverseCache = new Dictionary<Type,Dictionary<int,MemberInfo>> ();
			Dictionary<int, Type> _reverse = new Dictionary<int,Type> ();
			Dictionary<Type, SerializeHandler> _serializeHandlers = new Dictionary<Type, SerializeHandler> ();
			Dictionary<Type, DeserializeHandler> _deserializeHandlers = new Dictionary<Type, DeserializeHandler> ();

			const BindingFlags FieldBindingFlags = BindingFlags.Default | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

			static SimpleFormatter ()
			{
				_instance = new SimpleFormatter ();
			}

			SimpleFormatter ()
			{
				_mapping.Add (typeof (object), -1);
				_reverse.Add (-1, typeof (object));

				AddCustomHandler (typeof (byte), 0, delegate (Stream strm, object obj) {
					strm.WriteByte ((byte)obj);
				}, delegate (Stream strm) {
					return (byte)strm.ReadByte ();
				});
				AddCustomHandler (typeof (sbyte), 1, delegate (Stream strm, object obj) {
					strm.WriteByte ((byte)(sbyte)obj);
				}, delegate (Stream strm) {
					return (sbyte)(byte)strm.ReadByte ();
				});
				AddCustomHandler (typeof (short), 2, delegate (Stream strm, object obj) {
					ushort tmp = (ushort)(short)obj;
					strm.Write (new byte[]{(byte)(tmp >> 8), (byte)tmp}, 0, 2);
				}, delegate (Stream strm) {
					byte[] tmp = new byte[2];
					strm.Read (tmp, 0, tmp.Length);
					return (short)(tmp[0] << 8 | tmp[1]);
				});
				AddCustomHandler (typeof (ushort), 3, delegate (Stream strm, object obj) {
					ushort tmp = (ushort)obj;
					strm.Write (new byte[]{(byte)(tmp >> 8), (byte)tmp}, 0, 2);
				}, delegate (Stream strm) {
					byte[] tmp = new byte[2];
					strm.Read (tmp, 0, tmp.Length);
					return (ushort)((tmp[0] << 8) | tmp[1]);
				});
				AddCustomHandler (typeof (int), 4, delegate (Stream strm, object obj) {
					uint tmp = (uint)(int)obj;
					strm.Write (new byte[]{(byte)(tmp >> 24), (byte)(tmp >> 16), (byte)(tmp >> 8), (byte)tmp}, 0, 4);
				}, delegate (Stream strm) {
					byte[] tmp = new byte[4];
					strm.Read (tmp, 0, tmp.Length);
					return (tmp[0] << 24) | (tmp[1] << 16) | (tmp[2] << 8) | tmp[3];
				});
				AddCustomHandler (typeof (uint), 5, delegate (Stream strm, object obj) {
					uint tmp = (uint)obj;
					strm.Write (new byte[]{(byte)(tmp >> 24), (byte)(tmp >> 16), (byte)(tmp >> 8), (byte)tmp}, 0, 4);
				}, delegate (Stream strm) {
					byte[] tmp = new byte[4];
					strm.Read (tmp, 0, tmp.Length);
					return (uint)((tmp[0] << 24) | (tmp[1] << 16) | (tmp[2] << 8) | tmp[3]);
				});
				AddCustomHandler (typeof (long), 6, delegate (Stream strm, object obj) {
					ulong tmp = (ulong)(long)obj;
					strm.Write (new byte[]{(byte)(tmp >> 56), (byte)(tmp >> 48), (byte)(tmp >> 40), (byte)(tmp >> 32), 
						(byte)(tmp >> 24), (byte)(tmp >> 16), (byte)(tmp >> 8), (byte)tmp}, 0, 8);
				}, delegate (Stream strm) {
					byte[] tmp = new byte[8];
					strm.Read (tmp, 0, tmp.Length);
					return ((long)tmp[0] << 56) | ((long)tmp[1] << 48) | ((long)tmp[2] << 40) | ((long)tmp[3] << 32)
						| ((long)tmp[4] << 24) | ((long)tmp[5] << 16) | ((long)tmp[6] << 8) | (long)tmp[7];
				});
				AddCustomHandler (typeof (ulong), 7, delegate (Stream strm, object obj) {
					ulong tmp = (ulong)obj;
					strm.Write (new byte[]{(byte)(tmp >> 56), (byte)(tmp >> 48), (byte)(tmp >> 40), (byte)(tmp >> 32), 
						(byte)(tmp >> 24), (byte)(tmp >> 16), (byte)(tmp >> 8), (byte)tmp}, 0, 8);
				}, delegate (Stream strm) {
					byte[] tmp = new byte[8];
					strm.Read (tmp, 0, tmp.Length);
					return (ulong)(((ulong)tmp[0] << 56) | ((ulong)tmp[1] << 48) | ((ulong)tmp[2] << 40) | ((ulong)tmp[3] << 32)
						| ((ulong)tmp[4] << 24) | ((ulong)tmp[5] << 16) | ((ulong)tmp[6] << 8) | (ulong)tmp[7]);
				});
				AddCustomHandler (typeof (bool), 8, delegate (Stream strm, object obj) {
					bool tmp = (bool)obj;
					strm.WriteByte ((byte)(tmp ? 1 : 0));
				}, delegate (Stream strm) {
					return strm.ReadByte () != 0;
				});
				AddCustomHandler (typeof (float), 9, delegate (Stream strm, object obj) {
					strm.Write (BitConverter.GetBytes ((float)obj), 0, 4);
				}, delegate (Stream strm) {
					byte[] tmp = new byte[4];
					strm.Read (tmp, 0, tmp.Length);
					return BitConverter.ToSingle (tmp, 0);
				});
				AddCustomHandler (typeof (double), 10, delegate (Stream strm, object obj) {
					strm.Write (BitConverter.GetBytes ((double)obj), 0, 8);
				}, delegate (Stream strm) {
					byte[] tmp = new byte[8];
					strm.Read (tmp, 0, tmp.Length);
					return BitConverter.ToDouble (tmp, 0);
				});
				AddCustomHandler (typeof (TimeSpan), 11, delegate (Stream strm, object obj) {
					InvokeSerializer (typeof (long), strm, ((TimeSpan)obj).Ticks);
				}, delegate (Stream strm) {
					return new TimeSpan (InvokeDeserializer <long> (strm));
				});
				AddCustomHandler (typeof (DateTime), 12, delegate (Stream strm, object obj) {
					DateTime dt = (DateTime)obj;
					strm.WriteByte ((byte)dt.Kind);
					InvokeSerializer (typeof (long), strm, dt.Ticks);
				}, delegate (Stream strm) {
					DateTimeKind kind = (DateTimeKind)strm.ReadByte ();
					return new DateTime (InvokeDeserializer<long> (strm), kind);
				});
				AddCustomHandler (typeof (string), 13, delegate (Stream strm, object obj) {
					byte[] raw = System.Text.Encoding.UTF8.GetBytes ((string)obj);
					strm.Write (new byte[] {(byte)(raw.Length >> 16), (byte)(raw.Length >> 8), (byte)raw.Length}, 0, 3);
					strm.Write (raw, 0, raw.Length);
				}, delegate (Stream strm) {
					byte[] tmp = new byte[3];
					strm.Read (tmp, 0, tmp.Length);
					byte[] raw = new byte[(tmp[0] << 16) | (tmp[1] << 8) | tmp[2]];
					strm.Read (raw, 0, raw.Length);
					return System.Text.Encoding.UTF8.GetString (raw);
				});
				AddCustomHandler (typeof (IPEndPoint), 14, delegate (Stream strm, object obj) {
					IPEndPoint ep = (IPEndPoint)obj;
					byte[] rawAdrs = ep.Address.GetAddressBytes ();
					strm.WriteByte ((byte)rawAdrs.Length);
					strm.Write (rawAdrs, 0, rawAdrs.Length);
					InvokeSerializer (typeof (int), strm, ep.Port);
				}, delegate (Stream strm) {
					byte[] rawAdrs = new byte[strm.ReadByte ()];
					strm.Read (rawAdrs, 0, rawAdrs.Length);
					return new IPEndPoint (new IPAddress (rawAdrs), InvokeDeserializer<int> (strm));
				});

				Search ();
			}

			void Search ()
			{
				Assembly[] allAsm = AppDomain.CurrentDomain.GetAssemblies ();

				for (int i = 0; i < allAsm.Length; i ++) {
					Type[] types = allAsm[i].GetTypes ();
					for (int q = 0; q < types.Length; q ++) {
						object[] atts = types[q].GetCustomAttributes (SerializableTypeIdType, true);
						if (atts.Length == 1) {
							AddMapping (types[q], ((SerializableTypeIdAttribute)atts[0]).ID);
						}
					}
				}
			}

			void InvokeSerializer (Type type, Stream strm, object value)
			{
				_serializeHandlers[type] (strm, value);
			}

			T InvokeDeserializer<T> (Stream strm)
			{
				return (T)_deserializeHandlers[typeof (T)] (strm);
			}

			public void AddCustomHandler (Type type, SerializeHandler serializer, DeserializeHandler deserializer)
			{
				object[] atts = type.GetCustomAttributes (SerializableTypeIdType, true);
				if (atts == null || atts.Length != 1)
					throw new SerializationException ();
				AddCustomHandler (type, ((SerializableTypeIdAttribute)atts[0]).ID, serializer, deserializer);
			}

			public void AddCustomHandler (Type type, int typeId, SerializeHandler serializer, DeserializeHandler deserializer)
			{
				_mapping.Add (type, typeId);
				_reverse.Add (typeId, type);
				_serializeHandlers.Add (type, serializer);
				_deserializeHandlers.Add (type, deserializer);
			}

			public void AddMapping (Type type, int id)
			{
				_mapping.Add (type, id);
				_reverse.Add (id, type);

				FieldInfo[] fields = type.GetFields (FieldBindingFlags);
				List<MemberInfo> list = new List<MemberInfo> (fields.Length);
				Dictionary<int, MemberInfo> rcache = new Dictionary<int, MemberInfo> ();
				_fieldIdReverseCache[type] = rcache;
				for (int i = 0; i < fields.Length; i ++) {
					object[] catts = fields[i].GetCustomAttributes (SerializableFieldIndexType, true);
					if (catts.Length == 1) {
						list.Add (fields[i]);
						int fid = ((SerializableFieldIndexAttribute)catts[0]).ID;
						_fieldIdCache.Add (fields[i], fid);
						rcache.Add (fid, fields[i]);
					}
				}
				list.Sort (delegate (MemberInfo x, MemberInfo y) {
					return _fieldIdCache[x].CompareTo (_fieldIdCache[y]);
				});
				_fieldCache.Add (type, list.ToArray ());
			}

			public static SimpleFormatter Instance {
				get { return _instance; }
			}

			public void Serialize (Stream strm, object obj)
			{
				byte[] tmp = new byte[13];
				Type type = obj.GetType ();
				uint typeCode = (uint)_mapping [type];
				tmp[0] = (byte)(typeCode >> 24);
				tmp[1] = (byte)(typeCode >> 16);
				tmp[2] = (byte)(typeCode >> 8);
				tmp[3] = (byte)(typeCode);
				strm.Write (tmp, 0, 4);

				MemberInfo[] fields = _fieldCache[type];
				object[] values = FormatterServices.GetObjectData (obj, fields);
				for (int i = 0; i < values.Length; i++) {
					if (values[i] == null)
						continue;

					uint fieldIdx = (uint)_fieldIdCache[fields[i]];
					Serialize_Internal (strm, values[i], fieldIdx, false, tmp);
				}
				strm.WriteByte (0xff);
			}
			void Serialize_Internal (Stream strm, object value, uint fieldIdx, bool aryMode, byte[] tmp)
			{
				int p = 1;
				if (!aryMode) {
					tmp[1] = (byte)(fieldIdx >> 24);
					tmp[2] = (byte)(fieldIdx >> 16);
					tmp[3] = (byte)(fieldIdx >> 8);
					tmp[4] = (byte)(fieldIdx);
					p = 5;
				}

				SerializeHandler handler;
				Type fieldType = value.GetType ();
				if (_serializeHandlers.TryGetValue (fieldType, out handler)) {
					uint id = (uint)_mapping[fieldType];
					tmp[0] = 0;
					tmp[p++] = (byte)(id >> 24);
					tmp[p++] = (byte)(id >> 16);
					tmp[p++] = (byte)(id >> 8);
					tmp[p++] = (byte)(id);
					strm.Write (tmp, 0, p);
					handler (strm, value);
					return;
				}

				if (_mapping.ContainsKey (fieldType)) {
					tmp[0] = 1;
					strm.Write (tmp, 0, p);
					Serialize (strm, value);
					return;
				}

				if (fieldType.IsArray) {
					Array ary = (Array)value;
					int arySize = ary.Length;
					Type elementType = fieldType.GetElementType ();
					uint id = (uint)_mapping[elementType];
					tmp[0] = 2;
					tmp[p++] = (byte)(id >> 24);
					tmp[p++] = (byte)(id >> 16);
					tmp[p++] = (byte)(id >> 8);
					tmp[p++] = (byte)(id);
					tmp[p++] = (byte)(arySize >> 24);
					tmp[p++] = (byte)(arySize >> 16);
					tmp[p++] = (byte)(arySize >> 8);
					tmp[p++] = (byte)(arySize);
					strm.Write (tmp, 0, p);
					if (elementType.IsPrimitive || elementType.IsValueType) {
						SerializeHandler handler2 = _serializeHandlers[elementType];
						for (int q = 0; q < arySize; q++) {
							handler2 (strm, ary.GetValue (q));
						}
					} else {
						for (int q = 0; q < arySize; q++) {
							Serialize_Internal (strm, ary.GetValue (q), 0, true, tmp);
						}
					}
					return;
				}

				throw new NotSupportedException ();
			}

			public object Deserialize (Stream strm)
			{
				byte[] tmp = new byte[4];
				strm.Read (tmp, 0, tmp.Length);
				int typeId = (int)(uint)(((uint)tmp[0] << 24) | ((uint)tmp[1] << 16) | ((uint)tmp[2] << 8) | (uint)tmp[3]);
				Type type = _reverse[typeId];
				Dictionary<int, MemberInfo> rmap = _fieldIdReverseCache[type];
				object ret = FormatterServices.GetUninitializedObject (type);
				MemberInfo[] fields = _fieldCache[type];
				object[] values = FormatterServices.GetObjectData (ret, fields);
				while (true) {
					int flag = strm.ReadByte ();
					if (flag == 0xff) break;

					MemberInfo mi;
					object value = Deserialize_Internal (strm, rmap, flag, false, out mi, tmp);

					for (int i = 0; i < fields.Length; i ++) {
						if (mi == fields[i]) {
							values[i] = value;
							break;
						}
					}
				}
				FormatterServices.PopulateObjectMembers (ret, fields, values);
				return ret;
			}

			object Deserialize_Internal (Stream strm, Dictionary<int, MemberInfo> rmap, int flag, bool arrayMode, out MemberInfo mi, byte[] tmp)
			{
				int fieldIdx = 0;
				mi = null;
				if (!arrayMode) {
					strm.Read (tmp, 0, tmp.Length);
					fieldIdx = (int)(uint)(((uint)tmp[0] << 24) | ((uint)tmp[1] << 16) | ((uint)tmp[2] << 8) | (uint)tmp[3]);
					mi = rmap[fieldIdx];
				}

				if (flag == 0) {
					strm.Read (tmp, 0, tmp.Length);
					int fieldTypeId = (int)(uint)(((uint)tmp[0] << 24) | ((uint)tmp[1] << 16) | ((uint)tmp[2] << 8) | (uint)tmp[3]);
					Type fieldType = _reverse[fieldTypeId];
					DeserializeHandler handler;
					if (_deserializeHandlers.TryGetValue (fieldType, out handler)) {
						return handler (strm);
					} else {
						throw new SerializationException ();
					}
				} else if (flag == 1) {
					return Deserialize (strm);
				} else if (flag == 2) {
					strm.Read (tmp, 0, tmp.Length);
					int fieldTypeId = (int)(uint)(((uint)tmp[0] << 24) | ((uint)tmp[1] << 16) | ((uint)tmp[2] << 8) | (uint)tmp[3]);
					strm.Read (tmp, 0, tmp.Length);
					int arySize = (int)(uint)(((uint)tmp[0] << 24) | ((uint)tmp[1] << 16) | ((uint)tmp[2] << 8) | (uint)tmp[3]);
					Type elementType = _reverse[fieldTypeId];
					Array ary = Array.CreateInstance (elementType, arySize);
					if (elementType.IsPrimitive || elementType.IsValueType) {
						DeserializeHandler handler = _deserializeHandlers[elementType];
						for (int q = 0; q < arySize; q++)
							ary.SetValue (handler (strm), q);
					} else {
						for (int q = 0; q < arySize; q++) {
							MemberInfo mi_tmp;
							ary.SetValue (Deserialize_Internal (strm, null, strm.ReadByte (), true, out mi_tmp, tmp), q);
						}
					}
					return ary;
				} else {
					throw new SerializationException ();
				}
			}
		}
	}
}
