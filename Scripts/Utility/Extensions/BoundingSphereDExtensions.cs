using System;
using VRageMath;

namespace Rynchodon
{
	static class BoundingSphereDExtensions
	{

		public static void GetRandomPointOnSphere(this BoundingSphereD sphere, out Vector3D point)
		{
			double theta = 2d * Math.PI * Globals.Random.NextDouble();
			double cosPhi = 2 * Globals.Random.NextDouble() - 1d;
			double sinPhi = Math.Sin(Math.Acos(cosPhi));

			point.X = sphere.Radius * Math.Cos(theta) * sinPhi;
			point.Y = sphere.Radius * Math.Sin(theta) * sinPhi;
			point.Z = sphere.Radius * cosPhi;
		}

	}
}
