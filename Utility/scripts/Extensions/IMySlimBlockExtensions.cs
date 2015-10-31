using System;
using Sandbox.ModAPI;
using VRageMath;

namespace Rynchodon
{
	public static class IMySlimBlockExtensions
	{
		public static string getBestName(this IMySlimBlock slimBlock)
		{
			IMyCubeBlock Fatblock = slimBlock.FatBlock;
			if (Fatblock != null)
				return Fatblock.DisplayNameText;
			return slimBlock.ToString();
		}

		/// <summary>
		/// performs an action on each cell a block occupies
		/// </summary>
		/// <param name="invokeOnEach">function to call for each vector, if it return true short-curcuit</param>
		public static void ForEachCell(this IMySlimBlock block, Func<Vector3I, bool> invokeOnEach)
		{
			IMyCubeBlock FatBlock = block.FatBlock;
			if (FatBlock == null)
				invokeOnEach.Invoke(block.Position);
			else
				FatBlock.Min.ForEachVector(FatBlock.Max, invokeOnEach);
		}

		public static Vector3 LocalPosition(this IMySlimBlock block)
		{ return block.Position * block.CubeGrid.GridSize; }

	}
}
