using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon
{
	public static class RayCast
	{

		private static Logger myLogger = new Logger(null, "RayCast");

		/// <summary>
		/// <para>Test line segment between startPosition and targetPosition for obstructing entities.</para>
		/// <para>Tests for obstructing voxel map, character, or grid.</para>
		/// </summary>
		public static bool Obstructed(List<Line> lines, ICollection<IMyEntity> potentialObstructions, ICollection<IMyEntity> ignoreList, out object obstruction, bool checkVoxel = true)
		{
			// Voxel Test
			if (checkVoxel)
			{
				Vector3 boundary;
				foreach (Line testLine in lines)
					if (MyAPIGateway.Entities.RayCastVoxel_Safe(testLine.From, testLine.To, out boundary))
					{
						obstruction = boundary;
						return true;
					}
			}

			// Test each entity
			foreach (IMyEntity entity in potentialObstructions)
			{
				if (entity.Closed)
					continue;

				if (ignoreList != null && ignoreList.Contains(entity))
					continue;

				IMyCharacter asChar = entity as IMyCharacter;
				if (asChar != null)
				{
					double distance;
					foreach (Line testLine in lines)
						if (entity.WorldAABB.Intersects(new LineD(testLine.From, testLine.To), out distance))
						{
							obstruction = asChar;
							return true;
						}
					continue;
				}

				IMyCubeGrid asGrid = entity as IMyCubeGrid;
				if (asGrid != null)
				{
					ICollection<Vector3I> allHitCells;

					if (lines.Count == 1)
					{
						List<Vector3I> hitCells = new List<Vector3I>();
						asGrid.RayCastCells(lines[0].From, lines[0].To, hitCells);

						allHitCells = hitCells;
					}
					else
					{
						allHitCells = new HashSet<Vector3I>();
						foreach (Line testLine in lines)
						{
							List<Vector3I> hitCells = new List<Vector3I>();
							asGrid.RayCastCells(testLine.From, testLine.To, hitCells);

							foreach (Vector3I cell in hitCells)
								allHitCells.Add(cell);
						}
					}

					foreach (Vector3I pos in allHitCells)
					{
						IMySlimBlock slim = asGrid.GetCubeBlock(pos);
						if (slim == null)
							continue;

						if (ignoreList != null && slim.FatBlock != null && ignoreList.Contains(slim.FatBlock))
							continue;

						if (slim.FatBlock != null)
							obstruction = slim.FatBlock;
						else
							obstruction = asGrid;
						return true;
					}
				}
			}

			// no obstruction found
			obstruction = null;
			return false;
		}

	}
}
