using System;
using System.Collections.Generic;
using System.Linq;
using Rynchodon.Attached;
using Rynchodon.Autopilot.Data;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinder
{
	/// <summary>
	/// Checks paths for obstructing entities
	/// </summary>
	internal class PathChecker
	{
		private enum Stage : byte { None, Inititialized, Finished_Fast, Finished }

		//public bool Interrupt = false;

		private readonly Logger m_logger = new Logger(null, "PathChecker");
		private readonly IMyCubeGrid m_grid;

		private MyEntity m_ignoreEntity;
		private bool m_ignoreAsteroid;

		private GridShapeProfiler m_profiler;
		private IEnumerable<MyEntity> m_offendingEntities;
		private readonly List<MyEntity> m_offenders = new List<MyEntity>();
		private readonly List<MyEntity> m_offRemove = new List<MyEntity>();

		private Capsule m_path { get { return m_profiler.Path; } }

		public PathChecker(IMyCubeGrid grid)
		{
			this.m_logger = new Logger("PathChecker", () => m_grid.DisplayName);
			this.m_grid = grid;
			this.m_profiler = new GridShapeProfiler();
		}

		//private void CheckInterrupt()
		//{
		//	if (Interrupt)
		//		throw new InterruptException();
		//}

		/// <summary>
		/// Performs a fast test to see if the path is clear.
		/// </summary>
		/// <returns>True if path is clear. False if slow test needs to be run.</returns>
		public bool TestFast(PseudoBlock NavigationBlock, Vector3D worldDestination, bool ignoreAsteroid, MyEntity ignoreEntity, bool landing)
		{
			m_logger.debugLog(NavigationBlock.Grid != m_grid, "NavigationBlock.CubeGrid != m_grid", "TestFast()", Logger.severity.FATAL);

			//CheckInterrupt();

			this.m_ignoreEntity = ignoreEntity;
			this.m_ignoreAsteroid = ignoreAsteroid;
			this.m_profiler.Init(m_grid, RelativePosition3F.FromWorld(m_grid, worldDestination), NavigationBlock.LocalPosition, landing);

			Vector3D Displacement = worldDestination - NavigationBlock.WorldPosition;
			//m_logger.debugLog("Displacement = " + Displacement, "TestPath()");

			// entities in large AABB
			BoundingBoxD AtDest = m_grid.WorldAABB.Translate(Displacement);
			ICollection<MyEntity> offenders = EntitiesInLargeAABB(m_grid.WorldAABB, AtDest);
			if (offenders.Count == 0)
			{
				m_logger.debugLog("AABB is empty", "TestPath()", Logger.severity.DEBUG);
				return true;
			}
			m_logger.debugLog("collected entities to test: " + offenders.Count, "TestPath()");

			// perform capsule vs AABB test
			List<MyEntity> remove = new List<MyEntity>();
			foreach (MyEntity offending in offenders)
				if (!m_path.IntersectsAABB(offending))
				{
					m_logger.debugLog("no AABB intersection: " + offending.getBestName(), "TestEntities()");
					remove.Add(offending);
				}
			foreach (MyEntity entity in remove)
				offenders.Remove(entity);

			if (offenders.Count == 0)
			{
				m_logger.debugLog("no entities intersect path", "TestFast()", Logger.severity.DEBUG);
				return true;
			}
			m_logger.debugLog("entities intersecting path: " + offenders.Count, "TestPath()");

			foreach (var ent in offenders)
				m_logger.debugLog("entity: " + ent.getBestName(), "TestPath()");

			m_offendingEntities = offenders.OrderBy(entity => m_grid.WorldAABB.Distance(entity.PositionComp.WorldAABB));

			return false;
		}

		public bool TestSlow(out MyEntity blockingPath, out IMyCubeGrid blockingGrid, out Vector3? pointOfObstruction)
		{
			m_logger.debugLog(m_offendingEntities == null, "m_offendingEntities == null, did you remember to call TestFast()?", "TestSlow()", Logger.severity.FATAL);

			IMyCubeBlock ignoreBlock = m_ignoreEntity as IMyCubeBlock;

			foreach (MyEntity entity in m_offendingEntities)
			{
				//CheckInterrupt();

				IMyVoxelBase voxel = entity as IMyVoxelBase;
				if (voxel != null)
				{
					if (m_ignoreAsteroid)
					{
						m_logger.debugLog("Ignoring asteroid: " + voxel.getBestName(), "TestEntities()");
						continue;
					}

					Vector3[] intersection = new Vector3[2];
					if (!m_path.IntersectsAABB(voxel, out  intersection[0]))
					{
						m_logger.debugLog("path does not intersect AABB. " + voxel.getBestName(), "TestEntities()", Logger.severity.DEBUG);
						continue;
					}

					if (!m_path.get_Reverse().IntersectsAABB(voxel, out intersection[1]))
					{
						m_logger.debugLog("Reversed path does not intersect AABB, perhaps it moved? " + voxel.getBestName(), "TestEntities()", Logger.severity.WARNING);
						continue;
					}

					Capsule testSection = new Capsule(intersection[0], intersection[1], m_path.Radius);
					IMyVoxelMap asteroid = voxel as IMyVoxelMap;
					if (asteroid != null)
					{
						if (testSection.Intersects(asteroid, out pointOfObstruction))
						{
							blockingPath = entity;
							blockingGrid = null;
							return false;
						}
					}
					else
					{
						MyPlanet planet = voxel as MyPlanet;
						if (planet != null && testSection.Intersects(planet, out pointOfObstruction))
						{
							blockingPath = entity;
							blockingGrid = null;
							return false;
						}
					}

					m_logger.debugLog("Does not intersect path: " + voxel.getBestName(), "TestEntities()", Logger.severity.DEBUG);
					continue;
				}

				IMyCubeGrid grid = entity as IMyCubeGrid;
				if (grid != null)
				{
					if (m_profiler.rejectionIntersects(grid, ignoreBlock, out blockingPath, out pointOfObstruction))
					{
						blockingGrid = grid;
						return false;
					}
					continue;
				}

				m_logger.debugLog("not a grid, testing bounds", "TestEntities()");
				if (!m_path.IntersectsAABB(entity))
					continue;

				if (!m_path.IntersectsVolume(entity))
					continue;

				m_logger.debugLog("no more tests for non-grids are implemented", "TestEntities()", Logger.severity.DEBUG);
				pointOfObstruction = m_path.get_Line().ClosestPoint(entity.GetCentre());
				blockingPath = entity;
				blockingGrid = null;
				return false;
			}

			blockingPath = null;
			blockingGrid = null;
			pointOfObstruction = null;
			return true;
		}

		#region Do not delete

		///// <summary>
		///// How far long the line would the ship be able to travel? Uses a capsule derived from previously calculated path.
		///// </summary>
		///// <param name="canTravel">Line along which navigation block would travel</param>
		///// <remarks>
		///// Capsule only test because the ship will not be oriented correctly
		///// </remarks>
		///// <returns>distance from the destination that can be reached</returns>
		//public float distanceCanTravel(Line canTravel)
		//{
		//	Vector3D navBlockPos = navigationBlock.GetPosition();

		//	Vector3D DisplacementStart = canTravel.From - navBlockPos;
		//	Vector3D DisplacementEnd = canTravel.To - navBlockPos;

		//	BoundingBoxD atStart = myCubeGrid.WorldAABB.Translate(DisplacementStart);
		//	BoundingBoxD atDest = myCubeGrid.WorldAABB.Translate(DisplacementEnd);

		//	ICollection<MyEntity> offenders = EntitiesInLargeAABB(atStart, atDest);
		//	if (offenders.Count == 0)
		//	{
		//		myLogger.debugLog("AABB is empty", "distanceCanTravel()");
		//		return 0;
		//	}
		//	myLogger.debugLog("collected entities to test: " + offenders.Count, "distanceCanTravel()");
		//	offenders = SortByDistance(offenders);

		//	Capsule _path = new Capsule(canTravel.From, canTravel.To, myPath.Radius);
		//	Vector3? pointOfObstruction;
		//	MyEntity obstruction = TestEntities(offenders, _path, null, out pointOfObstruction, DestGrid);
		//	if (obstruction == null)
		//	{
		//		myLogger.debugLog("no obstruction", "distanceCanTravel()");
		//		return 0;
		//	}

		//	myLogger.debugLog("obstruction at " + pointOfObstruction + " distance from dest is " + Vector3.Distance(canTravel.To, (Vector3)pointOfObstruction), "distanceCanTravel()");
		//	return Vector3.Distance(canTravel.To, (Vector3)pointOfObstruction);
		//}

		//public const double NearbyRange = 2000;

		///// <summary>
		///// How far away is the closest entity to myCubeGrid?
		///// </summary>
		///// <returns>Distance to closest Entity, may be negative</returns>
		//public double ClosestEntity()
		//{
		//	BoundingBoxD NearbyBox = myCubeGrid.WorldAABB;
		//	NearbyBox.Inflate(NearbyRange);

		//	HashSet<MyEntity> offenders = new HashSet<MyEntity>();
		//	MyAPIGateway.Entities.GetEntitiesInAABB_Safe_NoBlock(NearbyBox, offenders, collect_Entities);
		//	if (offenders.Count == 0)
		//	{
		//		myLogger.debugLog("AABB is empty", "ClosestEntity()");
		//		return NearbyRange;
		//	}
		//	myLogger.debugLog("collected entities to test: " + offenders.Count, "ClosestEntity()");

		//	double distClosest = NearbyRange;
		//	foreach (MyEntity entity in offenders)
		//	{
		//		double distance = myCubeGrid.Distance_ShorterBounds(entity);
		//		if (distance < distClosest)
		//			distClosest = distance;
		//	}
		//	return distClosest;
		//}

		#endregion

		#region Private Test Functions

		private List<MyEntity> EntitiesInLargeAABB(BoundingBoxD start, BoundingBoxD end)
		{
			Vector3D[] pathPoints = new Vector3D[4];
			pathPoints[0] = start.Min;
			pathPoints[1] = start.Max;
			pathPoints[2] = end.Min;
			pathPoints[3] = end.Max;
			BoundingBoxD PathAABB = BoundingBoxD.CreateFromPoints(pathPoints);
			m_logger.debugLog("Path AABB = " + PathAABB, "EntitiesInLargeAABB()");

			m_offenders.Clear();
			MyGamePruningStructure.GetAllTopMostEntitiesInBox(ref PathAABB, m_offenders);
			m_offRemove.Clear();
			for (int i = 0; i < m_offenders.Count; i++)
				if (!collect_Entity(m_grid, m_offenders[i])
					|| (m_ignoreEntity != null && m_ignoreEntity == m_offenders[i]))
				{
					m_logger.debugLog("discarding: " + m_offenders[i].getBestName(), "EntitiesInLargeAABB()");
					m_offRemove.Add(m_offenders[i]);
				}
			for (int i = 0; i < m_offRemove.Count; i++)
				m_offenders.Remove(m_offRemove[i]);

			return m_offenders;
		}

		/// <returns>true iff the entity should be kept</returns>
		public static bool collect_Entity(IMyCubeGrid myGrid, MyEntity entity)
		{
			if (!(entity is IMyCubeGrid) && !(entity is IMyVoxelBase) && !(entity is IMyFloatingObject))
				return false;

			IMyCubeGrid asGrid = entity as IMyCubeGrid;
			if (asGrid != null)
			{
				if (asGrid == myGrid)
					return false;

				if (!asGrid.Save)
					return false;

				if (AttachedGrid.IsGridAttached(myGrid, asGrid, AttachedGrid.AttachmentKind.Physics))
					return false;
			}

			if (entity.Physics != null && entity.Physics.Mass > 0 && entity.Physics.Mass < 1000)
				return false;

			return true;
		}

		#endregion
		
	}
}
