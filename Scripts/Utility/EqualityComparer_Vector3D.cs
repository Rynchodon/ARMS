using System.Collections;
using System.Collections.Generic;
using VRageMath;

namespace Rynchodon.Utility
{
	public class EqualityComparer_Vector3D : IEqualityComparer<Vector3D>, IEqualityComparer
	{
		public static readonly EqualityComparer_Vector3D Instance = new EqualityComparer_Vector3D();

		private EqualityComparer_Vector3D() { }

		public new bool Equals(object x, object y)
		{
			return Equals((Vector3D)x, (Vector3D)y);
		}

		public bool Equals(Vector3D left, Vector3D right)
		{
			return left.X == right.X && left.Y == right.Y && left.Z == right.Z;
		}

		public int GetHashCode(object obj)
		{
			return obj.GetHashCode();
		}

		public int GetHashCode(Vector3D obj)
		{
			return obj.GetHashCode();
		}

	}
}
