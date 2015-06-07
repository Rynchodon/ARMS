using System;

namespace Rynchodon
{
	public static class StringExtensions
	{
		public static string getInstructions(this string displayName)
		{
			int start = displayName.IndexOf('[') + 1;
			int end = displayName.IndexOf(']');
			if (start > 0 && end > start) // has appropriate brackets
			{
				int length = end - start;
				return displayName.Substring(start, length);
			}
			return null;
		}

		public static bool looseContains(this string bigString, string smallString)
		{
			VRage.Exceptions.ThrowIf<ArgumentNullException>(bigString == null, "bigString");
			VRage.Exceptions.ThrowIf<ArgumentNullException>(smallString == null, "smallString");

			string compare1 = bigString.RemoveWhitespace().ToLower();
			string compare2 = smallString.RemoveWhitespace().ToLower();
			return compare1.Contains(compare2);
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

			return new String(newarr, 0, j);
		}
	}
}
