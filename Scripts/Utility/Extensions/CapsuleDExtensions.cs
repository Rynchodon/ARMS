using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon
{
	public static class CapsuleDExtensions
	{

		/// <summary>
		/// Performs a binary search for intersection using spheres.
		/// </summary>
		/// <param name="pointOfObstruction">a point on the capsule's line close to obstruction</param>
		public static bool Intersects(this CapsuleD capsule, IMyVoxelMap asteroid, out Vector3D pointOfObstruction, double capsuleLength = -1d)
		{
			if (capsuleLength < 0)
				Vector3D.Distance(ref capsule.P0, ref capsule.P1, out capsuleLength);
			double halfLength = capsuleLength / 2d;
			Vector3D temp; Vector3D.Add(ref capsule.P0, ref capsule.P1, out temp);
			Vector3D middle; Vector3D.Divide(ref temp, 2d, out middle);

			BoundingSphereD containingSphere = new BoundingSphereD(temp, halfLength + capsule.Radius);
			if (!asteroid.GetIntersectionWithSphere(ref containingSphere))
			{
				pointOfObstruction = Vector3.Invalid;
				return false;
			}

			if (capsuleLength < 1f)
			{
				pointOfObstruction = containingSphere.Center;
				return true;
			}

			return Intersects(new CapsuleD(capsule.P0, temp, capsule.Radius), asteroid, out pointOfObstruction, halfLength)
				|| Intersects(new CapsuleD(capsule.P1, temp, capsule.Radius), asteroid, out pointOfObstruction, halfLength);
		}

	}
}
