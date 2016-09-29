using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinder
{
	public class RotateChecker
	{

		private readonly Logger m_logger;
		private readonly IMyCubeGrid m_grid;
		private readonly GridCellCache m_cells;

		private List<MyEntity> m_obstructions = new List<MyEntity>();
		private Vector3D m_closestPoint;
		private FastResourceLock lock_closestPoint = new FastResourceLock();

		public Pathfinder.PathState PlanetState { get; private set; }
		public MyPlanet ClosestPlanet { get; private set; }
		public Vector3D ClosestPoint
		{
			get
			{
				using (lock_closestPoint.AcquireSharedUsing())
					return m_closestPoint;
			}
		}

		public RotateChecker(IMyCubeGrid grid)
		{
			this.m_logger = new Logger(() => m_grid.DisplayName);
			this.m_grid = grid;
			this.m_cells = GridCellCache.GetCellCache(grid);
		}

		/// <summary>
		/// Test if it is safe for the grid to rotate.
		/// </summary>
		/// <param name="axis">Normalized axis of rotation in world space.</param>
		/// <param name="ignoreAsteroids"></param>
		/// <returns>True iff the path is clear.</returns>
		public bool TestRotate(Vector3 axis, bool ignoreAsteroids, out IMyEntity obstruction)
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
				m_logger.alwaysLog("Math fail, upperBound does not have a value", Logger.severity.FATAL);
			Ray lower = new Ray(myLocalCentre - myLocalAxis * longestDim * 2f, myLocalAxis);
			float? lowerBound = m_grid.LocalAABB.Intersects(lower);
			if (!lowerBound.HasValue)
				m_logger.alwaysLog("Math fail, lowerBound does not have a value", Logger.severity.FATAL);
			m_logger.debugLog("LocalAABB: " + m_grid.LocalAABB + ", centre: " + myLocalCentre + ", axis: " + myLocalAxis + ", longest dimension: " + longestDim + ", upper ray: " + upper + ", lower ray: " + lower);
			float height = longestDim * 4f - upperBound.Value - lowerBound.Value;

			float furthest = 0f;
			m_cells.ForEach(cell => {
				Vector3 rejection = Vector3.Reject(cell * m_grid.GridSize, myLocalAxis);
				float cellDistSquared = Vector3.DistanceSquared(myLocalCoM, rejection);
				if (cellDistSquared > furthest)
					furthest = cellDistSquared;
			});
			float length = (float)Math.Sqrt(furthest) + m_grid.GridSize * 0.5f;

			m_logger.debugLog("height: " + height + ", length: " + length);

			BoundingSphereD surroundingSphere = new BoundingSphereD(centreOfMass, Math.Max(length, height));
			m_obstructions.Clear();
			MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref surroundingSphere, m_obstructions);

			LineSegment axisSegment = new LineSegment();

			ClosestPlanet = MyPlanetExtensions.GetClosestPlanet(centreOfMass);
			MyAPIGateway.Utilities.TryInvokeOnGameThread(TestPlanet);

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
							{
								m_logger.debugLog("Too close to " + voxel.getBestName() + ", CoM: " + centreOfMass.ToGpsTag("Centre of Mass") + ", required distance: " + surroundingSphere.Radius);
								obstruction = voxel;
								return false;
							}
							continue;
						}

						if (PlanetState != Pathfinder.PathState.No_Obstruction)
						{
							m_logger.debugLog("planet blocking");
							obstruction = ClosestPlanet;
							return false;
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
						{
							obstruction = grid;
							return false;
						}
						continue;
					}

					m_logger.debugLog("No tests for object: " + entity.getBestName(), Logger.severity.INFO);
					obstruction = entity;
					return false;
				}

			obstruction = null;
			return true;
		}

		private void TestPlanet()
		{
			MyPlanet planet = ClosestPlanet;
			if (planet == null)
				return;

			Vector3D myPos = m_grid.GetCentre();
			Vector3D planetCentre = planet.GetCentre();

			double distSqToPlanet = Vector3D.DistanceSquared(myPos, planetCentre);
			if (distSqToPlanet > planet.MaximumRadius * planet.MaximumRadius)
			{
				m_logger.debugLog("higher than planet maximum");
				PlanetState = Pathfinder.PathState.No_Obstruction;
				return;
			}

			using (lock_closestPoint.AcquireExclusiveUsing())
				m_closestPoint = planet.GetClosestSurfacePointGlobal(ref myPos);

			if (distSqToPlanet < Vector3D.DistanceSquared(m_closestPoint, planetCentre))
			{
				m_logger.debugLog("below surface");
				PlanetState = Pathfinder.PathState.Path_Blocked;
				return;
			}

			float longest = m_grid.GetLongestDim();
			if (Vector3D.DistanceSquared(myPos, m_closestPoint) < longest * longest)
			{
				m_logger.debugLog("near surface");
				PlanetState = Pathfinder.PathState.Path_Blocked;
				return;
			}

			m_logger.debugLog("clear");
			PlanetState = Pathfinder.PathState.No_Obstruction;
			return;
		}

	}
}
