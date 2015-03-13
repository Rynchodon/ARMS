using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;

namespace Rynchodon
{
	public static class StringExt
	{
		public static bool looseContains(string bigString, string smallString)
		{
			string compare1 = bigString.Replace(" ", string.Empty);
			string compare2 = smallString.Replace(" ", string.Empty);
			return compare1.Contains(compare2, StringComparison.OrdinalIgnoreCase);
		}
	}
}
