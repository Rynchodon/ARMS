using System.Collections.Generic;
using System.Linq;
using Rynchodon.Utility;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon
{
	public static class RayCast
	{

		private static Logger m_logger = new Logger("RayCast");

		/// <summary>
		/// <para>Test line segment between startPosition and targetPosition for obstructing entities.</para>
		/// <para>Tests for obstructing voxel map, character, or grid.</para>
		/// </summary>
		public static bool Obstructed<Tobstruct, Tignore>(LineD line, ICollection<Tobstruct> potentialObstructions, ICollection<Tignore> ignoreList, bool checkVoxel = true)
			where Tobstruct : IMyEntity
			where Tignore : IMyEntity
		{
			Profiler.StartProfileBlock("RayCast", "Obstructed");
			try
			{
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
							return true;
						}
					}
				}

				if (checkVoxel)
				{
					// Voxel Test
					MyVoxelBase contactVoxel;
					Vector3D? contactPoint;
					if (checkVoxel && RayCastVoxels(ref line, out contactVoxel, out contactPoint))
					{
						m_logger.debugLog("obstructed by voxel: " + contactVoxel + " at " + contactPoint);
						return true;
					}
				}

				// no obstruction found
				return false;
			}
			finally { Profiler.EndProfileBlock(); }
		}

		private static readonly FastResourceLock lock_rayCastVoxel = new FastResourceLock();

		/// <summary>
		/// Ray cast a particular voxel to check for intersection or get contact point.
		/// </summary>
		/// <param name="voxel">The voxel to ray cast</param>
		/// <param name="line">The line to check</param>
		/// <param name="contact">First intersection of line and voxel</param>
		/// <param name="useCollisionModel">Due to bug in SE, not used ATM</param>
		/// <param name="flags">Due to bug in SE, not used ATM</param>
		/// <returns>True iff the voxel intersects the line</returns>
		public static bool RayCastVoxel(MyVoxelBase voxel, ref LineD line, out Vector3D? contact, bool useCollisionModel = true, IntersectionFlags flags = IntersectionFlags.ALL_TRIANGLES)
		{
			using (MainLock.AcquireSharedUsing())
			{
				using (lock_rayCastVoxel.AcquireExclusiveUsing())
					return voxel.GetIntersectionWithLine(ref line, out contact, useCollisionModel, flags);
			}
		}

		/// <summary>
		/// Ray cast all the voxels in the world to check for intersection. Iterates voxels in no particular order and breaks on first contact.
		/// </summary>
		/// <param name="line">The line to check</param>
		/// <param name="contactVoxel">The voxel hit</param>
		/// <param name="contactPoint">First interesection of line and voxel</param>
		/// <param name="useCollisionModel">Due to bug in SE, not used ATM</param>
		/// <param name="flags">Due to bug in SE, not used ATM</param>
		/// <returns>True iff any voxel intersects the line</returns>
		public static bool RayCastVoxels(ref LineD line, out MyVoxelBase contactVoxel, out Vector3D? contactPoint, bool useCollisionModel = true, IntersectionFlags flags = IntersectionFlags.ALL_TRIANGLES)
		{
			List<MyLineSegmentOverlapResult<MyVoxelBase>> list = ResourcePool<List<MyLineSegmentOverlapResult<MyVoxelBase>>>.Get();
			try
			{
				MyGamePruningStructure.GetVoxelMapsOverlappingRay(ref line, list);
				list.OrderBy(item => item.Distance);

				using (MainLock.AcquireSharedUsing())
				{
					using (lock_rayCastVoxel.AcquireExclusiveUsing())
						foreach (var voxel in list)
							if (voxel.Element.GetIntersectionWithLine(ref line, out contactPoint, useCollisionModel, flags))
							{
								contactVoxel = voxel.Element;
								return true;
							}
				}

				contactVoxel = null;
				contactPoint = null;
				return false;
			}
			finally
			{
				list.Clear();
				ResourcePool<List<MyLineSegmentOverlapResult<MyVoxelBase>>>.Return(list);
			}
		}

	}
}
