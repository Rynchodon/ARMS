using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using VRageMath;

namespace Rynchodon
{
	public static class ByteConverter
	{

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

		#region Array

		private static void AppendBytes(byte[] bytes, byteUnion16 u, ref int pos)
		{
			bytes[pos++] = u.b0;
			bytes[pos++] = u.b1;
		}

		private static void AppendBytes(byte[] bytes, byteUnion32 u, ref int pos)
		{
			bytes[pos++] = u.b0;
			bytes[pos++] = u.b1;
			bytes[pos++] = u.b2;
			bytes[pos++] = u.b3;
		}

		private static void AppendBytes(byte[] bytes, byteUnion64 u, ref int pos)
		{
			bytes[pos++] = u.b0;
			bytes[pos++] = u.b1;
			bytes[pos++] = u.b2;
			bytes[pos++] = u.b3;
			bytes[pos++] = u.b4;
			bytes[pos++] = u.b5;
			bytes[pos++] = u.b6;
			bytes[pos++] = u.b7;
		}

		public static void AppendBytes(byte[] bytes, bool b, ref int pos)
		{
			if (b)
				bytes[pos++] = 1;
			else
				bytes[pos++] = 0;
		}

		public static void AppendBytes(byte[] bytes, byte b, ref int pos)
		{
			bytes[pos++] = b;
		}

		public static void AppendBytes(byte[] bytes, short s, ref int pos)
		{
			AppendBytes(bytes, new byteUnion16() { s = s }, ref pos);
		}

		public static void AppendBytes(byte[] bytes, ushort us, ref int pos)
		{
			AppendBytes(bytes, new byteUnion16() { us = us }, ref pos);
		}

		public static void AppendBytes(byte[] bytes, int i, ref int pos)
		{
			AppendBytes(bytes, new byteUnion32() { i = i }, ref pos);
		}

		public static void AppendBytes(byte[] bytes, uint ui, ref int pos)
		{
			AppendBytes(bytes, new byteUnion32() { ui = ui }, ref pos);
		}

		public static void AppendBytes(byte[] bytes, float f, ref int pos)
		{
			AppendBytes(bytes, new byteUnion32() { f = f }, ref pos);
		}

		public static void AppendBytes(byte[] bytes, long l, ref int pos)
		{
			AppendBytes(bytes, new byteUnion64() { l = l }, ref pos);
		}

		public static void AppendBytes(byte[] bytes, ulong ul, ref int pos)
		{
			AppendBytes(bytes, new byteUnion64() { ul = ul }, ref pos);
		}

		public static void AppendBytes(byte[] bytes, double d, ref int pos)
		{
			AppendBytes(bytes, new byteUnion64() { d = d }, ref pos);
		}

		#endregion Array

		#region List

		private static void AppendBytes(List<byte> bytes, byteUnion16 u)
		{
			bytes.Add(u.b0);
			bytes.Add(u.b1);
		}

		private static void AppendBytes(List<byte> bytes, byteUnion32 u)
		{
			bytes.Add(u.b0);
			bytes.Add(u.b1);
			bytes.Add(u.b2);
			bytes.Add(u.b3);
		}

		private static void AppendBytes(List<byte> bytes, byteUnion64 u)
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
			TypeCode code = Convert.GetTypeCode(data);
			switch (code)
			{
				case TypeCode.Boolean:
					AppendBytes(bytes, (bool)(object)data);
					return;
				case TypeCode.Byte:
					AppendBytes(bytes, (byte)(object)data);
					return;
				case TypeCode.Int16:
					AppendBytes(bytes, (short)(object)data);
					return;
				case TypeCode.UInt16:
					AppendBytes(bytes, (ushort)(object)data);
					return;
				case TypeCode.Int32:
					AppendBytes(bytes, (int)(object)data);
					return;
				case TypeCode.UInt32:
					AppendBytes(bytes, (uint)(object)data);
					return;
				case TypeCode.Int64:
					AppendBytes(bytes, (long)(object)data);
					return;
				case TypeCode.UInt64:
					AppendBytes(bytes, (ulong)(object)data);
					return;
				case TypeCode.Single:
					AppendBytes(bytes, (float)(object)data);
					return;
				case TypeCode.Double:
					AppendBytes(bytes, (double)(object)data);
					return;
				case TypeCode.String:
					AppendBytes(bytes, (string)(object)data);
					return;
			}
			Type typeofT = typeof(T);
			if (typeofT == typeof(StringBuilder))
			{
				AppendBytes(bytes, (StringBuilder)(object)data);
				return;
			}
			if (typeofT == typeof(Vector3))
			{
				AppendBytes(bytes, (Vector3)(object)data);
				return;
			}
			if (typeofT == typeof(Vector3D))
			{
				AppendBytes(bytes, (Vector3D)(object)data);
				return;
			}
			throw new InvalidCastException("data is of invalid type: " + code + ", " + data);
		}

		public static void AppendBytes<T>(List<byte> bytes, IEnumerable<T> data)
		{
			if (data == null)
			{
				AppendBytes(bytes, 0);
				return;
			}

			AppendBytes(bytes, data.Count());
			foreach (T item in data)
				AppendBytes(bytes, item);
		}

		#endregion List

		#endregion Append Bytes

		#region From Byte Array

		private static byteUnion16 GetByteUnion16(byte[] bytes, ref int pos)
		{
			return new byteUnion16()
			{
				b0 = bytes[pos++],
				b1 = bytes[pos++]
			};
		}

		private static byteUnion32 GetByteUnion32(byte[] bytes, ref int pos)
		{
			return new byteUnion32()
			{
				b0 = bytes[pos++],
				b1 = bytes[pos++],
				b2 = bytes[pos++],
				b3 = bytes[pos++]
			};
		}

		private static byteUnion64 GetByteUnion64(byte[] bytes, ref int pos)
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

		public static StringBuilder GetStringBuilder(byte[] bytes, ref int pos)
		{
			StringBuilder result = new StringBuilder(GetInt(bytes, ref pos));
			for (int index = 0; index < result.Length; index++)
				result[index] = GetChar(bytes, ref pos);
			return result;
		}

		public static Vector3 GetVector3(byte[] bytes, ref int pos)
		{
			return new Vector3(GetFloat(bytes, ref pos), GetFloat(bytes, ref pos), GetFloat(bytes, ref pos));
		}

		public static Vector3D GetVector3D(byte[] bytes, ref int pos)
		{
			return new Vector3D(GetDouble(bytes, ref pos), GetDouble(bytes, ref pos), GetDouble(bytes, ref pos));
		}

		public static void GetOfType<T>(byte[] bytes, ref int pos, ref T value)
		{
			switch (Convert.GetTypeCode(value))
			{
				case TypeCode.Boolean:
					value = (T)(object)GetBool(bytes, ref pos);
					return;
				case TypeCode.Byte:
					value = (T)(object)GetByte(bytes, ref pos);
					return;
				case TypeCode.Int16:
					value = (T)(object)GetShort(bytes, ref pos);
					return;
				case TypeCode.UInt16:
					value = (T)(object)GetUshort(bytes, ref pos);
					return;
				case TypeCode.Int32:
					value = (T)(object)GetInt(bytes, ref pos);
					return;
				case TypeCode.UInt32:
					value = (T)(object)GetUint(bytes, ref pos);
					return;
				case TypeCode.Int64:
					value = (T)(object)GetLong(bytes, ref pos);
					return;
				case TypeCode.UInt64:
					value = (T)(object)GetUlong(bytes, ref pos);
					return;
				case TypeCode.Single:
					value = (T)(object)GetFloat(bytes, ref pos);
					return;
				case TypeCode.Double:
					value = (T)(object)GetFloat(bytes, ref pos);
					return;
				case TypeCode.Char:
					value = (T)(object)GetChar(bytes, ref pos);
					return;
				case TypeCode.String:
					value = (T)(object)GetString(bytes, ref pos);
					return;
			}
			Type typeofT = typeof(T);
			if (typeofT == typeof(StringBuilder))
			{
				value = (T)(object)GetStringBuilder(bytes, ref pos);
				return;
			}
			if (typeofT == typeof(Vector3))
			{
				value = (T)(object)GetVector3(bytes, ref pos);
				return;
			}
			if (typeofT == typeof(Vector3D))
			{
				value = (T)(object)GetVector3D(bytes, ref pos);
				return;
			}
			throw new ArgumentException("Invalid TypeCode: " + Convert.GetTypeCode(value));
		}

		public static T[] GetArrayOfType<T>(byte[] bytes, ref int pos)
		{
			T[] array = new T[GetInt(bytes, ref pos)];
			for (int i = 0; i < array.Length; ++i)
				GetOfType(bytes, ref pos, ref array[i]);
			return array;
		}

		#endregion From Byte Array

	}
}
