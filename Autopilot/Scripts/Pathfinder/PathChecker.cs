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
		public IMyCubeGrid myCubeGrid { get; private set; }
		public bool IgnoreAsteroids = false;

		private GridShapeProfiler myGridShape;
		private Logger myLogger = new Logger(null, "PathChecker");

		public PathChecker(IMyCubeGrid grid)
		{
			myCubeGrid = grid;
			myLogger = new Logger("PathChecker", () => myCubeGrid.DisplayName);
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
			myLogger.debugLog("destination (local) = " + destination.getLocal(), "TestPath()");
			myLogger.debugLog("destination (nav block) = " + destination.getBlock(navigationBlock), "TestPath()");

			Vector3D Displacement = destination.getWorldAbsolute() - navigationBlock.GetPosition();
			//Vector3 Displacement = destination.getLocal() - navigationBlock.Position * CubeGrid.GridSize;
			myLogger.debugLog("Displacement = " + Displacement, "TestPath()");

			// entities in large AABB
			BoundingBoxD AtDest = myCubeGrid.WorldAABB.Translate(Displacement);
			//BoundingBoxD PathAABB = BoundingBoxD.CreateMerged(CubeGrid.WorldAABB, AtDest);

			List<Vector3D> PathPoints = new List<Vector3D>();
			PathPoints.Add(myCubeGrid.WorldAABB.Min);
			PathPoints.Add(myCubeGrid.WorldAABB.Max);
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
			Vector3D Centre = myCubeGrid.GetCentre();
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
			myGridShape = GridShapeProfiler.getFor(myCubeGrid);
			//var results = TimeAction.Time(()=>{
			//myGridShape.SetDestination(destination, navigationBlock);}, 10);
			//myLogger.debugLog("Timed Set Destination: " + results.Pretty_FiveNumbers(), "TestPath()");
			myGridShape.SetDestination(destination, navigationBlock);
			Capsule myPath = myGridShape.myPath;

			// test path
			foreach (IMyEntity entity in sortedOffenders.Values)
			{
				myLogger.debugLog("testing offender: " + entity.getBestName() + " at " + entity.GetPosition(), "TestPath()");

				IMyCubeGrid asGrid = entity as IMyCubeGrid;
				if (asGrid != null)
				{
					if (!myPath.IntersectsAABB(entity))//  !AABB_intersects_path(entity, myPath))
						continue;

					myLogger.debugLog("searching blocks of " + entity.getBestName(), "TestPath()");
					uint cellCount = 0, cellRejectedCount = 0;

					// foreach block
					float GridSize = asGrid.GridSize;
					List<IMySlimBlock> allSlims = new List<IMySlimBlock>();
					asGrid.GetBlocks_Safe(allSlims);
					foreach (IMySlimBlock slim in allSlims)
					{
						bool blockIntersects = false;
						slim.ForEachCell((cell) =>
							{
								Vector3 cellPosWorld = asGrid.GridIntegerToWorld(cell);
								//RelativeVector3F cellPosition = RelativeVector3F.createFromWorld(cellPosWorld, myCubeGrid);
								cellCount++;
								//Vector3 worldPosition = asGrid.GridIntegerToWorld(cell);

								// intersects capsule
								//myLogger.debugLog("distance(sq) between " + slim.getBestName() + " and path {" + myPath.P0.getWorldAbsolute() + ", " + myPath.P1.getWorldAbsolute() + "} is " + myPath.line_local.DistanceSquared(blockPosition.getLocal()) + ", BufferedRadiusSquared = " + myPath.BufferedRadiusSquared, "TestPath()");
								//if (myPath.line_local.DistanceSquared(blockPosition.getLocal()) > myPath.BufferedRadiusSquared)
								if (!myPath.Intersects(cellPosWorld, GridSize))
									return false;

								// rejection
								cellRejectedCount++;
								//Vector3 offendingBlockRejection = myGridShape.rejectVector(cellPosition.getLocal());
								if (myGridShape.rejectionIntersects( RelativeVector3F.createFromWorldAbsolute(cellPosWorld, myCubeGrid), myCubeGrid.GridSize))
								//myGridShape.rejectionIntersects(
								//foreach (Vector3 myBlockRejection in myGridShape.rejectionCells)
								//{
								//	myLogger.debugLog("comparing offending cell at " + offendingBlockRejection + " to " + myBlockRejection + ", distance is " + Vector3.DistanceSquared(offendingBlockRejection, myBlockRejection), "TestPath()");
								//	if (Vector3.DistanceSquared(offendingBlockRejection, myBlockRejection) < myPath.BufferSquared)
									{
										myLogger.debugLog("obstructing grid = " + asGrid.DisplayName + ", block = " + slim.getBestName(), "TestPath()", Logger.severity.DEBUG);
										blockIntersects = true;
										return true;
									}
								return false;
								//}
							});
						if (blockIntersects)
							return false;
					}
					myLogger.debugLog("no obstruction for grid " + asGrid.DisplayName + ", tested " + cellCount + " against capsule and " + cellRejectedCount + " against rejection", "TestPath()");
					continue;
				}

				// not a grid
				myLogger.debugLog("not a grid, testing bounds", "TestPath()");
				if (!myPath.IntersectsAABB(entity))
					continue;

				if (!myPath.IntersectsVolume(entity))
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
				if (asGrid == myCubeGrid)
					return false;

				if (AttachedGrids.isGridAttached(myCubeGrid, asGrid))
					return false;
			}

			return true;
		}

		//private bool AABB_intersects_path(IMyEntity entity, PathCapsule myPath)
		//{
		//	BoundingBoxD AABB = entity.WorldAABB;
		//	AABB.Inflate(myPath.BufferedRadius);
		//	double distance;
		//	//myLogger.debugLog("inflated " + entity.WorldAABB + " to " + AABB, "TestPath()");
		//	//myLogger.debugLog("line = " + myPath.line_world.From + " to " + myPath.line_world.To, "TestPath()");
		//	if (entity.WorldAABB.Intersects(myPath.line_world, out distance))
		//	{
		//		myLogger.debugLog("for " + entity.getBestName() + ", AABB(" + entity.WorldAABB + ") intersects path, distance = " + distance, "TestPath()", Logger.severity.DEBUG);
		//		return true;
		//	}
		//	//myLogger.debugLog("for " + entity.getBestName() + ", AABB(" + entity.WorldAABB + ") does not intersect path, distance = " + distance, "TestPath()", Logger.severity.DEBUG);
		//	return false;
		//}
	}
}
