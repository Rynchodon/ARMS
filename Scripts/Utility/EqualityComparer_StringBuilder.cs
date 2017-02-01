using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Rynchodon.Utility
{
	public class EqualityComparer_StringBuilder : IEqualityComparer<StringBuilder>, IEqualityComparer
	{

	 	public new bool Equals(object x, object y)
		{
			return Equals((StringBuilder)x, (StringBuilder)y);
		}

		public bool Equals(StringBuilder x, StringBuilder y)
		{
			return x.EqualsIgnoreCapacity(y);
		}

		public int GetHashCode(object obj)
		{
			return GetHashCode((StringBuilder)obj);
		}

		public int GetHashCode(StringBuilder obj)
		{
			return obj.ToString().GetHashCode();
		}

	}
}
