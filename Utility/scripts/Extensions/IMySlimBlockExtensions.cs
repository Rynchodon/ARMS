using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

		///// <summary>
		///// performs an action on each cell a block occupies plus all the surrounding cells
		///// </summary>
		///// <param name="invokeOnEach">function to call for each vector, if it return true short-curcuit</param>
		//public static void ForEachCellSurround(this IMySlimBlock block, Func<Vector3I, bool> invokeOnEach, int extend = 1)
		//{
		//	IMyCubeBlock FatBlock = block.FatBlock;
		//	if (FatBlock == null)
		//	{
		//		Vector3I min, max;
		//		min.ForEachVector(max, invokeOnEach);
		//	}
		//	else
		//	{
		//		float gridSize = block.CubeGrid.GridSize;
		//		Vector3I min, max;
		//		min.ForEachVector(max, invokeOnEach);
		//	}
		//}

		public static Vector3 LocalPosition(this IMySlimBlock block)
		{ return block.Position * block.CubeGrid.GridSize; }
	}
}
