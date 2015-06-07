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
			{
				invokeOnEach.Invoke(block.Position);
			}
			else
			{
				//float gridSize = block.CubeGrid.GridSize;
				//Vector3I min, max;
				//FatBlock.LocalAABB.Min.ApplyOperation((metresComp) => { return (int)Math.Round(metresComp / gridSize); }, out min);
				//FatBlock.LocalAABB.Max.ApplyOperation((metresComp) => { return (int)Math.Round(metresComp / gridSize); }, out max);
				FatBlock. Min.ForEachVector(FatBlock.Max, invokeOnEach);
			}
		}

		public static Vector3 LocalPosition(this IMySlimBlock block)
		{ return block.Position * block.CubeGrid.GridSize; }
	}
}
