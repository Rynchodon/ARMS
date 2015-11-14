using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinder
{
	public class RotateChecker
	{

		private readonly Logger m_logger;
		private readonly IMyCubeGrid m_grid;
		private readonly GridCellCache m_cells;

		private List<MyEntity> m_obstructions = new List<MyEntity>();

		public RotateChecker(IMyCubeGrid grid)
		{
			this.m_logger = new Logger("RotateChecker", () => m_grid.DisplayName);
			this.m_grid = grid;
			this.m_cells = GridCellCache.GetCellCache(grid);
		}

		/// <summary>
		/// Test if it is safe for the grid to rotate.
		/// </summary>
		/// <param name="axis">Normalized axis of rotation in world space.</param>
		/// <param name="ignoreAsteroids"></param>
		/// <returns>True iff the path is clear.</returns>
		public bool TestRotate(Vector3 axis, bool ignoreAsteroids)
		{
			Vector3 centreOfMass = m_grid.Physics.CenterOfMassWorld;
			float longestDim = m_grid.GetLongestDim();

			// calculate height
			Matrix toMyLocal = m_grid.WorldMatrixNormalizedInv;
			Vector3 myLocalCoM = Vector3.Transform(centreOfMass, toMyLocal);
			Vector3 myLocalAxis = Vector3.Transform(axis, toMyLocal.GetOrientation());
			Vector3 myLocalCentre = m_grid.LocalAABB.Center; // CoM may not be on ship (it now considers mass from attached grids)
			Ray upper = new Ray(myLocalCentre + myLocalAxis * longestDim * 2f, -myLocalAxis);
			float? upperBound = m_grid.LocalAABB.Intersects(upper);
			if (!upperBound.HasValue)
				m_logger.alwaysLog("Math fail, upperBound does not have a value", "TestRotate()", Logger.severity.FATAL);
			Ray lower = new Ray(myLocalCentre - myLocalAxis * longestDim * 2f, myLocalAxis);
			float? lowerBound = m_grid.LocalAABB.Intersects(lower);
			if (!lowerBound.HasValue)
				m_logger.alwaysLog("Math fail, lowerBound does not have a value", "TestRotate()", Logger.severity.FATAL);
			m_logger.debugLog("LocalAABB: " + m_grid.LocalAABB + ", centre: " + myLocalCentre + ", axis: " + myLocalAxis + ", longest dimension: " + longestDim + ", upper ray: " + upper + ", lower ray: " + lower, "TestRotate()");
			float height = longestDim * 4f - upperBound.Value - lowerBound.Value;

			float furthest = 0f;
			m_cells.ForEach(cell => {
				Vector3 rejection = Vector3.Reject(cell * m_grid.GridSize, myLocalAxis);
				float cellDistSquared = Vector3.DistanceSquared(myLocalCoM, rejection);
				if (cellDistSquared > furthest)
					furthest = cellDistSquared;
			});
			float length = (float)Math.Sqrt(furthest) + m_grid.GridSize * 0.5f;

			m_logger.debugLog("height: " + height + ", length: " + length, "TestRotate()");

			BoundingSphereD surroundingSphere = new BoundingSphereD(centreOfMass, Math.Max(length, height));
			m_obstructions.Clear();
			MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref surroundingSphere, m_obstructions);

			LineSegment axisSegment = new LineSegment();

			foreach (MyEntity entity in m_obstructions)
				if (PathChecker.collect_Entity(m_grid, entity))
				{
					if (entity is IMyVoxelBase)
					{
						if (ignoreAsteroids)
							continue;

						IMyVoxelMap voxel = entity as IMyVoxelMap;
						if (voxel != null)
						{
							if (voxel.GetIntersectionWithSphere(ref surroundingSphere))
								return false;
							continue;
						}
						MyPlanet planet = entity as MyPlanet;
						if (planet != null)
						{
							Vector3D centreD = surroundingSphere.Center;
							Vector3D closestPoint = planet.GetClosestSurfacePointGlobal(ref centreD);
							double minDistance = surroundingSphere.Radius; minDistance *= minDistance;
							if (Vector3D.DistanceSquared(centreD, closestPoint) <= minDistance)
							{
								m_logger.debugLog("Too close to " + planet.getBestName(), "TestRotate()");
								return false;
							}
						}
						continue;
					}

					IMyCubeGrid grid = entity as IMyCubeGrid;
					if (grid != null)
					{
						Matrix toLocal = grid.WorldMatrixNormalizedInv;
						Vector3 localAxis = Vector3.Transform(axis, toLocal.GetOrientation());
						Vector3 localCentre = Vector3.Transform(centreOfMass, toLocal);
						axisSegment.From = localCentre - localAxis * height;
						axisSegment.To = localCentre + localAxis * height;

						bool found = false;
						GridCellCache.GetCellCache(grid).ForEach(cell => {
							if (axisSegment.PointInCylinder(length, cell * grid.GridSize))
							{
								found = true;
								return true;
							}
							return false;
						});

						if (found)
							return false;
						continue;
					}

					m_logger.debugLog("No tests for object: " + entity.getBestName(), "TestRotate()", Logger.severity.INFO);
					return false;
				}

			return true;
		}

	}
}
