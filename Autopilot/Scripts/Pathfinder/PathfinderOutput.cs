// skip file on build

using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinder
{
	internal class PathfinderOutput
	{
		public enum Result : byte { Incomplete, Path_Clear, Searching_Alt, Alternate_Path, No_Way_Forward }

		public readonly Result PathfinderResult;
		//public readonly IMyEntity Obstruction;
		public readonly Vector3D Waypoint;

		/// <summary>
		/// <para>Distance to closest entity, has nothing to do with obstruction.</para>
		/// <para>May be negative.</para>
		/// </summary>
		public double DistanceToClosest { get { return lazy_DistanceToClosest.Value; } }
		private Lazy<double> lazy_DistanceToClosest;

		public PathfinderOutput(Result PathfinderResult, /*PathChecker getClosestFrom = null, IMyEntity Obstruction = null,*/ Vector3D Waypoint = new Vector3D())
		{
			this.PathfinderResult = PathfinderResult;
			//this.Obstruction = Obstruction;
			this.Waypoint = Waypoint;

			//if (getClosestFrom != null)
			//switch (PathfinderResult)
			//{
			//	case Result.Incomplete:
			//	case Result.Path_Clear:
			//	case Result.Alternate_Path:
			//		this.lazy_DistanceToClosest = new Lazy<double>(() => { return getClosestFrom.ClosestEntity(); });
			//		break;
			//	case Result.Searching_Alt:
			//	case Result.No_Way_Forward:
			//		this.lazy_DistanceToClosest = new Lazy<double>(() => { return PathChecker.NearbyRange; });
			//		break;
			//}
		}
	}
}
