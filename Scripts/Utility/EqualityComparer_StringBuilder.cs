using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Rynchodon.Utility
{
	public class EqualityComparer_StringBuilder : IEqualityComparer<StringBuilder>, IEqualityComparer
	{

		public static readonly EqualityComparer_StringBuilder Instance = new EqualityComparer_StringBuilder();

		private EqualityComparer_StringBuilder() { }

		public new bool Equals(object x, object y)
		{
			return Equals((StringBuilder)x, (StringBuilder)y);
		}

		public bool Equals(StringBuilder x, StringBuilder y)
		{
			if (x == null)
				return y == null;
			if (y == null)
				return false;
			return ReferenceEquals(x, y) || x.EqualsIgnoreCapacity(y);
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
