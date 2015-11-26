using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRageMath;

namespace Rynchodon
{
	public static class MyPlanetExtensions
	{

		private static readonly Logger s_logger = new Logger("MyPlanetExtensions");

		private static readonly FastResourceLock lock_getSurfPoint = new FastResourceLock("lock_getSurfPoint");

		public static bool Intersects(this MyPlanet planet, ref BoundingSphereD sphere)
		{
			Vector3D sphereCentre = sphere.Center;
			Vector3D planetCentre = planet.GetCentre();

			double distSq_sphereToPlanetCentre = Vector3D.DistanceSquared(sphereCentre, planetCentre);
			double everest = planet.MaximumRadius + sphere.Radius; everest *= everest;
			if (distSq_sphereToPlanetCentre > everest)
				return false;

			Vector3D closestPoint = GetClosestSurfacePointGlobal_Safeish(planet, sphereCentre);
			s_logger.debugLog("got surface point: " + closestPoint, "Intersects()");

			double minDistance = sphere.Radius * sphere.Radius;
			if (Vector3D.DistanceSquared(sphereCentre, closestPoint) <= minDistance)
				return true;

			return distSq_sphereToPlanetCentre < Vector3D.DistanceSquared(planetCentre, closestPoint);
		}

		public static Vector3D GetClosestSurfacePointGlobal_Safeish(this MyPlanet planet, Vector3D worldPoint)
		{
			bool except = false;
			Vector3D surface = Vector3D.Zero;
			MainLock.UsingShared(() => except = GetClosestSurfacePointGlobal_Sub_Safeish(planet, worldPoint, out surface));

			if (except)
				return GetClosestSurfacePointGlobal_Safeish(planet, worldPoint);
			return surface;
		}

		[System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
		private static bool GetClosestSurfacePointGlobal_Sub_Safeish(this MyPlanet planet, Vector3D worldPoint, out Vector3D closestPoint)
		{
			using (lock_getSurfPoint.AcquireExclusiveUsing())
				try
				{
					closestPoint = planet.GetClosestSurfacePointGlobal(ref worldPoint);
					return false;
				}
				catch (AccessViolationException ex)
				{
					s_logger.debugLog("Caught Exception: " + ex, "GetClosestSurfacePointGlobal_Sub_Safeish()", Logger.severity.DEBUG);
					closestPoint = Vector3D.Zero;
					return true;
				}
		}

	}
}
