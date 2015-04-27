using Sandbox.ModAPI;
using System;
using VRageMath;

namespace Rynchodon
{
	public static class CapsuleExtensions
	{
		public static Line get_Line(this Capsule cap)
		{ return new Line(cap.P0, cap.P1, false); }

		public static bool IntersectsAABB(this Capsule cap, IMyEntity entity)
		{
			BoundingBox AABB = (BoundingBox)entity.WorldAABB;
			AABB.Inflate(cap.Radius);
			float distance;
			return (AABB.Intersects(cap.get_Line(), out distance));
		}

		/// <param name="buffer">the grid size of the grid to test, added to the size of the grid that owns the path</param>
		public static bool Intersects(this Capsule cap, Vector3 worldPosition, float buffer)
		{
			float bufferedRadius = cap.Radius + buffer;
			float bufferedRadiusSquared = bufferedRadius * bufferedRadius;
			return cap.get_Line().DistanceSquared(worldPosition) <= bufferedRadiusSquared;
		}

		public static bool Intersects(this Capsule capsule, Capsule other)
		{
			float shortestDistanceSquared = Line.GetShortestDistanceSquared(capsule.get_Line(), other.get_Line());
			float radiiSquared = capsule.Radius + other.Radius;
			radiiSquared *= radiiSquared;
			return shortestDistanceSquared <= radiiSquared;
		}
	}
}
