
using System;
using Sandbox.Game.Entities;
using VRageMath;

namespace Rynchodon
{
	public static class MyPlanetExtensions
	{

		private static readonly Logger s_logger = new Logger("MyPlanetExtensions");
		private static readonly FastResourceLock lock_getSurfPoint = new FastResourceLock();

		[System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
		public static bool Intersects(this MyPlanet planet, ref BoundingSphereD sphere)
		{
			Vector3D centre = sphere.Center;
			Vector3D closestPoint = Vector3.Zero;

			// obviously not ideal but there does not seem to be any alternative
			try
			{
				using (lock_getSurfPoint.AcquireExclusiveUsing())
					closestPoint = planet.GetClosestSurfacePointGlobal(ref centre);
			}
			catch (Exception ex)
			{
				s_logger.debugLog("Caught Exception: " + ex, "Intersects()", Logger.severity.WARNING);
				return true;
			}

			double minDistance = sphere.Radius * sphere.Radius;
			if (Vector3D.DistanceSquared(centre, closestPoint) <= minDistance)
				return true;

			Vector3D planetCentre = planet.GetCentre();
			return Vector3D.DistanceSquared(planetCentre, centre) < Vector3D.DistanceSquared(planetCentre, closestPoint);
		}

	}
}
