using Sandbox.ModAPI;
using System;
using VRage;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinder
{
	/// <summary>
	/// Primary class for pathfinding
	/// </summary>
	public class Pathfinder
	{
		public readonly Vector3D Destination;
		public readonly IMyCubeBlock NavigationBlock;
		public readonly IMyCubeGrid CubeGrid;

		private PathfinderOutput Output;
		private FastResourceLock lock_Output = new FastResourceLock();

		public Pathfinder(Vector3D Destination, IMyCubeBlock NavigationBlock)
		{
			this.Destination = Destination;
			this.NavigationBlock = NavigationBlock;
			this.CubeGrid = NavigationBlock.CubeGrid;
		}
	}
}
