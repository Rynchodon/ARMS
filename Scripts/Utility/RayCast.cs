using System.Collections.Generic;
using Rynchodon.Utility;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon
{
	/*
	 * TODO:
	 * write a method that uses MyGamePruningStructure.GetAllEntitiesInRay()
	 * write a method for raycasting voxels
	 */
	public static class RayCast
	{

		private static Logger myLogger = new Logger("RayCast");

		/// <summary>
		/// <para>Test line segment between startPosition and targetPosition for obstructing entities.</para>
		/// <para>Tests for obstructing voxel map, character, or grid.</para>
		/// </summary>
		public static bool Obstructed<Tobstruct, Tignore>(List<Line> lines, ICollection<Tobstruct> potentialObstructions, ICollection<Tignore> ignoreList, bool checkVoxel = true)
			where Tobstruct : IMyEntity
			where Tignore : IMyEntity
		{
			Profiler.StartProfileBlock("RayCast", "Obstructed");
			try
			{
				// Voxel Test
				if (checkVoxel)
				{
					Vector3 boundary;
					foreach (Line testLine in lines)
						if (MyAPIGateway.Entities.RayCastVoxel_Safe(testLine.From, testLine.To, out boundary))
							return true;
				}

				// Test each entity
				foreach (IMyEntity entity in potentialObstructions)
				{
					if (entity.Closed)
						continue;

					if (ignoreList != null && ignoreList.Contains((Tignore)entity))
						continue;

					IMyCharacter asChar = entity as IMyCharacter;
					if (asChar != null)
					{
						double distance;
						foreach (Line testLine in lines)
						{
							LineD l = (LineD)testLine;
							if (entity.WorldAABB.Intersects(ref l, out distance))
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

							if (ignoreList != null && slim.FatBlock != null && ignoreList.Contains((Tignore)slim.FatBlock))
								continue;

							if (slim.FatBlock != null)
							{
								Dictionary<string, MyEntitySubpart> subparts = ((MyCubeBlock)slim.FatBlock).Subparts;
								if (subparts != null && subparts.Count != 0)
								{
									bool subpartHit = false;
									foreach (var part in subparts)
									{
										Vector3 positionPart = Vector3.Transform(asGrid.GridIntegerToWorld(pos), part.Value.PositionComp.WorldMatrixNormalizedInv);

										if (slim.FatBlock.LocalAABB.Contains(positionPart) == ContainmentType.Disjoint)
										{
											myLogger.debugLog("disjoint: " + part.Key + ", LocalAABB: " + part.Value.PositionComp.LocalAABB + ", position: " + positionPart, "Obstructed()");
										}
										else
										{
											myLogger.debugLog("contained: " + part.Key + ", LocalAABB: " + part.Value.PositionComp.LocalAABB + ", position: " + positionPart, "Obstructed()");
											subpartHit = true;
											break;
										}
									}
									if (!subpartHit)
										continue;
								}

								// for piston base and stator, cell may not actually be inside local AABB
								// if this is done for doors, they would always be treated as open
								// other blocks have not been tested
								if ((slim.FatBlock is IMyMotorStator || slim.FatBlock is IMyPistonBase))
								{
									Vector3 positionBlock = Vector3.Transform(asGrid.GridIntegerToWorld(pos), slim.FatBlock.WorldMatrixNormalizedInv);

									if (slim.FatBlock.LocalAABB.Contains(positionBlock) == ContainmentType.Disjoint)
									{
										myLogger.debugLog("disjoint: " + slim.FatBlock.DisplayNameText + ", LocalAABB: " + slim.FatBlock.LocalAABB + ", position: " + positionBlock, "Obstructed()");
										continue;
									}
									else
										myLogger.debugLog("contained: " + slim.FatBlock.DisplayNameText + ", LocalAABB: " + slim.FatBlock.LocalAABB + ", position: " + positionBlock, "Obstructed()");
								}

							}
							return true;
						}
					}
				}

				// no obstruction found
				return false;
			}
			finally { Profiler.EndProfileBlock(); }
		}

	}
}
