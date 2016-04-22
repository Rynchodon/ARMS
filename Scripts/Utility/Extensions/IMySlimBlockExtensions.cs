using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using VRage.Game.ModAPI;
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

		public static IEnumerator<Vector3I> ForEachCell(this IMySlimBlock block)
		{
			IMyCubeBlock FatBlock = block.FatBlock;
			if (FatBlock == null)
				yield return block.Position;
			else
			{
				Vector3I vector;
				for (vector.X = FatBlock.Min.X; vector.X <= FatBlock.Max.X; vector.X++)
					for (vector.Y = FatBlock.Min.Y; vector.Y <= FatBlock.Max.Y; vector.Y++)
						for (vector.Z = FatBlock.Min.Z; vector.Z <= FatBlock.Max.Z; vector.Z++)
							yield return vector;
			}
		}

		public static IEnumerator<Vector3I> ForEachNeighbourCell(this IMySlimBlock block)
		{
			Vector3I min, max;

			IMyCubeBlock FatBlock = block.FatBlock;
			if (FatBlock == null)
			{
				min = block.Position - 1;
				max = block.Position + 1;
			}
			else
			{
				min = FatBlock.Min - 1;
				max = FatBlock.Max + 1;
			}

			Vector3I neighbour;

			neighbour.X = min.X;
			for (neighbour.Y = min.Y; neighbour.Y <= max.Y; neighbour.Y++)
				for (neighbour.Z = min.Z; neighbour.Z <= max.Z; neighbour.Z++)
					yield return neighbour;

			neighbour.X = max.X;
			for (neighbour.Y = min.Y; neighbour.Y <= max.Y; neighbour.Y++)
				for (neighbour.Z = min.Z; neighbour.Z <= max.Z; neighbour.Z++)
					yield return neighbour;

			min.X += 1;
			max.X -= 1;

			neighbour.Y = min.Y;
			for (neighbour.X = min.X; neighbour.X <= max.X; neighbour.X++)
				for (neighbour.Z = min.Z; neighbour.Z <= max.Z; neighbour.Z++)
					yield return neighbour;

			neighbour.Y = max.Y;
			for (neighbour.X = min.X; neighbour.X <= max.X; neighbour.X++)
				for (neighbour.Z = min.Z; neighbour.Z <= max.Z; neighbour.Z++)
					yield return neighbour;

			min.Y += 1;
			max.Y -= 1;

			neighbour.Z = min.Z;
			for (neighbour.X = min.X; neighbour.X <= max.X; neighbour.X++)
				for (neighbour.Y = min.Y; neighbour.Y <= max.Y; neighbour.Y++)
					yield return neighbour;

			neighbour.Z = max.Z;
			for (neighbour.X = min.X; neighbour.X <= max.X; neighbour.X++)
				for (neighbour.Y = min.Y; neighbour.Y <= max.Y; neighbour.Y++)
					yield return neighbour;
		}

		public static Vector3 LocalPosition(this IMySlimBlock block)
		{
			if (block.FatBlock != null)
				return block.FatBlock.LocalPosition();
			return block.Position * block.CubeGrid.GridSize;
		}

		public static bool Closed(this IMySlimBlock block)
		{
			return block.IsDestroyed || block.IsFullyDismounted;
		}

		public static Vector3I Min(this IMySlimBlock block)
		{
			if (block.FatBlock != null)
				return block.FatBlock.Min;
			return block.Position;
		}

		public static Vector3I Max(this IMySlimBlock block)
		{
			if (block.FatBlock != null)
				return block.FatBlock.Max;
			return block.Position;
		}

		/// <summary>CurrentDamage + 1f - BuildLevelRatio</summary>
		public static float Damage(this IMySlimBlock block)
		{
			return block.CurrentDamage + 1f - block.BuildLevelRatio;
		}

	}
}
