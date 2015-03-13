using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;

namespace Rynchodon
{
	public static class Utility
	{
		public static bool softContains(this string bigString, string smallString)
		{
			string compare1 = bigString.Replace(" ", string.Empty).ToUpper();
			string compare2 = smallString.Replace(" ", string.Empty).ToUpper();
			return compare1.Contains(compare2);
		}
	}
}
