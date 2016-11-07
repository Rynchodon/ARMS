using System.Collections.Generic;
using VRageMath;

namespace Rynchodon.Utility
{
	public class EqualityComparer_Vector3D : IEqualityComparer<Vector3D>
	{

		public bool Equals(Vector3D left, Vector3D right)
		{
			return left.X == right.X && left.Y == right.Y && left.Z == right.Z;
		}

		public int GetHashCode(Vector3D obj)
		{
			return obj.GetHashCode();
		}

	}
}
