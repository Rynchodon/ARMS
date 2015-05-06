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

		private IMyCubeGrid DestGrid;
		private IMyCubeBlock NavigationBlock;
		private Capsule myPath;
		private bool IgnoreAsteroids;

		private Logger myLogger = new Logger(null, "PathChecker");

		public PathChecker(IMyCubeGrid grid)
		{
			myCubeGrid = grid;
			myLogger = new Logger("PathChecker", () => myCubeGrid.DisplayName);
		}

		#region Public Test Functions

		/// <summary>
		/// Test the path for obstructions
		/// </summary>
		/// <exception cref="InterruptException">If interrupted</exception>
		/// I considered keeping track of the closest entity, in the event there was no obstruction. This would have been, at best, unreliable due to initial AABB test.
		public IMyEntity TestPath(Vector3D worldDestination, IMyCubeBlock navigationBlock, bool IgnoreAsteroids, out Vector3? pointOfObstruction, IMyCubeGrid DestGrid)
		{
			worldDestination.throwIfNull_argument("destination");
			worldDestination.throwIfNull_argument("navigationBlock");

			Interrupt = false;
			this.NavigationBlock = navigationBlock;
			this.IgnoreAsteroids = IgnoreAsteroids;
			this.DestGrid = DestGrid;

			myLogger.debugLog("Test path to (world absolute) " + worldDestination, "TestPath()");
			//myLogger.debugLog("destination (local) = " + worldDestination.getLocal(), "TestPath()");
			//myLogger.debugLog("destination (nav block) = " + worldDestination.getBlock(navigationBlock), "TestPath()");

			Vector3D Displacement = worldDestination - navigationBlock.GetPosition();
			myLogger.debugLog("Displacement = " + Displacement, "TestPath()");

			// entities in large AABB
			BoundingBoxD AtDest = myCubeGrid.WorldAABB.Translate(Displacement);
			ICollection<IMyEntity> offenders = EntitiesInLargeAABB(myCubeGrid.WorldAABB, AtDest);
			if (offenders.Count == 0)
			{
				myLogger.debugLog("AABB is empty", "TestPath()", Logger.severity.DEBUG);
				pointOfObstruction = null;
				return null;
			}
			myLogger.debugLog("collected entities to test: " + offenders.Count, "TestPath()");

			// sort offenders by distance
			offenders = SortByDistance(offenders);

			// set destination
			GridShapeProfiler myGridShape = GridShapeProfiler.getFor(myCubeGrid);
			//myLogger.debugLog("destination = " + worldDestination.getWorldAbsolute() + ", navigationBlock = " + navigationBlock.GetPosition(), "TestPath()");
			myGridShape.SetDestination(RelativeVector3F.createFromWorldAbsolute(worldDestination, myCubeGrid), navigationBlock);
			myPath = myGridShape.myPath;
			myLogger.debugLog("got path from " + myPath.P0 + " to " + myPath.P1 + " with radius " + myPath.Radius, "TestPath()");

			// test path
			return TestEntities(offenders, myPath, myGridShape, out pointOfObstruction, this.DestGrid);
		}

		/// <summary>
		/// How far long the line would the ship be able to travel? Uses a capsule derived from previously calculated path.
		/// </summary>
		/// <param name="canTravel">Line along which navigation block would travel</param>
		/// <remarks>
		/// Capsule only test because the ship will not be oriented correctly
		/// </remarks>
		/// <returns>distance from the destination that can be reached</returns>
		public float distanceCanTravel(Line canTravel)
		{
			Vector3D navBlockPos = NavigationBlock.GetPosition();

			Vector3D DisplacementStart = canTravel.From - navBlockPos;
			Vector3D DisplacementEnd = canTravel.To - navBlockPos;

			BoundingBoxD atStart = myCubeGrid.WorldAABB.Translate(DisplacementStart);
			BoundingBoxD atDest = myCubeGrid.WorldAABB.Translate(DisplacementEnd);

			ICollection<IMyEntity> offenders = EntitiesInLargeAABB(atStart, atDest);
			if (offenders.Count == 0)
			{
				myLogger.debugLog("AABB is empty", "distanceCanTravel()");
				return 0;
			}
			myLogger.debugLog("collected entities to test: " + offenders.Count, "TestPath()");
			offenders = SortByDistance(offenders);
			//if (offenders.Count == 0)
			//{
			//	myLogger.debugLog("all offenders ignored", "distanceCanTravel()");
			//	return 0;
			//}
			//myLogger.debugLog("remaining after ignore list: " + offenders.Count, "TestPath()");

			Capsule _path = new Capsule(canTravel.From, canTravel.To, myPath.Radius);
			Vector3? pointOfObstruction;
			IMyEntity obstruction = TestEntities(offenders, _path, null, out pointOfObstruction, DestGrid);
			if (obstruction == null)
			{
				myLogger.debugLog("no obstruction", "distanceCanTravel()");
				return 0;
			}

			myLogger.debugLog("obstruction at " + pointOfObstruction + " distance from dest is " + Vector3.Distance(canTravel.To, (Vector3)pointOfObstruction), "distanceCanTravel()");
			return Vector3.Distance(canTravel.To, (Vector3)pointOfObstruction);
		}

		public const double NearbyRange = 2000;

		/// <summary>
		/// How far away is the closest entity to myCubeGrid?
		/// </summary>
		/// <returns>Distance to closest Entity, may be negative</returns>
		public double ClosestEntity()
		{
			BoundingBoxD NearbyBox = myCubeGrid.WorldAABB;
			NearbyBox.Inflate(NearbyRange);

			HashSet<IMyEntity> offenders = new HashSet<IMyEntity>();// = MyAPIGateway.Entities.GetEntitiesInAABB_Safe(ref NearbyBox);
			MyAPIGateway.Entities.GetEntitiesInAABB_Safe_NoBlock(NearbyBox, offenders, collect_Entities);
			if (offenders.Count == 0)
			{
				myLogger.debugLog("AABB is empty", "ClosestEntity()");
				return NearbyRange;
			}
			myLogger.debugLog("collected entities to test: " + offenders.Count, "ClosestEntity()");

			double distClosest = NearbyRange;
			foreach (IMyEntity entity in offenders)
			{
					double distance = myCubeGrid.Distance_ShorterBounds(entity);
					if (distance < distClosest)
						distClosest = distance;
			}
			return distClosest;
		}

		#endregion
		#region Private Test Functions

		private HashSet<IMyEntity> EntitiesInLargeAABB(BoundingBoxD start, BoundingBoxD end)
		{
			List<Vector3D> PathPoints = new List<Vector3D>();
			PathPoints.Add(start.Min);
			PathPoints.Add(start.Max);
			PathPoints.Add(end.Min);
			PathPoints.Add(end.Max);
			BoundingBoxD PathAABB = BoundingBoxD.CreateFromPoints(PathPoints);
			myLogger.debugLog("Path AABB = " + PathAABB, "EntitiesInLargeAABB()");

			HashSet<IMyEntity> results = new HashSet<IMyEntity>();
			MyAPIGateway.Entities.GetEntitiesInAABB_Safe_NoBlock(PathAABB, results, collect_Entities);
			return results;
		}

		private ICollection<IMyEntity> SortByDistance(ICollection<IMyEntity> offenders)
		{
			SortedDictionary<float, IMyEntity> sortedOffenders = new SortedDictionary<float, IMyEntity>();
			foreach (IMyEntity entity in offenders)
			{
				CheckInterrupt();
				float distance = (float)myCubeGrid.Distance_ShorterBounds(entity);
				while (sortedOffenders.ContainsKey(distance))
					distance = distance.IncrementSignificand();
				sortedOffenders.Add(distance, entity);
			}
			return sortedOffenders.Values;
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

			if (entity.Physics != null && entity.Physics.Mass > 0 && entity.Physics.Mass < 1000)
				return false;

			return true;
		}

		/// <param name="offenders">entities to test</param>
		/// <param name="myGridShape">iff null, skip rejection test</param>
		private IMyEntity TestEntities(ICollection<IMyEntity> offenders, Capsule path, GridShapeProfiler myGridShape, out Vector3? pointOfObstruction, IMyCubeGrid GridDestination)
		{
			foreach (IMyEntity entity in offenders)
			{
				CheckInterrupt();
				myLogger.debugLog("testing offender: " + entity.getBestName() + " at " + entity.GetPosition(), "TestEntities()");

				IMyCubeGrid asGrid = entity as IMyCubeGrid;
				if (asGrid != null)
				{
					if (asGrid == GridDestination)
					{
						myLogger.debugLog("grid is destination: " + asGrid.DisplayName, "TestEntities()");
						continue;
					}

					if (!path.IntersectsAABB(entity))
					{
						myLogger.debugLog("no AABB intersection: " + asGrid.DisplayName, "TestEntities()");
						continue;
					}

					myLogger.debugLog("searching blocks of " + entity.getBestName(), "TestEntities()");
					uint cellCount = 0, cellRejectedCount = 0;

					// foreach block
					float GridSize = asGrid.GridSize;
					List<IMySlimBlock> allSlims = new List<IMySlimBlock>();
					asGrid.GetBlocks_Safe(allSlims);
					foreach (IMySlimBlock slim in allSlims)
					{
						bool blockIntersects = false;
						Vector3 cellPosWorld = new Vector3();
						//myLogger.debugLog("slim = " + slim.getBestName() + ", fat = " + slim.FatBlock + ", cell = " + slim.Position, "TestEntities()");
						//if (slim.FatBlock != null)
						//{
						//	myLogger.debugLog("fatblock min = " + slim.FatBlock.Min + ", fatblock max = " + slim.FatBlock.Max, "TestEntities()");
						//	//myLogger.debugLog("fatblock AABB min = " + slim.FatBlock.LocalAABB.Min + ", fatblock AABB max = " + slim.FatBlock.LocalAABB.Max, "TestEntities()");
						//}
						slim.ForEachCell((cell) => {
							CheckInterrupt();
							cellPosWorld = asGrid.GridIntegerToWorld(cell);
							cellCount++;

							//myLogger.debugLog("slim = " + slim.getBestName() + ", cell = " + cell + ", world position = " + cellPosWorld, "TestEntities()");
							// intersects capsule
							if (!path.Intersects(cellPosWorld, GridSize))
								return false;

							// rejection
							cellRejectedCount++;
							if (myGridShape == null || myGridShape.rejectionIntersects(RelativeVector3F.createFromWorldAbsolute(cellPosWorld, myCubeGrid), myCubeGrid.GridSize))
							{
								myLogger.debugLog("obstructing grid = " + asGrid.DisplayName + ", cell = " + cellPosWorld + ", block = " + slim.getBestName(), "TestEntities()", Logger.severity.DEBUG);
								blockIntersects = true;
								return true;
							}
							//myLogger.debugLog("rejection: no collision, cell = " + cellPosWorld + ", block = " + slim.getBestName(), "TestEntities()", Logger.severity.DEBUG);
							return false;
						});
						if (blockIntersects)
						{
							//myLogger.debugLog("closest point on line: {" + path.get_Line().From + ", " + path.get_Line().To + "} to " + cellPosWorld + " is " + path.get_Line().ClosestPoint(cellPosWorld), "TestEntities()");
							pointOfObstruction = path.get_Line().ClosestPoint(cellPosWorld);
							return entity;
						}
					}
					myLogger.debugLog("no obstruction for grid " + asGrid.DisplayName + ", tested " + cellCount + " against capsule and " + cellRejectedCount + " against rejection", "TestPath()");
					continue;
				}

				// not a grid
				if (IgnoreAsteroids && entity is IMyVoxelMap)
				{
					myLogger.debugLog("Ignoring asteroid: " + entity.getBestName(), "TestEntities()");
					continue;
				}

				myLogger.debugLog("not a grid, testing bounds", "TestEntities()");
				if (!path.IntersectsAABB(entity))
					continue;

				if (!path.IntersectsVolume(entity))
					continue;

				myLogger.debugLog("no more tests for non-grids are implemented", "TestEntities()", Logger.severity.DEBUG);
				//myLogger.debugLog("closest point on line: {" + path.get_Line().From + ", " + path.get_Line().To + "} to " + entity.GetCentre() + " is " + path.get_Line().ClosestPoint(entity.GetCentre()), "TestEntities()");
				pointOfObstruction = path.get_Line().ClosestPoint(entity.GetCentre());
				return entity;
			}

			myLogger.debugLog("no obstruction was found", "TestPath()", Logger.severity.DEBUG);
			pointOfObstruction = null;
			return null;
		}

		#endregion
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
