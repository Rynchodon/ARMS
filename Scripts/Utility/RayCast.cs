using System.Collections.Generic;
using System.Linq;
using Rynchodon.Utility;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon
{
	public static class RayCast
	{

		public const bool DefaultCheckPlanet = false;
		private const int FilterLayerVoxel = 28;

		private static Logger m_logger = new Logger();
		private static FastResourceLock m_rayCastLock = new FastResourceLock();

		/// <summary>
		/// <para>Test line segment between startPosition and targetPosition for obstructing entities.</para>
		/// <para>Tests for obstructing voxel map, character, or grid.</para>
		///// <param name="shortTest">When checking voxels, shortens the line by 1 m, needed to interact with an entity that may be on the surface of the voxel.</param>
		/// </summary>
		public static bool Obstructed<Tignore>(LineD line, IEnumerable<Tignore> ignoreList, bool checkVoxel = true, bool checkPlanet = DefaultCheckPlanet) where Tignore : IMyEntity
		{
			List<MyLineSegmentOverlapResult<MyEntity>> potentialObstruction = ResourcePool<List<MyLineSegmentOverlapResult<MyEntity>>>.Get();
			MyGamePruningStructure.GetAllEntitiesInRay(ref line, potentialObstruction);
			bool result = Obstructed(line, potentialObstruction.Select(overlap => overlap.Element), ignoreList, checkVoxel, checkPlanet);
			potentialObstruction.Clear();
			ResourcePool<List<MyLineSegmentOverlapResult<MyEntity>>>.Return(potentialObstruction);
			return result;
		}

		/// <summary>
		/// <para>Test line segment between startPosition and targetPosition for obstructing entities.</para>
		/// <para>Tests for obstructing voxel map, character, or grid.</para>
		///// <param name="shortTest">When checking voxels, shortens the line by 1 m, needed to interact with an entity that may be on the surface of the voxel.</param>
		/// </summary>
		public static bool Obstructed<Tobstruct, Tignore>(LineD line, IEnumerable<Tobstruct> potentialObstructions, IEnumerable<Tignore> ignoreList, bool checkVoxel = true, bool checkPlanet = DefaultCheckPlanet)//, bool shortTest = true)
			where Tobstruct : IMyEntity
			where Tignore : IMyEntity
		{
			Profiler.StartProfileBlock();
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
					if (entity.WorldAABB.Intersects(ref line, out distance))
					{
						m_logger.debugLog("obstructed by character: " + entity.getBestName());
						Profiler.EndProfileBlock();
						return true;
					}
					continue;
				}

				IMyCubeGrid asGrid = entity as IMyCubeGrid;
				if (asGrid != null)
				{
					if (!asGrid.Save)
						continue;

					ICollection<Vector3I> allHitCells;

					List<Vector3I> hitCells = new List<Vector3I>();
					asGrid.RayCastCells(line.From, line.To, hitCells);

					allHitCells = hitCells;

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
										m_logger.debugLog("disjoint: " + part.Key + ", LocalAABB: " + part.Value.PositionComp.LocalAABB + ", position: " + positionPart);
									}
									else
									{
										m_logger.debugLog("contained: " + part.Key + ", LocalAABB: " + part.Value.PositionComp.LocalAABB + ", position: " + positionPart);
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
									m_logger.debugLog("disjoint: " + slim.FatBlock.DisplayNameText + ", LocalAABB: " + slim.FatBlock.LocalAABB + ", position: " + positionBlock);
									continue;
								}
								else
									m_logger.debugLog("contained: " + slim.FatBlock.DisplayNameText + ", LocalAABB: " + slim.FatBlock.LocalAABB + ", position: " + positionBlock);
							}

						}

						m_logger.debugLog("obstructed by block: " + slim.getBestName() + " on " + slim.CubeGrid.DisplayName + ", id: " + slim.CubeGrid.EntityId);
						Profiler.EndProfileBlock();
						return true;
					}
				}
			}

			if (checkVoxel)
			{
				// Voxel Test
				MyVoxelBase hitVoxel;
				Vector3D hitPosition;
				if (RayCastVoxels(line, out hitVoxel, out hitPosition, checkPlanet))
				{
					m_logger.debugLog("obstructed by voxel: " + hitVoxel + " at " + hitPosition);
					Profiler.EndProfileBlock();
					return true;
				}
			}

			// no obstruction found
			Profiler.EndProfileBlock();
			return false;
		}

		/// <summary>
		/// Ray cast all the voxels in the world to check for intersection.
		/// </summary>
		/// <param name="line">The line to check</param>
		///// <param name="shortTest">Shortens the line by 5 m, needed to interact with an entity that may be on the surface of the voxel.</param>
		/// <returns>True iff any voxel intersects the line</returns>
		public static bool RayCastVoxels(LineD line, out MyVoxelBase hitVoxel, out Vector3D hitPosition, bool checkPlanet = DefaultCheckPlanet)
		{
			//const double ShortenBy = 5d;
#if PROFILE
			Profiler.StartProfileBlock();
			try
			{
#endif
			CapsuleD capsule;
			capsule.P0 = line.From;
			capsule.P1 = line.To;
			capsule.Radius = 0f;
			return CapsuleDExtensions.IntersectsVoxel(ref capsule, out hitVoxel, out hitPosition, checkPlanet);
#if PROFILE
			}
			finally { Profiler.EndProfileBlock(); }
#endif
		}

		public static bool RayCastVoxels(ref Vector3D start, ref Vector3D end, out MyVoxelBase hitVoxel, out Vector3D hitPosition, bool checkPlanet = DefaultCheckPlanet)
		{
#if PROFILE
			Profiler.StartProfileBlock();
			try
			{
#endif
			CapsuleD capsule;
			capsule.P0 = start;
			capsule.P1 = end;
			capsule.Radius = 0f;

			return CapsuleDExtensions.IntersectsVoxel(ref capsule, out hitVoxel, out hitPosition, checkPlanet);
#if PROFILE
			}
			finally { Profiler.EndProfileBlock(); }
#endif
		}

	}
}
