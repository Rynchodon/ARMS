
using Sandbox.Game.Entities;
using VRageMath;

namespace Rynchodon
{
	public static class MyPlanetExtensions
	{

		public static bool Intersects(this MyPlanet planet, BoundingSphereD sphere)
		{
			Vector3D centre = sphere.Center;
			Vector3D closestPoint = planet.GetClosestSurfacePointGlobal(ref centre);
			double minDistance = sphere.Radius; minDistance *= minDistance;
			return Vector3D.DistanceSquared(centre, closestPoint) <= minDistance;
		}

	}
}
