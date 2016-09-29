using System.Collections.Generic;
using System.Linq;
using Rynchodon.Attached;
using Rynchodon.Autopilot.Data;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
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

		private readonly Logger m_logger = new Logger();
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
			this.m_logger = new Logger(() => m_grid.DisplayName);
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
			m_logger.debugLog("NavigationBlock.CubeGrid != m_grid", Logger.severity.FATAL, condition: NavigationBlock.Grid != m_grid);

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
				m_logger.debugLog("AABB is empty", Logger.severity.TRACE);
				return true;
			}
			m_logger.debugLog("collected entities to test: " + offenders.Count);

			// perform capsule vs AABB test
			List<MyEntity> remove = new List<MyEntity>();
			foreach (MyEntity offending in offenders)
				if (!m_path.IntersectsAABB(offending))
				{
					//m_logger.debugLog("no AABB intersection: " + offending.getBestName(), "TestPath()");
					remove.Add(offending);
				}
			foreach (MyEntity entity in remove)
				offenders.Remove(entity);

			if (offenders.Count == 0)
			{
				m_logger.debugLog("no entities intersect path", Logger.severity.TRACE);
				return true;
			}
			m_logger.debugLog("entities intersecting path: " + offenders.Count);

			//foreach (var ent in offenders)
			//	m_logger.debugLog("entity: " + ent.getBestName(), "TestPath()");

			m_offendingEntities = offenders.OrderBy(entity => m_grid.WorldAABB.Distance(entity.PositionComp.WorldAABB));

			return false;
		}

		public bool TestSlow(out MyEntity blockingPath, out Vector3? pointOfObstruction)
		{
			m_logger.debugLog("m_offendingEntities == null, did you remember to call TestFast()?", Logger.severity.FATAL, condition: m_offendingEntities == null);

			IMyCubeBlock ignoreBlock = m_ignoreEntity as IMyCubeBlock;

			foreach (MyEntity entity in m_offendingEntities)
			{
				m_logger.debugLog("checking entity: " + entity.getBestName());

				MyVoxelBase voxel = entity as MyVoxelBase;
				if (voxel != null)
					if (TestVoxel(voxel, m_path, out pointOfObstruction))
						continue;
					else
					{
						blockingPath = entity;
						return false;
					}

				IMyCubeGrid grid = entity as IMyCubeGrid;
				if (grid != null)
				{
					if (m_profiler.rejectionIntersects(grid, ignoreBlock, out blockingPath, out pointOfObstruction))
						return false;
					continue;
				}

				m_logger.debugLog("not a grid, testing bounds");
				if (!m_path.IntersectsAABB(entity))
					continue;

				if (!m_path.IntersectsVolume(entity))
					continue;

				m_logger.debugLog("no more tests for non-grids are implemented", Logger.severity.DEBUG);
				pointOfObstruction = m_path.get_Line().ClosestPoint(entity.GetCentre());
				blockingPath = entity;
				return false;
			}

			blockingPath = null;
			pointOfObstruction = null;
			return true;
		}

		/// <summary>
		/// Tests for path intersection with voxel.
		/// </summary>
		/// <returns>True iff path is clear; voxel does not intersect path.</returns>
		private bool TestVoxel(MyVoxelBase voxel, Capsule path, out Vector3? pointOfObstruction)
		{
			if (m_ignoreAsteroid)
			{
				m_logger.debugLog("Ignoring asteroid: " + voxel.getBestName());
				pointOfObstruction = null;
				return true;
			}

			Vector3[] intersection = new Vector3[2];
			if (!path.IntersectsAABB(voxel, out  intersection[0]))
			{
				m_logger.debugLog("path does not intersect AABB. " + voxel.getBestName(), Logger.severity.TRACE);
				pointOfObstruction = null;
				return true;
			}

			if (!path.get_Reverse().IntersectsAABB(voxel, out intersection[1]))
			{
				m_logger.debugLog("Reversed path does not intersect AABB, perhaps it moved? " + voxel.getBestName(), Logger.severity.WARNING);
				pointOfObstruction = null;
				return true;
			}

			Capsule testSection = new Capsule(intersection[0], intersection[1], path.Radius);
			IMyVoxelMap asteroid = voxel as IMyVoxelMap;
			if (asteroid != null)
			{
				if (testSection.Intersects(asteroid, out pointOfObstruction))
					return false;
			}
			// planet test is done by PlanetChecker

			m_logger.debugLog("Does not intersect path: " + voxel.getBestName(), Logger.severity.TRACE);
			pointOfObstruction = null;
			return true;
		}

		private List<MyVoxelBase> m_voxels = new List<MyVoxelBase>();

		//public bool GravityTest(LineSegmentD line, Vector3D finalDestination, out MyEntity blockingPath, out Vector3? pointOfObstruction)
		//{
		//	if (Vector3.DistanceSquared(line.From, line.To) > 10000f)
		//	{
		//		BoundingBoxD box = line.BoundingBox;
		//		m_voxels.Clear();
		//		MyGamePruningStructure.GetAllVoxelMapsInBox(ref box, m_voxels);

		//		foreach (MyEntity entity in m_voxels)
		//		{
		//			MyPlanet planet = entity as MyPlanet;
		//			if (planet == null)
		//				continue;

		//			Vector3D planetCentre = planet.GetCentre();
		//			Vector3D closestPoint = line.ClosestPoint(planetCentre);

		//			if (!planet.IsPositionInGravityWell(closestPoint))
		//				continue;

		//			m_logger.debugLog("path: " + line.From + " to " + line.To + ", final: " + finalDestination + ", closest point: " + closestPoint + ", planet @ " + planetCentre + " : " + planet.getBestName(), "GravityTest()");
			
		//			if (closestPoint == line.From)
		//			{
		//				m_logger.debugLog("closest point is start", "GravityTest()");
		//				continue;
		//			}
		//			if (closestPoint == line.To)
		//			{
		//				if (line.To == finalDestination )
		//				{
		//					m_logger.debugLog("closest point is final", "GravityTest()");
		//					continue;
		//				}
		//				if (Vector3D.DistanceSquared(planetCentre, closestPoint) < Vector3D.DistanceSquared(planetCentre, finalDestination))
		//				{
		//					m_logger.debugLog("closest point is end, which is closer to planet than final dest is", "GravityTest()");
		//					blockingPath = entity;
		//					pointOfObstruction = closestPoint;
		//					return false;
		//				}
		//				m_logger.debugLog("closest point is end", "GravityTest()");
		//				continue;
		//			}

		//			//float startGravity = planet.GetWorldGravityGrid( line.From).LengthSquared();
		//			//float closestGravity = planet.GetWorldGravityGrid(closestPoint).LengthSquared();

		//			//double toDistSq = Vector3D.DistanceSquared(planetCentre, line.To);

		//			//if (closestPoint == line.To && (line.To == finalDestination ||     ))
		//			//{
		//			//	m_logger.debugLog("path: " + line.From + " to " + line.To + ", closest point: " + closestPoint + ", planet @ " + planetCentre + " : " + planet.getBestName(), "GravityTest()");
		//			//	m_logger.debugLog("closest point is end", "GravityTest()");
		//			//	continue;
		//			//}

		//			//m_logger.debugLog("path: " + line.From + " to " + line.To + ", closest point: " + closestPoint + ", planet @ " + planetCentre + " : " + planet.getBestName(), "GravityTest()", Logger.severity.DEBUG);


		//			double closestDistSq = Vector3D.DistanceSquared(planetCentre, closestPoint) + 1f;
		//			if (closestDistSq < Vector3D.DistanceSquared(planetCentre, line.From) || closestDistSq < Vector3D.DistanceSquared(planetCentre, line.To))
		//			{
		//				m_logger.debugLog("path moves ship closer to planet. closestDistSq: " + closestDistSq + ", from dist sq: " + Vector3D.DistanceSquared(planetCentre, line.From) + ", to dist sq: " + Vector3D.DistanceSquared(planetCentre, line.To), "GravityTest()", Logger.severity.INFO);
		//				blockingPath = entity;
		//				pointOfObstruction = closestPoint;
		//				return false;
		//			}
		//		}
		//	}

		//	blockingPath = null;
		//	pointOfObstruction = null;
		//	return true;
		//}

		/// <summary>
		/// How far long the line would the ship be able to travel? Uses a capsule derived from previously calculated path.
		/// </summary>
		/// <param name="canTravel">Line along which navigation block would travel</param>
		/// <remarks>
		/// Capsule only test because the ship will not be oriented correctly
		/// </remarks>
		/// <returns>distance from the destination that can be reached</returns>
		public float distanceCanTravel(LineSegment canTravel)
		{
			BoundingBoxD atFrom = m_grid.WorldAABB.Translate(canTravel.From - m_grid.GetPosition());
			BoundingBoxD atTo = m_grid.WorldAABB.Translate(canTravel.To - m_grid.GetPosition());

			ICollection<MyEntity> offenders = EntitiesInLargeAABB(atFrom, atTo);
			if (offenders.Count == 0)
			{
				m_logger.debugLog("AABB is empty");
				return 0;
			}
			m_logger.debugLog("collected entities to test: " + offenders.Count);
			IOrderedEnumerable<MyEntity> ordered = offenders.OrderBy(entity => Vector3D.Distance(canTravel.From, entity.GetCentre()));

			Capsule path = new Capsule(canTravel.From, canTravel.To, m_path.Radius);
			Vector3? pointOfObstruction = null;
			foreach (MyEntity entity in ordered)
				if (path.IntersectsAABB(entity))
				{
					MyVoxelBase voxel = entity as MyVoxelBase;
					if (voxel != null)
						if (!TestVoxel(voxel, path, out pointOfObstruction))
						{
							m_logger.debugLog("obstruction at " + pointOfObstruction + " distance from dest is " + Vector3.Distance(canTravel.To, (Vector3)pointOfObstruction));
							return Vector3.Distance(canTravel.To, (Vector3)pointOfObstruction);
						}

					IMyCubeGrid grid = entity as IMyCubeGrid;
					if (grid != null)
					{
						float minDistSq = (m_grid.GridSize + grid.GridSize) * (m_grid.GridSize + grid.GridSize);
						GridCellCache cache = GridCellCache.GetCellCache(grid);
						cache.ForEach(cell => {
							Vector3 cellPos = grid.GridIntegerToWorld(cell);
							if (canTravel.PointInCylinder(minDistSq, ref cellPos))
							{
								pointOfObstruction = cellPos;
								return true;
							}
							return false;
						});
						if (pointOfObstruction.HasValue)
							return Vector3.Distance(canTravel.To, (Vector3)pointOfObstruction);
					}

					m_logger.debugLog("not a grid, testing bounds");
					if (!m_path.IntersectsVolume(entity))
						continue;

					m_logger.debugLog("no more tests for non-grids are implemented", Logger.severity.DEBUG);
					pointOfObstruction = m_path.get_Line().ClosestPoint(entity.GetCentre());
					return Vector3.Distance(canTravel.To, (Vector3)pointOfObstruction);
				}

			// no obstruction
			m_logger.debugLog("no obstruction");
			return 0f;
		}

		private List<MyEntity> EntitiesInLargeAABB(BoundingBoxD start, BoundingBoxD end)
		{
			Vector3D[] pathPoints = new Vector3D[4];
			pathPoints[0] = start.Min;
			pathPoints[1] = start.Max;
			pathPoints[2] = end.Min;
			pathPoints[3] = end.Max;
			BoundingBoxD PathAABB = BoundingBoxD.CreateFromPoints(pathPoints);
			//m_logger.debugLog("Path AABB = " + PathAABB, "EntitiesInLargeAABB()");

			m_offenders.Clear();
			MyGamePruningStructure.GetTopMostEntitiesInBox(ref PathAABB, m_offenders);
			m_offRemove.Clear();
			for (int i = 0; i < m_offenders.Count; i++)
				if (!collect_Entity(m_grid, m_offenders[i])
					|| (m_ignoreEntity != null && m_ignoreEntity == m_offenders[i]))
				{
					//m_logger.debugLog("discarding: " + m_offenders[i].getBestName(), "EntitiesInLargeAABB()");
					m_offRemove.Add(m_offenders[i]);
				}
			for (int i = 0; i < m_offRemove.Count; i++)
				m_offenders.Remove(m_offRemove[i]);

			return m_offenders;
		}

		/// <returns>true iff the entity should be kept</returns>
		public static bool collect_Entity(IMyCubeGrid myGrid, MyEntity entity)
		{
			if (!(entity is IMyCubeGrid) && !(entity is IMyVoxelMap) && !(entity is IMyFloatingObject))
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

	}
}
