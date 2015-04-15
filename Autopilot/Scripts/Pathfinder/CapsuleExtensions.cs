using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinder
{
	/// <summary>
	/// Capsules are suited for testing paths for obstructions, but they lack the appropriate methods.
	/// </summary>
	public static class CapsuleExtensions
	{
		/// <summary>
		/// <para>Not Implemented</para>
		/// <para>Tests whether or not the grid interesects with this capsule.</para>
		/// </summary>
		public static bool IntersectsWith(this Capsule capsule, GridShapeProfiler grid)
		{
			VRage.Exceptions.ThrowIf<NotImplementedException>(true);
			return true;
		}

		/// <summary>
		/// <para>Not Implemented</para>
		/// <para>Tests whether or not the grid interesects with this capsule.</para>
		/// </summary>
		public static bool IntersectsWith(this Capsule capsule, IMyCubeGrid grid)
		{ return (IntersectsWith(capsule, GridShapeProfiler.getFor(grid))); }

		/// <summary>
		/// <para>Not Implemented</para>
		/// <para>Tests whether or not the asteroid interesects with this capsule.</para>
		/// </summary>
		public static bool IntersectsWith(this Capsule capsule, IMyVoxelMap asteroid)
		{
			VRage.Exceptions.ThrowIf<NotImplementedException>(true);
			return true;
		}

		/// <summary>
		/// <para>Tests whether or not another capsule intersects with this capsule.</para>
		/// </summary>
		public static bool IntersectsWith(this Capsule capsule, Capsule other)
		{
			float shortestDistanceSquared = Line.GetShortestDistanceSquared(new Line(capsule.P0, capsule.P1), new Line(other.P0, other.P1));
			float radiiSquared = capsule.Radius + other.Radius;
			radiiSquared *= radiiSquared;
			return shortestDistanceSquared <= radiiSquared;
		}
	}
}
