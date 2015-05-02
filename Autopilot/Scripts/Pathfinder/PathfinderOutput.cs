using Sandbox.ModAPI;
using System;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinder
{
	internal class PathfinderOutput
	{
		public enum Result : byte { Incomplete, Path_Clear, Searching_Alt, Alternate_Path, No_Way_Forward }

		public readonly Result PathfinderResult;
		public readonly IMyEntity Obstruction;
		public readonly Vector3D Waypoint;
		/// <summary>
		/// <para>Distance to closest entity, has nothing to do with obstruction.</para>
		/// <para>May be negative.</para>
		/// </summary>
		public readonly double DistanceToClosest;
		//public readonly bool AllowsMovement;

		public PathfinderOutput(PathChecker getClosestFrom, Result PathfinderResult, IMyEntity Obstruction = null, Vector3D Waypoint = new Vector3D())
		{
			this.PathfinderResult = PathfinderResult;
			this.Obstruction = Obstruction;
			this.Waypoint = Waypoint;

			switch (PathfinderResult)
			{
				case Result.Incomplete:
				case Result.Path_Clear:
				case Result.Alternate_Path:
					//AllowsMovement = true;
					this.DistanceToClosest = getClosestFrom.ClosestEntity();
					break;
				case Result.Searching_Alt:
				case Result.No_Way_Forward:
					//AllowsMovement = false;
					this.DistanceToClosest = PathChecker.NearbyRange;
					break;
			}
		}
	}
}