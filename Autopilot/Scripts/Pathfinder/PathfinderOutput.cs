using Sandbox.ModAPI;
using System;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinder
{
	//public class PathCheckOutput
	//{
	//	public enum Result : byte { Incomplete, Clear, Obstruction }

	//	public readonly Result PathCheckResult;
	//	/// <summary>
	//	/// Obstructing Entity or Closest Entity
	//	/// </summary>
	//	public readonly IMyEntity ClosestEntity;
	//	/// <summary>
	//	/// Distance to Obstructing Entity or Closest Entity
	//	/// </summary>
	//	public readonly float Distance;

	//	public static PathCheckOutput Incomplete;

	//	static PathCheckOutput()
	//	{ Incomplete = new PathCheckOutput(Result.Incomplete); }

	//	private PathCheckOutput(Result PathCheckResult = Result.Incomplete, IMyEntity Closest = null, float Distance = float.MaxValue)
	//	{
	//		this.PathCheckResult = PathCheckResult;
	//		this.ClosestEntity = Closest;
	//		this.Distance = Distance;
	//	}

	//	public static PathCheckOutput Clear(IMyEntity Closest = null, float Distance = float.MaxValue)
	//	{
			 
	//	}



	//}

	public class PathfinderOutput
	{
		public enum Result : byte { Incomplete, Path_Clear, Searching_Alt, Alternate_Path, No_Way_Forward }
		
		public readonly Result PathfinderResult;
		public readonly IMyEntity Obstruction;
		public readonly Vector3D Waypoint;

		public PathfinderOutput(Result PathfinderResult, IMyEntity Obstruction = null, Vector3D Waypoint = new Vector3D())
		{
			this.PathfinderResult = PathfinderResult;
			this.Obstruction = Obstruction;
			this.Waypoint = Waypoint;
		}
	}
}
