// skip file on build

using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

namespace Rynchodon
{
	/// <summary>
	/// violates standard conventions and stuff
	/// </summary>
	public static class Int64ToBase64
	{
		private static string chars = "1234567890qwertyuiopasdfghjklzxcvbnmQWERTYUIOPASDFGHJKLZXCVBNM!@";

		public static string toBase64(Int64 num)
		{
			string result = string.Empty;
			char[] digits = new char[11];
			if (num < 0)
			{
				result = "-";
				num = -num;
			}
			for (int index = 11; index >= 0; index--)
			{
				digits[index] = chars[(int)(num % 64)];
				num /= 64;
			}
			return result + new	string(digits);
		}

		public static Int64 toInt64(string num)
		{

		}

	}
}
