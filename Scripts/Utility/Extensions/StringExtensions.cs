using System;
using System.Text.RegularExpressions;

namespace Rynchodon
{
	public static class StringExtensions
	{

		public static bool looseContains(this string bigString, string smallString)
		{
			VRage.Exceptions.ThrowIf<ArgumentNullException>(bigString == null, "bigString");
			VRage.Exceptions.ThrowIf<ArgumentNullException>(smallString == null, "smallString");

			bigString = bigString.LowerRemoveWhitespace();
			smallString = smallString.LowerRemoveWhitespace();
			return bigString.Contains(smallString);
		}

		/// <remarks>
		/// From http://stackoverflow.com/a/20857897
		/// </remarks>
		public static string RemoveWhitespace(this string input)
		{
			int j = 0, inputlen = input.Length;
			char[] newarr = new char[inputlen];

			for (int i = 0; i < inputlen; ++i)
			{
				char tmp = input[i];

				if (!char.IsWhiteSpace(tmp))
				{
					newarr[j] = tmp;
					++j;
				}
			}

			if (j == inputlen)
				return input;
			return new String(newarr, 0, j);
		}

		/// <summary>
		/// Convert a string to lower case and remove whitespace.
		/// </summary>
		public static string LowerRemoveWhitespace(this string input)
		{
			int outIndex = 0;
			char[] output = new char[input.Length];

			for (int inIndex = 0; inIndex < input.Length; inIndex++)
			{
				char current = input[inIndex];

				if (!char.IsWhiteSpace(current))
				{
					output[outIndex] = char.ToLower(current);
					outIndex++;
				}
			}

			return new String(output, 0, outIndex);
		}

		public static bool Contains(this string bigString, string littleString, StringComparison compare = StringComparison.InvariantCultureIgnoreCase)
		{
			return bigString.IndexOf(littleString, compare) != -1;
		}

	}
}
