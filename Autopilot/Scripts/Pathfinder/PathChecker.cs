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
			myLogger = new Logger("PathChecker", CubeGrid.DisplayName);
		}

		/// <summary>
		/// Test the path for obstructions
		/// </summary>
		/// <param name="destination"></param>
		/// <param name="navigationBlock"></param>
		/// <returns>true iff the path is clear from obstructions</returns>
		private bool TestPath(RelativeVector3F destination, IMyCubeBlock navigationBlock)
		{
			Vector3 Displacement = destination.getGrid() - navigationBlock.Position * CubeGrid.GridSize;

			// entities in AABB
			BoundingBoxD AtDest = CubeGrid.WorldAABB.Translate(Displacement);
			BoundingBoxD PathAABB = BoundingBoxD.CreateMerged(CubeGrid.WorldAABB, AtDest);
			List<IMyEntity> offenders = MyAPIGateway.Entities.GetEntitiesInAABB_Safe(ref PathAABB);
			if (offenders.Count == 0)
			{
				myLogger.debugLog("AABB is empty", "TestPath()", Logger.severity.DEBUG);
				return true;
			}

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
			}
			if (sortedOffenders.Count == 0)
			{
				myLogger.debugLog("all offenders are ignored", "TestPath()", Logger.severity.DEBUG);
				return true;
			}

			// set destination
			myGridShape.SetDestination(destination, navigationBlock, Displacement);
			Path myPath = myGridShape.myPath;
			
			// test path
			foreach (IMyEntity entity in sortedOffenders.Values)
			{
				IMyCubeGrid asGrid = entity as IMyCubeGrid;
				if (asGrid != null)
				{
					// grid's AABB intersects capsule?
					
					// foreach block
					List<IMySlimBlock> allSlims = new List<IMySlimBlock>();
					asGrid.GetBlocks_Safe(allSlims);
					foreach	(IMySlimBlock slim in allSlims)
					{
						// position
						Vector3 blockPosWorld = slim.CubeGrid.GridIntegerToWorld(slim.Position);
						RelativeVector3F blockPosition = RelativeVector3F.createFromWorld(blockPosWorld, CubeGrid);

						// intersects capsule
						if (myPath.line.DistanceSquared(blockPosition.getGrid()) < myPath.BufferedRadiusSquared)
							continue;

						// rejection
						Vector3 offendingBlockRejection = myGridShape.rejectVector(blockPosition.getGrid());
						foreach (Vector3 myBlockRejection in myGridShape.rejectionCells)
							if (Vector3.DistanceSquared(offendingBlockRejection, myBlockRejection) < myGridShape. PathBufferSquared)
							{
								myLogger.debugLog("obstructing grid = " + asGrid.DisplayName + ", block = " + slim.getBestName(), "TestPath()");
								return false;
							}
					}
					continue;
				}

				// not a grid

				IMyVoxelMap asVoxel = entity as IMyVoxelMap;
				if (asVoxel != null)
				{
					// test voxel
				}
			}

			myLogger.debugLog("no obstruction was found", "TestPath()");
			return true;
		}

		/// <returns>true iff the entity should be kept</returns>
		private bool collect_Entities(IMyEntity entity)
		{
			IMyCubeGrid asGrid = entity as IMyCubeGrid;
			IMyVoxelMap asVoxel = entity as IMyVoxelMap;
			IMyVoxelShape asShape = entity as IMyVoxelShape;

			if (asGrid == null && asVoxel == null && asShape == null)
				return false;

			if (entity.Physics != null && entity.Physics.Mass > 0 && entity.Physics.Mass < 1000)
				return false;

			if (asGrid != null)
			{
				if (asGrid == CubeGrid)
					return false;

				if (AttachedGrids.isGridAttached(CubeGrid, asGrid))
					return false;
			}

			return true;
		}
	}
}
