#define LOG_ENABLED // remove on build

using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinder
{
	public class PathChecker
	{
		public IMyCubeGrid CubeGrid { get; private set; }
		public bool IgnoreAsteroids = false;

		private GridShapeProfiler myGridShape;
		private Logger myLogger = new Logger(null, "PathChecker");

		public PathChecker(IMyCubeGrid grid)
		{
			CubeGrid = grid;
			myLogger = new Logger("PathChecker", () => CubeGrid.DisplayName);
		}

		/// <summary>
		/// Test the path for obstructions
		/// </summary>
		/// <returns>true iff the path is clear from obstructions</returns>
		public bool TestPath(RelativeVector3F destination, IMyCubeBlock navigationBlock)
		{
			destination.throwIfNull_argument("destination");
			destination.throwIfNull_argument("navigationBlock");

			myLogger.debugLog("destination (world absolute) = " + destination.getWorldAbsolute(), "TestPath()");
			myLogger.debugLog("destination (nav block) = " + destination.getBlock(navigationBlock), "TestPath()");

			Vector3D Displacement = destination.getWorldAbsolute() - navigationBlock.GetPosition();
			//Vector3 Displacement = destination.getLocal() - navigationBlock.Position * CubeGrid.GridSize;
			myLogger.debugLog("Displacement = " + Displacement, "TestPath()");

			// entities in large AABB
			BoundingBoxD AtDest = CubeGrid.WorldAABB.Translate(Displacement);
			//BoundingBoxD PathAABB = BoundingBoxD.CreateMerged(CubeGrid.WorldAABB, AtDest);

			List<Vector3D> PathPoints = new List<Vector3D>();
			PathPoints.Add(CubeGrid.WorldAABB.Min);
			PathPoints.Add(CubeGrid.WorldAABB.Max);
			PathPoints.Add(AtDest.Min);
			PathPoints.Add(AtDest.Max);
			BoundingBoxD PathAABB = BoundingBoxD.CreateFromPoints(PathPoints);
			myLogger.debugLog("Path AABB = " + PathAABB, "TestPath()");

			List<IMyEntity> offenders = MyAPIGateway.Entities.GetEntitiesInAABB_Safe(ref PathAABB);
			if (offenders.Count == 0)
			{
				myLogger.debugLog("AABB is empty", "TestPath()", Logger.severity.DEBUG);
				return true;
			}
			myLogger.debugLog("collected entities to test: " + offenders.Count, "TestPath()");

			// filter offenders and sort by distance
			Vector3D Centre = CubeGrid.GetCentre();
			SortedDictionary<float, IMyEntity> sortedOffenders = new SortedDictionary<float, IMyEntity>();
			foreach (IMyEntity entity in offenders)
			{
				if (collect_Entities(entity))
				{
					float distanceSquared = Vector3.DistanceSquared(Centre, entity.GetCentre());
					sortedOffenders.Add(distanceSquared, entity);
				}
				//else
				//	myLogger.debugLog("ignoring: " + entity.getBestName(), "TestPath()");
			}
			if (sortedOffenders.Count == 0)
			{
				myLogger.debugLog("all offenders are ignored", "TestPath()", Logger.severity.DEBUG);
				return true;
			}
			myLogger.debugLog("remaining after ignore list: " + sortedOffenders.Count, "TestPath()");

			// set destination
			myGridShape = GridShapeProfiler.getFor(CubeGrid);
			myGridShape.SetDestination(destination, navigationBlock, Displacement);
			Path myPath = myGridShape.myPath;

			// test path
			foreach (IMyEntity entity in sortedOffenders.Values)
			{
				myLogger.debugLog("testing offender: " + entity.getBestName() + " at " + entity.GetPosition(), "TestPath()");

				IMyCubeGrid asGrid = entity as IMyCubeGrid;
				if (asGrid != null)
				{
					if (!AABB_intersects_path(entity, myPath))
						continue;

					myLogger.debugLog("searching blocks of " + entity.getBestName(), "TestPath()");
					uint blockCount = 0, blockRejectedCount = 0;

					// foreach block
					List<IMySlimBlock> allSlims = new List<IMySlimBlock>();
					asGrid.GetBlocks_Safe(allSlims);
					foreach (IMySlimBlock slim in allSlims)
					{
						// position. TODO: each cell a block occupies
						Vector3 blockPosWorld = slim.CubeGrid.GridIntegerToWorld(slim.Position);
						RelativeVector3F blockPosition = RelativeVector3F.createFromWorld(blockPosWorld, CubeGrid);
						blockCount++;

						// intersects capsule
						if (myPath.line_local.DistanceSquared(blockPosition.getLocal()) < myPath.BufferedRadiusSquared)
							continue;

						blockRejectedCount++;
						// rejection
						Vector3 offendingBlockRejection = myGridShape.rejectVector(blockPosition.getLocal());
						foreach (Vector3 myBlockRejection in myGridShape.rejectionCells)
							if (Vector3.DistanceSquared(offendingBlockRejection, myBlockRejection) < myPath.BufferSquared)
							{
								myLogger.debugLog("obstructing grid = " + asGrid.DisplayName + ", block = " + slim.getBestName(), "TestPath()", Logger.severity.DEBUG);
								return false;
							}
					}
					myLogger.debugLog("no obstruction for grid " + asGrid.DisplayName + ", tested " + blockCount + " against capsule and " + blockRejectedCount + " against rejection", "TestPath()");
					continue;
				}

				// not a grid
				myLogger.debugLog("not a grid, testing AABB", "TestPath()");
				if (!AABB_intersects_path(entity, myPath))
					continue;

				myLogger.debugLog("no more tests for non-grids are implemented", "TestPath()", Logger.severity.DEBUG);
				return false;
			}

			myLogger.debugLog("no obstruction was found", "TestPath()", Logger.severity.DEBUG);
			return true;
		}

		/// <returns>true iff the entity should be kept</returns>
		private bool collect_Entities(IMyEntity entity)
		{
			if (!(entity is IMyCubeGrid) && !(entity is IMyVoxelMap) && !(entity is IMyFloatingObject))
				return false;

			//if (entity is IMyCubeBlock) // blocks will be tested when grid intersects capsule
			//	return false;

			//if (entity is IMyCharacter)
			//	return false;

			IMyCubeGrid asGrid = entity as IMyCubeGrid;
			if (asGrid != null)
			{
				if (asGrid == CubeGrid)
					return false;

				if (AttachedGrids.isGridAttached(CubeGrid, asGrid))
					return false;
			}

			return true;
		}

		private bool AABB_intersects_path(IMyEntity entity, Path myPath)
		{
			BoundingBoxD AABB = entity.WorldAABB;
			AABB.Inflate(myPath.BufferedRadius);
			double distance;
			myLogger.debugLog("inflated " + entity.WorldAABB + " to " + AABB, "TestPath()");
			myLogger.debugLog("line = " + myPath.line_world.From + " to " + myPath.line_world.To, "TestPath()");
			if (entity.WorldAABB.Intersects(myPath.line_world, out distance))
			{
				myLogger.debugLog("for " + entity.getBestName() + ", AABB(" + entity.WorldAABB + ") intersect path, distance = " + distance, "TestPath()", Logger.severity.DEBUG);
				return true;
			}
			myLogger.debugLog("for " + entity.getBestName() + ", AABB(" + entity.WorldAABB + ") does not intersect path, distance = " + distance, "TestPath()", Logger.severity.DEBUG);
			return false;
		}
	}
}
