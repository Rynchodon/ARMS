#define LOG_ENABLED // remove on build

using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using VRage;
using Sandbox.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinder
{
	/// <summary>
	/// Checks paths for obstructing entities
	/// </summary>
	internal class PathChecker
	{
		public IMyCubeGrid myCubeGrid { get; private set; }
		//public bool IgnoreAsteroids = false;

		//private GridShapeProfiler myGridShape;
		private Logger myLogger = new Logger(null, "PathChecker");

		public PathChecker(IMyCubeGrid grid)
		{
			myCubeGrid = grid;
			myLogger = new Logger("PathChecker", () => myCubeGrid.DisplayName);
		}

		/// <summary>
		/// Test the path for obstructions
		/// </summary>
		/// <exception cref="InterruptException">If interrupted</exception>
		/// I considered keeping track of the closest entity. This would have been, at best, unreliable due to initial AABB test.
		public IMyEntity TestPath(RelativeVector3F destination, IMyCubeBlock navigationBlock, bool IgnoreAsteroids)
		{
			destination.throwIfNull_argument("destination");
			destination.throwIfNull_argument("navigationBlock");

			Interrupt = false;

			myLogger.debugLog("destination (world absolute) = " + destination.getWorldAbsolute(), "TestPath()");
			myLogger.debugLog("destination (local) = " + destination.getLocal(), "TestPath()");
			myLogger.debugLog("destination (nav block) = " + destination.getBlock(navigationBlock), "TestPath()");

			Vector3D Displacement = destination.getWorldAbsolute() - navigationBlock.GetPosition();
			myLogger.debugLog("Displacement = " + Displacement, "TestPath()");

			// entities in large AABB
			BoundingBoxD AtDest = myCubeGrid.WorldAABB.Translate(Displacement);

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
				return null;
			}
			myLogger.debugLog("collected entities to test: " + offenders.Count, "TestPath()");

			// filter offenders and sort by distance
			Vector3D Centre = myCubeGrid.GetCentre();
			SortedDictionary<float, IMyEntity> sortedOffenders = new SortedDictionary<float, IMyEntity>();
			foreach (IMyEntity entity in offenders)
			{
				CheckInterrupt();
				if (collect_Entities(entity))
				{
					float distanceSquared = Vector3.DistanceSquared(Centre, entity.GetCentre());
					sortedOffenders.Add(distanceSquared, entity);
				}
			}
			if (sortedOffenders.Count == 0)
			{
				myLogger.debugLog("all offenders are ignored", "TestPath()", Logger.severity.DEBUG);
				return null;
			}
			myLogger.debugLog("remaining after ignore list: " + sortedOffenders.Count, "TestPath()");

			// set destination
			GridShapeProfiler myGridShape = GridShapeProfiler.getFor(myCubeGrid);
			myGridShape.SetDestination(destination, navigationBlock);
			Capsule myPath = myGridShape.myPath;

			// test path
			foreach (IMyEntity entity in sortedOffenders.Values)
			{
				CheckInterrupt();
				myLogger.debugLog("testing offender: " + entity.getBestName() + " at " + entity.GetPosition(), "TestPath()");

				IMyCubeGrid asGrid = entity as IMyCubeGrid;
				if (asGrid != null)
				{
					if (!myPath.IntersectsAABB(entity))
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
							CheckInterrupt();
							Vector3 cellPosWorld = asGrid.GridIntegerToWorld(cell);
							cellCount++;

							// intersects capsule
							if (!myPath.Intersects(cellPosWorld, GridSize))
								return false;

							// rejection
							cellRejectedCount++;
							if (myGridShape.rejectionIntersects(RelativeVector3F.createFromWorldAbsolute(cellPosWorld, myCubeGrid), myCubeGrid.GridSize))
							{
								myLogger.debugLog("obstructing grid = " + asGrid.DisplayName + ", block = " + slim.getBestName(), "TestPath()", Logger.severity.DEBUG);
								blockIntersects = true;
								return true;
							}
							return false;
						});
						if (blockIntersects)
							return entity;
					}
					myLogger.debugLog("no obstruction for grid " + asGrid.DisplayName + ", tested " + cellCount + " against capsule and " + cellRejectedCount + " against rejection", "TestPath()");
					continue;
				}

				// not a grid
				if (IgnoreAsteroids && entity is IMyVoxelMap)
				{
					myLogger.debugLog("Ignoring asteroid: " + entity.getBestName(), "TestPath()");
					continue;
				}

				myLogger.debugLog("not a grid, testing bounds", "TestPath()");
				//if (!myPath.IntersectsAABB(entity))
				//	continue;

				if (!myPath.IntersectsVolume(entity))
					continue;

				myLogger.debugLog("no more tests for non-grids are implemented", "TestPath()", Logger.severity.DEBUG);
				return entity;
			}

			myLogger.debugLog("no obstruction was found", "TestPath()", Logger.severity.DEBUG);
			return null;
		}

		/// <returns>true iff the entity should be kept</returns>
		private bool collect_Entities(IMyEntity entity)
		{
			if (!(entity is IMyCubeGrid) && !(entity is IMyVoxelMap) && !(entity is IMyFloatingObject))
				return false;

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

		#region Interrupt

		public bool Interrupt = false;

		private void CheckInterrupt()
		{
			if (Interrupt)
				throw new InterruptException();
		}

		#endregion
	}
}
