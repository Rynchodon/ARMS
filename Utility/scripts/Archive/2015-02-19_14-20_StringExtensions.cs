using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;

namespace Rynchodon
{
	public static class StringExtensions
	{
		public static bool looseContains(this string bigString, string smallString)
		{
			string compare1 = bigString.Replace(" ", string.Empty);
			string compare2 = smallString.Replace(" ", string.Empty);
			return compare1.Contains(compare2, StringComparison.OrdinalIgnoreCase);
		}
	}
}
