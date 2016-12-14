using System.Collections.Generic;
using Sandbox.Game.Entities.Cube;
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
				return Fatblock.getBestName();
			return slimBlock.ToString();
		}

		public static string nameWithId(this IMySlimBlock slimBlock)
		{
			IMyCubeBlock Fatblock = slimBlock.FatBlock;
			if (Fatblock != null)
				return Fatblock.nameWithId();
			return slimBlock.ToString();
		}
		
		public static IEnumerator<Vector3I> ForEachNeighbourCell(this IMySlimBlock block)
		{
			MySlimBlock proper = (MySlimBlock)block;
			Vector3I min = proper.Min, max = proper.Max;

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
			return ((MySlimBlock)block).Min;
		}

		public static Vector3I Max(this IMySlimBlock block)
		{
			return ((MySlimBlock)block).Max;
		}

		/// <summary>CurrentDamage + 1f - BuildLevelRatio</summary>
		public static float Damage(this IMySlimBlock block)
		{
			return block.CurrentDamage + 1f - block.BuildLevelRatio;
		}

		public static IEnumerable<Vector3I> FirstCells(this IMySlimBlock block, Base6Directions.Direction direction)
		{
			MySlimBlock slimBlock = (MySlimBlock)block;
			Vector3I min = slimBlock.Min, max = slimBlock.Max;
			Vector3I cell;

			switch (direction)
			{
				case Base6Directions.Direction.Right:
					cell.X = max.X;
					for (cell.Y = min.Y; cell.Y <= max.Y; ++cell.Y)
						for (cell.Z = min.Z; cell.Z <= max.Z; ++cell.Z)
							yield return cell;
					yield break;
				case Base6Directions.Direction.Left:
					cell.X = min.X;
					for (cell.Y = min.Y; cell.Y <= max.Y; ++cell.Y)
						for (cell.Z = min.Z; cell.Z <= max.Z; ++cell.Z)
							yield return cell;
					yield break;

				case Base6Directions.Direction.Up:
					cell.Y = max.Y;
					for (cell.X = min.X; cell.X <= max.X; ++cell.X)
						for (cell.Z = min.Z; cell.Z <= max.Z; ++cell.Z)
							yield return cell;
					yield break;
				case Base6Directions.Direction.Down:
					cell.Y = min.Y;
					for (cell.X = min.X; cell.X <= max.X; ++cell.X)
						for (cell.Z = min.Z; cell.Z <= max.Z; ++cell.Z)
							yield return cell;
					yield break;

				case Base6Directions.Direction.Backward:
					cell.Z = max.Z;
					for (cell.X = min.X; cell.X <= max.X; ++cell.X)
						for (cell.Y = min.Y; cell.Y <= max.Y; ++cell.Y)
							yield return cell;
					yield break;
				case Base6Directions.Direction.Forward:
					cell.Z = min.Z;
					for (cell.X = min.X; cell.X <= max.X; ++cell.X)
						for (cell.Y = min.Y; cell.Y <= max.Y; ++cell.Y)
							yield return cell;
					yield break;
			}
		}

	}
}
