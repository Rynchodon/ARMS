using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRageMath;

namespace Rynchodon
{
	/// <summary>
	/// Performs ray-casts for obstructions, assess obstructions, determines Vector required to hit target.
	/// </summary>
	public class TargetTracker
	{
		/// <summary>
		/// <para>Test for entities intersecting a line segment.</para>
		/// </summary>
		/// <param name="world">Line to test for entities</param>
		/// <param name="pointOfIntersection">the point of contact between Line and intersecting entity</param>
		/// <param name="collect">Ignore entity if function returns false. Not tested against IMyCubeBlock</param>
		/// <param name="collect_CubeBlock">If null, do not ignore any IMyCubeBlock. Ignore IMyCubeBlock if function returns false</param>
		/// <param name="sort">entities will be sorted according to results of this function</param>
		/// <returns>An entity intersecting the Line</returns>
		/// <remarks>
		/// <para>For IMyCubeGrid, if a cell the line passes through is occupied, the cell is considered to be the point of intersection</para>
		/// <para>For entities that are not IMyCubeGrid, Line will be tested against IMyEntity.WorldVolume</para>
		/// </remarks>
		public static IMyEntity EntityOnLine(LineD world, out Vector3D? pointOfIntersection, Func<IMyEntity, bool> collect, Func<IMyCubeBlock, bool> collect_CubeBlock = null, Func<IMyEntity, double> sort = null)
		{
			if (collect == null)
				throw new ArgumentNullException("collect");

			// Get entities in AABB
			Vector3D[] points = { world.From, world.To };
			BoundingBoxD AABB = BoundingBoxD.CreateFromPoints(points);
			ICollection<IMyEntity> entitiesInAABB = MyAPIGateway.Entities.GetEntitiesInAABB_Safe(ref AABB);

			RayD worldRay = new RayD(world.From, world.Direction);

			if (sort != null)
			{
				SortedDictionary<double, IMyEntity> sortedEntities = new SortedDictionary<double, IMyEntity>();
				foreach (IMyEntity entity in entitiesInAABB)
				{
					// filter entities
					if (entity is IMyCubeBlock || !collect(entity))
						continue;

					sortedEntities.Add(sort(entity), entity);
				}
				entitiesInAABB = sortedEntities.Values;
			}

			foreach (IMyEntity entity in entitiesInAABB)
			{
				if (sort == null)
				{
					// filter entities
					if (entity is IMyCubeBlock || !collect(entity))
						continue;
				}

				// ray cast
				IMyCubeGrid entityAsGrid = entity as IMyCubeGrid;
				if (entityAsGrid != null)
				{
					// TODO: test GetLineIntersectionExact...

					List<Vector3I> rayCastCells = new List<Vector3I>();
					entityAsGrid.RayCastCells(world.From, world.To, rayCastCells); // I do not know if rayCastCells will be sorted

					foreach (Vector3I cell in rayCastCells)
					{
						IMySlimBlock block = entityAsGrid.GetCubeBlock(cell);
						if (block == null)
							continue;
						IMyCubeBlock FatBlock = block.FatBlock;
						if (FatBlock == null)
						{
							pointOfIntersection = entityAsGrid.GridIntegerToWorld(cell);
							return entityAsGrid;
						}
						else
							if (collect_CubeBlock == null || collect_CubeBlock(FatBlock))
							{
								pointOfIntersection = entityAsGrid.GridIntegerToWorld(cell);
								return entityAsGrid;
							}
					}
				}
				else
				{
					double tMin, tMax;
					if (entity.WorldVolume.IntersectRaySphere(worldRay, out tMin, out tMax))
					{
						pointOfIntersection = tMin * worldRay.Direction + worldRay.Position;
						return entity;
					}
				}
			}

			pointOfIntersection = null;
			return null;
		}
	}
}
