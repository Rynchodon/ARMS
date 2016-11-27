using System;
using System.Collections;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon
{
	public static class IMyCubeGridExtensions
	{

		/// <summary>
		/// Gets the simple name for a grid, "Station", "Large Ship", or "Small Ship".
		/// </summary>
		public static string SimpleName(this IMyCubeGrid grid)
		{
			if (grid.GridSizeEnum == MyCubeSize.Large)
				if (grid.IsStatic)
					return "Station";
				else
					return "Large Ship";
			else
				return "Small Ship";
		}

		/// <summary>
		/// Yields the first occupied cells encountered when raycasting a grid in a given base direction.
		/// </summary>
		/// <param name="grid">The grid to get blocks from.</param>
		/// <param name="baseDirection">The direction of ray.</param>
		public static IEnumerable<Vector3I> FirstBlocks(this IMyCubeGrid grid, Vector3 baseDirection)
		{
			return FirstBlocks(grid, Base6Directions.GetIntVector(Base6Directions.GetDirection(baseDirection)));
		}

		/// <summary>
		/// Yields the first occupied cells encountered when raycasting a grid in a given base direction.
		/// </summary>
		/// <param name="grid">The grid to get blocks from.</param>
		/// <param name="baseDirection">The direction of ray.</param>
		public static IEnumerable<Vector3I> FirstBlocks(this IMyCubeGrid grid, Vector3I baseDirection)
		{
			Logger.DebugLog("baseDirection(" + baseDirection + ") has a magnitude", Logger.severity.FATAL, condition: baseDirection.RectangularLength() != 1);

			BoundingBox localAABB = grid.LocalAABB;

			Vector3 minF; Vector3.Divide(ref localAABB.Min, grid.GridSize, out minF);
			Vector3 maxF; Vector3.Divide(ref localAABB.Max, grid.GridSize, out maxF);
			Vector3I min, max;

			Func<float, int> round = f => (int)Math.Round(f);
			minF.ApplyOperation(round, out min);
			maxF.ApplyOperation(round, out max);

			Vector3I perp0, perp1;
			perp0 = Base6Directions.GetIntVector(Base6Directions.GetPerpendicular(Base6Directions.GetDirection(baseDirection)));
			Vector3I.Cross(ref baseDirection, ref perp0, out perp1);

			int baseStart; Vector3I.Dot(ref baseDirection, ref min, out baseStart);
			int baseEnd; Vector3I.Dot(ref baseDirection, ref max, out baseEnd);
			bool incrementBase = baseStart <= baseEnd;

			int perp0Min; Vector3I.Dot(ref perp0, ref min, out perp0Min);
			int perp0Max; Vector3I.Dot(ref perp0, ref max, out perp0Max);
			if (perp0Max < perp0Min)
			{
				int temp = perp0Max;
				perp0Max = perp0Min;
				perp0Min = temp;
			}

			int perp1Min; Vector3I.Dot(ref perp1, ref min, out perp1Min);
			int perp1Max; Vector3I.Dot(ref perp1, ref max, out perp1Max);
			if (perp1Max < perp1Min)
			{
				int temp = perp1Max;
				perp1Max = perp1Min;
				perp1Min = temp;
			}

			Logger.DebugLog("min: " + min + ", max: " + max);
			Logger.DebugLog("base: " + baseDirection + ", perp0: " + perp0 + ", perp1: " + perp1);
			Logger.DebugLog("base range: " + baseStart + ":" + baseEnd);
			Logger.DebugLog("perp0 range: " + perp0Min + ":" + perp0Max);
			Logger.DebugLog("perp1 range: " + perp1Min + ":" + perp1Max);

			for (int perp0Value = perp0Min; perp0Value <= perp0Max; perp0Value++)
				for (int perp1Value = perp1Min; perp1Value <= perp1Max; perp1Value++)
				{
					int baseValue = baseStart;
					while (true)
					{
						Vector3I cell = baseValue * baseDirection + perp0Value * perp0 + perp1Value * perp1;

						if (grid.CubeExists(cell))
						{
							yield return cell;
							break;
						}

						if (baseValue == baseEnd)
							break;
						if (incrementBase)
							baseValue++;
						else
							baseValue--;
					}
				}

			yield break;
		}

	}
}
