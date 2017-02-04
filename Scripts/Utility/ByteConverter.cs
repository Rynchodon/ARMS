using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using VRageMath;

namespace Rynchodon
{
	public static class ByteConverter
	{

		private static class TypeConverter<T>
		{
			public delegate void AppendBytesDelegate(List<byte> bytes, T data);
			public delegate T ReadBytesDelegate(byte[] bytes, ref int position);

			public static AppendBytesDelegate AppendBytes;
			public static ReadBytesDelegate ReadBytes;
		}

		static ByteConverter()
		{
			TypeConverter<bool>.AppendBytes = AppendBytes;
			TypeConverter<bool>.ReadBytes = GetBool;

			TypeConverter<byte>.AppendBytes = AppendBytes;
			TypeConverter<byte>.ReadBytes = GetByte;

			TypeConverter<short>.AppendBytes = AppendBytes;
			TypeConverter<short>.ReadBytes = GetShort;

			TypeConverter<ushort>.AppendBytes = AppendBytes;
			TypeConverter<ushort>.ReadBytes = GetUshort;

			TypeConverter<int>.AppendBytes = AppendBytes;
			TypeConverter<int>.ReadBytes = GetInt;

			TypeConverter<uint>.AppendBytes = AppendBytes;
			TypeConverter<uint>.ReadBytes = GetUint;

			TypeConverter<long>.AppendBytes = AppendBytes;
			TypeConverter<long>.ReadBytes = GetLong;

			TypeConverter<ulong>.AppendBytes = AppendBytes;
			TypeConverter<ulong>.ReadBytes = GetUlong;

			TypeConverter<float>.AppendBytes = AppendBytes;
			TypeConverter<float>.ReadBytes = GetFloat;

			TypeConverter<double>.AppendBytes = AppendBytes;
			TypeConverter<double>.ReadBytes = GetDouble;

			TypeConverter<string>.AppendBytes = AppendBytes;
			TypeConverter<string>.ReadBytes = GetString;

			TypeConverter<DateTime>.AppendBytes = AppendBytes;
			TypeConverter<DateTime>.ReadBytes = GetDateTime;

			TypeConverter<StringBuilder>.AppendBytes = AppendBytes;
			TypeConverter<StringBuilder>.ReadBytes = GetStringBuilder;

			TypeConverter<Vector3>.AppendBytes = AppendBytes;
			TypeConverter<Vector3>.ReadBytes = GetVector3;

			TypeConverter<Vector3D>.AppendBytes = AppendBytes;
			TypeConverter<Vector3D>.ReadBytes = GetVector3D;
		}

		#region Union

		[StructLayout(LayoutKind.Explicit)]
		private struct byteUnion16
		{
			[FieldOffset(0)]
			public short s;
			[FieldOffset(0)]
			public ushort us;
			[FieldOffset(0)]
			public char c;

			[FieldOffset(0)]
			public byte b0;
			[FieldOffset(1)]
			public byte b1;
		}

		[StructLayout(LayoutKind.Explicit)]
		private struct byteUnion32
		{
			[FieldOffset(0)]
			public int i;
			[FieldOffset(0)]
			public uint ui;
			[FieldOffset(0)]
			public float f;

			[FieldOffset(0)]
			public byte b0;
			[FieldOffset(1)]
			public byte b1;
			[FieldOffset(2)]
			public byte b2;
			[FieldOffset(3)]
			public byte b3;
		}

		[StructLayout(LayoutKind.Explicit)]
		private struct byteUnion64
		{
			[FieldOffset(0)]
			public long l;
			[FieldOffset(0)]
			public ulong ul;
			[FieldOffset(0)]
			public double d;

			[FieldOffset(0)]
			public byte b0;
			[FieldOffset(1)]
			public byte b1;
			[FieldOffset(2)]
			public byte b2;
			[FieldOffset(3)]
			public byte b3;
			[FieldOffset(4)]
			public byte b4;
			[FieldOffset(5)]
			public byte b5;
			[FieldOffset(6)]
			public byte b6;
			[FieldOffset(7)]
			public byte b7;
		}

		#endregion Union

		#region Append Bytes
		
		#region List

		private static void AppendBytes(List<byte> bytes, byteUnion16 u)
		{
			if (BitConverter.IsLittleEndian)
			{
				bytes.Add(u.b0);
				bytes.Add(u.b1);
			}
			else
			{
				bytes.Add(u.b1);
				bytes.Add(u.b0);
			}
		}

		private static void AppendBytes(List<byte> bytes, byteUnion32 u)
		{
			if (BitConverter.IsLittleEndian)
			{
				bytes.Add(u.b0);
				bytes.Add(u.b1);
				bytes.Add(u.b2);
				bytes.Add(u.b3);
			}
			else
			{
				bytes.Add(u.b3);
				bytes.Add(u.b2);
				bytes.Add(u.b1);
				bytes.Add(u.b0);
			}
		}

		private static void AppendBytes(List<byte> bytes, byteUnion64 u)
		{
			if (BitConverter.IsLittleEndian)
			{
				bytes.Add(u.b0);
				bytes.Add(u.b1);
				bytes.Add(u.b2);
				bytes.Add(u.b3);
				bytes.Add(u.b4);
				bytes.Add(u.b5);
				bytes.Add(u.b6);
				bytes.Add(u.b7);
			}
			else
			{
				bytes.Add(u.b7);
				bytes.Add(u.b6);
				bytes.Add(u.b5);
				bytes.Add(u.b4);
				bytes.Add(u.b3);
				bytes.Add(u.b2);
				bytes.Add(u.b0);
			}
		}

		public static void AppendBytes(List<byte> bytes, bool b)
		{
			if (b)
				bytes.Add(1);
			else
				bytes.Add(0);
		}

		public static void AppendBytes(List<byte> bytes, byte b)
		{
			bytes.Add(b);
		}

		public static void AppendBytes(List<byte> bytes, short s)
		{
			AppendBytes(bytes, new byteUnion16() { s = s });
		}

		public static void AppendBytes(List<byte> bytes, ushort us)
		{
			AppendBytes(bytes, new byteUnion16() { us = us });
		}

		public static void AppendBytes(List<byte> bytes, char c)
		{
			AppendBytes(bytes, new byteUnion16() { c = c });
		}

		public static void AppendBytes(List<byte> bytes, int i)
		{
			AppendBytes(bytes, new byteUnion32() { i = i });
		}

		public static void AppendBytes(List<byte> bytes, uint ui)
		{
			AppendBytes(bytes, new byteUnion32() { ui = ui });
		}

		public static void AppendBytes(List<byte> bytes, float f)
		{
			AppendBytes(bytes, new byteUnion32() { f = f });
		}

		public static void AppendBytes(List<byte> bytes, long l)
		{
			AppendBytes(bytes, new byteUnion64() { l = l });
		}

		public static void AppendBytes(List<byte> bytes, ulong ul)
		{
			AppendBytes(bytes, new byteUnion64() { ul = ul });
		}

		public static void AppendBytes(List<byte> bytes, double d)
		{
			AppendBytes(bytes, new byteUnion64() { d = d });
		}

		public static void AppendBytes(List<byte> bytes, string s)
		{
			AppendBytes(bytes, s.Length);
			foreach (char c in s)
				AppendBytes(bytes, c);
		}

		public static void AppendBytes(List<byte> bytes, DateTime dt)
		{
			AppendBytes(bytes, new byteUnion64() { l = dt.Ticks });
		}

		public static void AppendBytes(List<byte> bytes, StringBuilder s)
		{
			AppendBytes(bytes, s.Length);
			for (int index = 0; index < s.Length; index++)
				AppendBytes(bytes, s[index]);
		}

		public static void AppendBytes(List<byte> bytes, Vector3 v)
		{
			AppendBytes(bytes, v.X);
			AppendBytes(bytes, v.Y);
			AppendBytes(bytes, v.Z);
		}

		public static void AppendBytes(List<byte> bytes, Vector3D v)
		{
			AppendBytes(bytes, v.X);
			AppendBytes(bytes, v.Y);
			AppendBytes(bytes, v.Z);
		}

		public static void AppendBytes<T>(List<byte> bytes, T data)
		{
			if (TypeConverter<T>.AppendBytes != null)
			{
				TypeConverter<T>.AppendBytes(bytes, data);
				return;
			}

			AppendBytes(bytes, (object)data);
		}

		public static void AppendBytes(List<byte> bytes, object data)
		{
			IConvertible convertible = data as IConvertible;
			if (convertible != null)
				switch (convertible.GetTypeCode())
				{
					case TypeCode.Boolean:
						AppendBytes(bytes, (bool)data);
						return;
					case TypeCode.Byte:
						AppendBytes(bytes, (byte)data);
						return;
					case TypeCode.Int16:
						AppendBytes(bytes, (short)data);
						return;
					case TypeCode.UInt16:
						AppendBytes(bytes, (ushort)data);
						return;
					case TypeCode.Int32:
						AppendBytes(bytes, (int)data);
						return;
					case TypeCode.UInt32:
						AppendBytes(bytes, (uint)data);
						return;
					case TypeCode.Int64:
						AppendBytes(bytes, (long)data);
						return;
					case TypeCode.UInt64:
						AppendBytes(bytes, (ulong)data);
						return;
					case TypeCode.DateTime:
						AppendBytes(bytes, ((DateTime)data).Ticks);
						return;
					case TypeCode.Single:
						AppendBytes(bytes, (float)data);
						return;
					case TypeCode.Double:
						AppendBytes(bytes, (double)data);
						return;
					case TypeCode.String:
						AppendBytes(bytes, (string)data);
						return;
					default:
						throw new Exception("No conversion for: " + convertible.GetTypeCode() + ", " + data);
				}

			Type typeOfData = data.GetType();
			if (typeOfData == typeof(StringBuilder))
			{
				AppendBytes(bytes, (StringBuilder)data);
				return;
			}
			if (typeOfData == typeof(Vector3))
			{
				AppendBytes(bytes, (Vector3)data);
				return;
			}
			if (typeOfData == typeof(Vector3D))
			{
				AppendBytes(bytes, (Vector3D)data);
				return;
			}
			if (typeof(IEnumerable).IsAssignableFrom(typeOfData))
			{
				IEnumerable enumerable = (IEnumerable)data;
				int count;
				if (typeof(ICollection).IsAssignableFrom(typeOfData))
					count = ((ICollection)data).Count;
				else
				{
					count = 0;
					foreach (object item in enumerable)
						++count;
				}
				AppendBytes(bytes, count);
				foreach (object item in enumerable)
				{
					Logger.DebugLog("Array item: " + item);
					AppendBytes(bytes, item);
				}
				return;
			}
			throw new InvalidCastException("data is of invalid type: " + convertible?.GetTypeCode() + ", " + typeOfData + ", " + data);
		}

		#endregion List

		#endregion Append Bytes

		#region From Byte Array

		private static byteUnion16 GetByteUnion16(byte[] bytes, ref int pos)
		{
			if (BitConverter.IsLittleEndian)
			{
				return new byteUnion16()
				{
					b0 = bytes[pos++],
					b1 = bytes[pos++]
				};
			}
			else
			{
				return new byteUnion16()
				{
					b1 = bytes[pos++],
					b0 = bytes[pos++]
				};
			}
		}

		private static byteUnion32 GetByteUnion32(byte[] bytes, ref int pos)
		{
			if (BitConverter.IsLittleEndian)
			{
				return new byteUnion32()
				{
					b0 = bytes[pos++],
					b1 = bytes[pos++],
					b2 = bytes[pos++],
					b3 = bytes[pos++]
				};
			}
			else
			{
				return new byteUnion32()
				{
					b3 = bytes[pos++],
					b2 = bytes[pos++],
					b1 = bytes[pos++],
					b0 = bytes[pos++]
				};
			}
		}

		private static byteUnion64 GetByteUnion64(byte[] bytes, ref int pos)
		{
			if (BitConverter.IsLittleEndian)
			{
				return new byteUnion64()
				{
					b0 = bytes[pos++],
					b1 = bytes[pos++],
					b2 = bytes[pos++],
					b3 = bytes[pos++],
					b4 = bytes[pos++],
					b5 = bytes[pos++],
					b6 = bytes[pos++],
					b7 = bytes[pos++]
				};
			}
			else
			{
				return new byteUnion64()
				{
					b7 = bytes[pos++],
					b6 = bytes[pos++],
					b5 = bytes[pos++],
					b4 = bytes[pos++],
					b3 = bytes[pos++],
					b2 = bytes[pos++],
					b1 = bytes[pos++],
					b0 = bytes[pos++]
				};
			}
		}

		public static bool GetBool(byte[] bytes, ref int pos)
		{
			return bytes[pos++] != 0;
		}

		public static byte GetByte(byte[] bytes, ref int pos)
		{
			return bytes[pos++];
		}

		public static short GetShort(byte[] bytes, ref int pos)
		{
			return GetByteUnion16(bytes, ref pos).s;
		}

		public static ushort GetUshort(byte[] bytes, ref int pos)
		{
			return GetByteUnion16(bytes, ref pos).us;
		}

		public static char GetChar(byte[] bytes, ref int pos)
		{
			return GetByteUnion16(bytes, ref pos).c;
		}

		public static int GetInt(byte[] bytes, ref int pos)
		{
			return GetByteUnion32(bytes, ref pos).i;
		}

		public static uint GetUint(byte[] bytes, ref int pos)
		{
			return GetByteUnion32(bytes, ref pos).ui;
		}

		public static float GetFloat(byte[] bytes, ref int pos)
		{
			return GetByteUnion32(bytes, ref pos).f;
		}

		public static long GetLong(byte[] bytes, ref int pos)
		{
			return GetByteUnion64(bytes, ref pos).l;
		}

		public static ulong GetUlong(byte[] bytes, ref int pos)
		{
			return GetByteUnion64(bytes, ref pos).ul;
		}

		public static double GetDouble(byte[] bytes, ref int pos)
		{
			return GetByteUnion64(bytes, ref pos).d;
		}

		public static string GetString(byte[] bytes, ref int pos)
		{
			char[] result = new char[GetInt(bytes, ref pos)];
			for (int index = 0; index < result.Length; index++)
				result[index] = GetChar(bytes, ref pos);
			return new string(result);
		}

		public static DateTime GetDateTime(byte[] bytes, ref int pos)
		{
			return new DateTime(GetByteUnion64(bytes, ref pos).l);
		}

		public static StringBuilder GetStringBuilder(byte[] bytes, ref int pos)
		{
			return new StringBuilder(GetString(bytes, ref pos));
		}

		public static Vector3 GetVector3(byte[] bytes, ref int pos)
		{
			return new Vector3(GetFloat(bytes, ref pos), GetFloat(bytes, ref pos), GetFloat(bytes, ref pos));
		}

		public static Vector3D GetVector3D(byte[] bytes, ref int pos)
		{
			return new Vector3D(GetDouble(bytes, ref pos), GetDouble(bytes, ref pos), GetDouble(bytes, ref pos));
		}

		public static T GetOfType<T>(byte[] bytes, ref int position)
		{
			if (TypeConverter<T>.ReadBytes != null)
				return TypeConverter<T>.ReadBytes(bytes, ref position);

			return (T)GetOfType(bytes, ref position, typeof(T));
		}

		public static object GetOfType(byte[] bytes, ref int pos, Type typeOfObject)
		{
			if (typeof(Enum).IsAssignableFrom(typeOfObject))
				return GetOfType(bytes, ref pos, Enum.GetUnderlyingType(typeOfObject));

			if (typeOfObject == typeof(bool))
				return GetBool(bytes, ref pos);

			if (typeOfObject == typeof(byte))
				return GetByte(bytes, ref pos);

			if (typeOfObject == typeof(short))
				return GetShort(bytes, ref pos);

			if (typeOfObject == typeof(ushort))
				return GetUshort(bytes, ref pos);

			if (typeOfObject == typeof(int))
				return GetInt(bytes, ref pos);

			if (typeOfObject == typeof(uint))
				return GetUint(bytes, ref pos);

			if (typeOfObject == typeof(long))
				return GetLong(bytes, ref pos);

			if (typeOfObject == typeof(ulong))
				return GetUlong(bytes, ref pos);

			if (typeOfObject == typeof(DateTime))
				return new DateTime(GetLong(bytes, ref pos));

			if (typeOfObject == typeof(float))
				return GetFloat(bytes, ref pos);

			if (typeOfObject == typeof(double))
				return GetDouble(bytes, ref pos);

			if (typeOfObject == typeof(char))
				return GetChar(bytes, ref pos);

			if (typeOfObject == typeof(string))
				return GetString(bytes, ref pos);

			if (typeOfObject == typeof(StringBuilder))
				return GetStringBuilder(bytes, ref pos);

			if (typeOfObject == typeof(Vector3))
				return GetVector3(bytes, ref pos);

			if (typeOfObject == typeof(Vector3D))
				return GetVector3D(bytes, ref pos);

			if (typeof(Array).IsAssignableFrom(typeOfObject))
				return CreateArray(bytes, ref pos, typeOfObject);

			throw new InvalidCastException("typeOfObject is invalid: " + typeOfObject);
		}

		private static Array CreateArray(byte[] bytes, ref int pos, Type typeOfObject)
		{
			Type elementType = typeOfObject.GetElementType();
			Array array = Array.CreateInstance(elementType, GetInt(bytes, ref pos));
			for (int index = 0; index < array.Length; ++index)
			{
				array.SetValue(GetOfType(bytes, ref pos, elementType), index);
				Logger.DebugLog("Array item: " + array.GetValue(index));
			}
			return array;
		}

		#endregion From Byte Array

	}
}
