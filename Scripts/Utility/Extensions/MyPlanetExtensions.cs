
using System;
using Sandbox.Game.Entities;
using VRageMath;

namespace Rynchodon
{
	public static class MyPlanetExtensions
	{

		private static readonly Logger s_logger = new Logger("MyPlanetExtensions");

		[System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
		public static bool Intersects(this MyPlanet planet, ref BoundingSphereD sphere)
		{
			Vector3D centre = sphere.Center;
			Vector3D closestPoint = Vector3.Zero;

			// obviously not ideal but there does not seem to be any alternative
			try { closestPoint = planet.GetClosestSurfacePointGlobal(ref centre); }
			catch (AccessViolationException)
			{
				s_logger.debugLog("Caught AccessViolationException", "Intersects()", Logger.severity.INFO);
				return true;
			}
			
			double minDistance = sphere.Radius * sphere.Radius;
			s_logger.debugLog("checking intersection"
					+ ", sphere centre: " + centre
					+ ", sphere radius: " + sphere.Radius
					+ ", surface point: " + closestPoint
					+ ", distance to surface point: " + Vector3D.Distance(centre, closestPoint)
					, "Intersects()");
			if (Vector3D.DistanceSquared(centre, closestPoint) <= minDistance)
				return true;

			Vector3D planetCentre = planet.GetCentre();
			s_logger.debugLog("altitude of sphere: " + Vector3D.Distance(planetCentre, centre) + ", altitude of surface point: " + Vector3D.Distance(planetCentre, closestPoint), "Intersects()");
			bool pointTest = Vector3D.DistanceSquared(planetCentre, centre) < Vector3D.DistanceSquared(planetCentre, closestPoint);
			bool overlapTest = planet.DoOverlapSphereTest((float)sphere.Radius, sphere.Center);
			s_logger.debugLog("point test: " + pointTest + ", overlap test: " + overlapTest, "Intersects()");
			return pointTest;
		}

	}
}
