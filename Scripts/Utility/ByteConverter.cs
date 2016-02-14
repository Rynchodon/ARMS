using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Rynchodon
{
	public static class ByteConverter
	{

		private static readonly byte unknownChar = Convert.ToByte('?');

		#region Union

		[StructLayout(LayoutKind.Explicit)]
		private struct byteUnion16
		{
			[FieldOffset(0)]
			public short s;
			[FieldOffset(0)]
			public ushort us;

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

		private static void AppendBytes(byteUnion16 u, byte[] bytes, ref int pos)
		{
			bytes[pos++] = u.b0;
			bytes[pos++] = u.b1;
		}

		private static void AppendBytes(byteUnion32 u, byte[] bytes, ref int pos)
		{
			bytes[pos++] = u.b0;
			bytes[pos++] = u.b1;
			bytes[pos++] = u.b2;
			bytes[pos++] = u.b3;
		}

		private static void AppendBytes(byteUnion64 u, byte[] bytes, ref int pos)
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

		public static void AppendBytes(bool b, byte[] bytes, ref int pos)
		{
			if (b)
				bytes[pos++] = 1;
			else
				bytes[pos++] = 0;
		}

		public static void AppendBytes(byte b, byte[] bytes, ref int pos)
		{
			bytes[pos++] = b;
		}

		public static void AppendBytes(short s, byte[] bytes, ref int pos)
		{
			AppendBytes(new byteUnion16() { s = s }, bytes, ref pos);
		}

		public static void AppendBytes(ushort us, byte[] bytes, ref int pos)
		{
			AppendBytes(new byteUnion16() { us = us }, bytes, ref pos);
		}

		public static void AppendBytes(int i, byte[] bytes, ref int pos)
		{
			AppendBytes(new byteUnion32() { i = i }, bytes, ref pos);
		}

		public static void AppendBytes(uint ui, byte[] bytes, ref int pos)
		{
			AppendBytes(new byteUnion32() { ui = ui }, bytes, ref pos);
		}

		public static void AppendBytes(float f, byte[] bytes, ref int pos)
		{
			AppendBytes(new byteUnion32() { f = f }, bytes, ref pos);
		}

		public static void AppendBytes(long l, byte[] bytes, ref int pos)
		{
			AppendBytes(new byteUnion64() { l = l }, bytes, ref pos);
		}

		public static void AppendBytes(ulong ul, byte[] bytes, ref int pos)
		{
			AppendBytes(new byteUnion64() { ul = ul }, bytes, ref pos);
		}

		public static void AppendBytes(double d, byte[] bytes, ref int pos)
		{
			AppendBytes(new byteUnion64() { d = d }, bytes, ref pos);
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

		public static void AppendBytes(List<byte> bytes, char c)
		{
			int i = (int)c;
			if (i < 256)
				AppendBytes(bytes, (byte)i);
			else
				AppendBytes(bytes, unknownChar);
		}

		public static void AppendBytes(List<byte> bytes, string s)
		{
			foreach (char c in s)
			{
				int i = (int)c;
				if (i < 256)
					AppendBytes(bytes, (byte)i);
				else
					AppendBytes(bytes,unknownChar);
			}
		}

		public static void AppendBytes(List<byte> bytes, object data)
		{
			TypeCode code = Convert.GetTypeCode(data);
			switch (code)
			{
				case TypeCode.Boolean:
					AppendBytes(bytes, (bool)data);
					break;
				case TypeCode.Byte:
					AppendBytes(bytes, (byte)data);
					break;
				case TypeCode.Int16:
					AppendBytes(bytes, (short)data);
					break;
				case TypeCode.UInt16:
					AppendBytes(bytes, (ushort)data);
					break;
				case TypeCode.Int32:
					AppendBytes(bytes, (int)data);
					break;
				case TypeCode.UInt32:
					AppendBytes(bytes, (uint)data);
					break;
				case TypeCode.Int64:
					AppendBytes(bytes, (long)data);
					break;
				case TypeCode.UInt64:
					AppendBytes(bytes, (ulong)data);
					break;
				case TypeCode.Single:
					AppendBytes(bytes, (float)data);
					break;
				case TypeCode.Double:
					AppendBytes(bytes, (double)data);
					break;
				case TypeCode.String:
					AppendBytes(bytes, (string)data);
					break;
				default:
					throw new InvalidCastException("Argument is not a primitive: " + code + ", " + data);
			}
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

		public static char GetChar(byte[] bytes, ref int pos)
		{
			return (char)GetByte(bytes, ref pos);
		}

		public static object GetOfType(byte[] bytes, TypeCode code, ref int pos)
		{
			switch (code)
			{
				case TypeCode.Boolean:
					return GetBool(bytes, ref pos);
				case TypeCode.Byte:
					return GetByte(bytes, ref pos);
				case TypeCode.Int16:
					return GetShort(bytes, ref pos);
				case TypeCode.UInt16:
					return GetUshort(bytes, ref pos);
				case TypeCode.Int32:
					return GetInt(bytes, ref pos);
				case TypeCode.UInt32:
					return GetUint(bytes, ref pos);
				case TypeCode.Int64:
					return GetLong(bytes, ref pos);
				case TypeCode.UInt64:
					return GetUlong(bytes, ref pos);
				case TypeCode.Single:
					return GetFloat(bytes, ref pos);
				case TypeCode.Double:
					return GetFloat(bytes, ref pos);
				case TypeCode.Char:
					return GetChar(bytes, ref pos);
			}
			throw new ArgumentException("Invalid TypeCode: " + code);
		}

		public static string GetString(byte[] bytes, ref int pos)
		{
			return GetString(bytes, bytes.Length - pos, ref pos);
		}

		public static string GetString(byte[] bytes, int length, ref int pos)
		{
			char[] result = new char[length];
			for (int index = 0; index < length; index++)
				result[index] = GetChar(bytes, ref pos);
			return new string(result);
		}

		#endregion From Byte Array

	}
}
