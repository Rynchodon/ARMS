#define LOG_ENABLED // remove on build

using System;
using Sandbox.Game.Entities;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon
{
	public static class CapsuleExtensions
	{
		private static Logger myLogger = new Logger();

		public static Vector3 get_Middle(this Capsule cap)
		{ return (cap.P0 + cap.P1) / 2f; }

		/// <summary>
		/// Length Squared P0 to P1
		/// </summary>
		public static float get_lengthSquared(this Capsule cap)
		{ return (cap.P0 - cap.P1).LengthSquared(); }

		/// <summary>
		/// Length P0 to P1
		/// </summary>
		public static float get_length(this Capsule cap)
		{ return (cap.P0 - cap.P1).Length(); }

		/// <summary>
		/// Gets the line from P0 to P1.
		/// </summary>
		/// <param name="cap">Capsule to get line for</param>
		/// <returns>A line from P0 to P1</returns>
		/// <remarks>
		/// Does not calculate AABB
		/// </remarks>
		public static Line get_Line(this Capsule cap)
		{ return new Line(cap.P0, cap.P1, false); }

		/// <summary>
		/// Gets the line segment from P0 to P1.
		/// </summary>
		/// <param name="cap">Capsule to get line segment for</param>
		/// <returns>A LineSegment from P0 to P1.</returns>
		public static LineSegment get_LineSegment(this Capsule cap)
		{ return new LineSegment(cap.P0, cap.P1); }

		/// <summary>
		/// Gets a Capsule where P0 and P1 are reversed.
		/// </summary>
		/// <param name="cap">Capsule to reverse</param>
		/// <returns>A capsule where P0 and P1 are reversed.</returns>
		public static Capsule get_Reverse(this Capsule cap)
		{ return new Capsule(cap.P1, cap.P0, cap.Radius); }

		/// <summary>
		/// Tests the WorldAABB of an entity for intersection with a capsule
		/// </summary>
		/// <param name="cap">Capsule to test for intersection</param>
		/// <param name="entity">WorldAABB will be tested against capsule</param>
		/// <param name="distance">Distance at which path intersects AABB</param>
		/// <returns>true if there is an intersection (including boundary)</returns>
		public static bool IntersectsAABB(this Capsule cap, IMyEntity entity, out float distance)
		{
			BoundingBox AABB = (BoundingBox)entity.WorldAABB;
			Vector3 Radius = new Vector3(cap.Radius, cap.Radius, cap.Radius);
			AABB = new BoundingBox(AABB.Min - Radius, AABB.Max + Radius);
			//myLogger.debugLog("Testing AABB: " + AABB.Min + ", " + AABB.Max + " against line: " + cap.P0 + ", " + cap.P1, "IntersectsAABB()");
			return (AABB.Intersects(cap.get_Line(), out distance));
		}

		/// <summary>
		/// Tests the WorldAABB of an entity for intersection with a capsule
		/// </summary>
		/// <param name="cap">Capsule to test for intersection</param>
		/// <param name="entity">WorldAABB will be tested against capsule</param>
		/// <returns>true if there is an intersection (including boundary)</returns>
		public static bool IntersectsAABB(this Capsule cap, IMyEntity entity)
		{
			float distance;
			return IntersectsAABB(cap, entity, out  distance);
		}

		/// <summary>
		/// Tests the WorldAABB of an entity for intersection with a capsule
		/// </summary>
		/// <param name="cap">Capsule to test for intersection</param>
		/// <param name="entity">WorldAABB will be tested against capsule</param>
		/// <param name="intersection0">A point along the line of Capsule near the intersection</param>
		/// <returns>true if there is an intersection (including boundary)</returns>
		public static bool IntersectsAABB(this Capsule cap, IMyEntity entity, out Vector3 intersection0)
		{
			BoundingBox AABB = (BoundingBox)entity.WorldAABB;
			Vector3 Radius = new Vector3(cap.Radius, cap.Radius, cap.Radius);
			AABB = new BoundingBox(AABB.Min - Radius, AABB.Max + Radius);

			Line capLine = cap.get_Line();
			float distance;
			if (AABB.Intersects(capLine, out distance))
			{
				intersection0 = capLine.From + capLine.Direction / capLine.Length * distance;
				return true;
			}

			intersection0 = new Vector3();
			return false;
		}

		/// <summary>
		/// Tests the WorldVolume of an entity for intersection with a capsule
		/// </summary>
		/// <param name="cap">Capsule to test for intersection</param>
		/// <param name="entity">WorldVolume will be tested against capsule</param>
		/// <returns>true if there is an intersection (including boundary)</returns>
		public static bool IntersectsVolume(this Capsule cap, IMyEntity entity)
		{
			BoundingSphere Volume = (BoundingSphere)entity.WorldVolume;
			return cap.get_Line().DistanceLessEqual(Volume.Center, cap.Radius + Volume.Radius);
		}

		/// <summary>
		/// Tests the WorldVolume of an entity for intersection with a capsule
		/// </summary>
		/// <param name="cap">Capsule to test for intersection</param>
		/// <param name="entity">WorldVolume will be tested against capsule</param>
		/// <param name="distance">distance from capsule to WorldVolume</param>
		/// <returns>true if there is an intersection (including boundary)</returns>
		public static bool IntersectsVolume(this Capsule cap, IMyEntity entity, out float distance)
		{
			BoundingSphere Volume = (BoundingSphere)entity.WorldVolume;
			distance = cap.get_Line().Distance(Volume.Center) - cap.Radius - Volume.Radius;
			return distance <= 0;
		}

		/// <summary>
		/// Tests whether or not a position is intersecting a capsule
		/// </summary>
		/// <param name="cap">capsule to test for intersection</param>
		/// <param name="worldPosition">position to test for intersection</param>
		/// <param name="buffer">normally, the grid size of the grid supplying the worldPosition. added to the radius of the capsule</param>
		/// <returns>true if the worldPosition intersects the capsule (including boundary)</returns>
		public static bool Intersects(this Capsule cap, Vector3 worldPosition, float buffer)
		{ return cap.get_Line().DistanceLessEqual(worldPosition, cap.Radius + buffer); }

		/// <summary>
		/// Tests whether or not a position is intersecting a capsule
		/// </summary>
		/// <param name="cap">capsule to test for intersection</param>
		/// <param name="worldPosition">position to test for intersection</param>
		/// <param name="buffer">normally, the grid size of the grid supplying the worldPosition. added to the radius of the capsule</param>
		/// <param name="distance">distance between capsule and worldPosition (- buffer)</param>
		/// <returns>true if the worldPosition intersects the capsule (including boundary)</returns>
		public static bool Intersects(this Capsule cap, Vector3 worldPosition, float buffer, out float distance)
		{
			distance = cap.get_Line().Distance(worldPosition) - cap.Radius - buffer;
			return distance <= 0;
		}

		/// <summary>
		/// Test two capsules for intersection
		/// </summary>
		/// <param name="capsule">first capsule to test</param>
		/// <param name="other">second capsule to test</param>
		/// <param name="shortestDistanceSquared">distance squared between lines of capsules</param>
		/// <returns>true if the capsules intersect (including boundary)</returns>
		public static bool Intersects(this Capsule capsule, Capsule other, out float shortestDistanceSquared)
		{
			shortestDistanceSquared = Line.GetShortestDistanceSquared(capsule.get_Line(), other.get_Line());
			float radiiSquared = capsule.Radius + other.Radius;
			radiiSquared *= radiiSquared;
			return shortestDistanceSquared <= radiiSquared;
		}

		/// <summary>
		/// Performs a binary search for intersection using spheres.
		/// </summary>
		/// <param name="pointOfObstruction">a point on the capsule's line close to obstruction</param>
		public static bool Intersects(this Capsule capsule, IMyVoxelMap asteroid, out Vector3? pointOfObstruction, float capsuleLength = -1)
		{
			if (capsuleLength < 0)
				capsuleLength = capsule.get_length();
			float halfLength = capsuleLength / 2;
			Vector3 middle = capsule.get_Middle();

			BoundingSphereD containingSphere = new BoundingSphereD(middle, halfLength + capsule.Radius);
			if (!asteroid.GetIntersectionWithSphere(ref containingSphere))
			{
				pointOfObstruction = null;
				return false;
			}

			if (capsuleLength < 1f)
			{
				pointOfObstruction = containingSphere.Center;
				return true;
			}

			return Intersects(new Capsule(capsule.P0, middle, capsule.Radius), asteroid, out pointOfObstruction, halfLength)
				|| Intersects(new Capsule(capsule.P1, middle, capsule.Radius), asteroid, out pointOfObstruction, halfLength);
		}

		//public static bool Intersects(this Capsule capsule, MyPlanet planet, out Vector3? pointOfObstruction, float capsuleLength = -1)
		//{
		//	if (capsuleLength < 0)
		//		capsuleLength = capsule.get_length();
		//	float halfLength = capsuleLength / 2;
		//	Vector3 middle = capsule.get_Middle();

		//	BoundingSphereD checkSphere = new BoundingSphereD(middle, halfLength + capsule.Radius);
		//	if (!planet.Intersects(ref checkSphere))
		//	{
		//		pointOfObstruction = null;
		//		return false;
		//	}

		//	if (capsuleLength < 1f)
		//	{
		//		pointOfObstruction = middle;
		//		return true;
		//	}

		//	return Intersects(new Capsule(capsule.P0, middle, capsule.Radius), planet, out pointOfObstruction, halfLength)
		//		|| Intersects(new Capsule(capsule.P1, middle, capsule.Radius), planet, out pointOfObstruction, halfLength);
		//}

	}
}
