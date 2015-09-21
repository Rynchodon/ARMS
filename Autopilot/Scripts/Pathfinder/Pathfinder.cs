#define LOG_ENABLED // remove on build

using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRage.ModAPI;
using VRageMath;
using Rynchodon.Autopilot.NavigationSettings;
using Rynchodon.Settings;

namespace Rynchodon.Autopilot.Pathfinder
{
	/// <summary>
	/// Finds alternate paths if the current path is blocked.
	/// </summary>
	internal class Pathfinder
	{
		private readonly IMyCubeGrid CubeGrid;

		private NavSettings CNS;
		private Vector3D Destination;// { get; private set; }
		private Vector3D? Waypoint;// { get; private set; }
		private Vector3D WayDest
		{
			get
			{
				if (Waypoint != null) 
					return (Vector3D)Waypoint;
				return Destination;
			}
		}

		private IMyCubeGrid DestGrid;
		private IMyCubeBlock NavigationBlock;// { get; private set; }
		private bool IgnoreAsteroids;// { get; private set; }
		private NavSettings.SpecialFlying SpecialFyingInstructions;// { get; private set; }

		private PathChecker myPathChecker;
		private static ThreadManager PathFinderThread = new ThreadManager(ServerSettings.GetSetting<byte>(ServerSettings.SettingName.yParallelPathfinder));

		/// <summary>next time CheckPath() is allowed to run</summary>
		private DateTime nextRun = DateTime.MinValue;

		private Logger myLogger;

		public Pathfinder(IMyCubeGrid grid)
		{
			grid.throwIfNull_argument("grid");
			CubeGrid = grid;
			myPathChecker = new PathChecker(grid);
			myLogger = new Logger("Pathfinder", () => grid.DisplayName);
			myOutput = new PathfinderOutput(myPathChecker, PathfinderOutput.Result.Incomplete);
		}

		private PathfinderOutput myOutput;
		private FastResourceLock lock_myOutput = new FastResourceLock();

		private void SetOutput(PathfinderOutput newOutput)
		{
			if (myPathChecker != null && myPathChecker.Interrupt)
				return;
			using (lock_myOutput.AcquireExclusiveUsing())
				myOutput = newOutput;
		}

		public PathfinderOutput GetOutput()
		{
			using (lock_myOutput.AcquireSharedUsing())
			{
				PathfinderOutput temp = myOutput;
				if (temp.PathfinderResult != PathfinderOutput.Result.Incomplete)
					myOutput = new PathfinderOutput(myPathChecker, PathfinderOutput.Result.Incomplete);
				return temp;
			}
		}

		internal void Run(NavSettings CNS, IMyCubeBlock NavigationBlock, Rynchodon.Weapons.Engager myEngager)
		{
			if (!CNS.getWayDest(false).HasValue)
			{
				myLogger.debugLog("no destination", "Run()");
				return;
			}

			Vector3D destination = CNS.getWayDest(false).Value;
			Vector3D? waypoint = CNS.myWaypoint;
			bool ignoreAsteroids = CNS.ignoreAsteroids;
			NavSettings.SpecialFlying SpecialFyingInstructions = CNS.SpecialFlyingInstructions;
			if (waypoint == destination)
				waypoint = null;

			// if something has changed, interrupt previous
			if (this.Destination != destination || this.Waypoint != waypoint || this.NavigationBlock != NavigationBlock || this.IgnoreAsteroids != ignoreAsteroids || this.SpecialFyingInstructions != SpecialFyingInstructions || myOutput == null)
			{
				if (myPathChecker != null)
					myPathChecker.Interrupt = true;
				this.CNS = CNS;
				this.Destination = destination;
				this.Waypoint = waypoint;
				this.NavigationBlock = NavigationBlock;
				this.IgnoreAsteroids = ignoreAsteroids;
				this.SpecialFyingInstructions = SpecialFyingInstructions;

				// decide whether to use collision avoidance or slowdown
				this.DestGrid = null;
				switch (CNS.getTypeOfWayDest())
				{
					case NavSettings.TypeOfWayDest.BLOCK:
					case NavSettings.TypeOfWayDest.GRID:
						// hostile grids should always be avoided (slowdown not available)
						if (!CNS.target_locked)
						{
							// run slowdown. see Navigator.calcMoveAndRotate()
							this.DestGrid = CNS.CurrentGridDest.Grid;
						}
						break;
					case NavSettings.TypeOfWayDest.LAND:
					default:
						if (CNS.landingState != NavSettings.LANDING.OFF && CNS.CurrentGridDest != null)
							this.DestGrid = CNS.CurrentGridDest.Grid;
						break;
				}
			}

			// not a race, this function is only called from one thread
			if (DateTime.UtcNow < nextRun)
				return;
			nextRun = DateTime.MaxValue;

			PathFinderThread.EnqueueAction(Wrapper_CheckPath);
		}

		private void Wrapper_CheckPath()
		{
			try
			{ CheckPath(); }
			catch (InterruptException)
			{ myLogger.debugLog("Caught Interrupt", "Wrapper_CheckPath", Logger.severity.DEBUG); }
			catch (Exception other)
			{ myLogger.debugLog("Exception: " + other, "Wrapper_CheckPath", Logger.severity.ERROR); }
			finally
			{
				if (SpecialFyingInstructions == NavSettings.SpecialFlying.None && myOutput != null && myOutput.PathfinderResult == PathfinderOutput.Result.No_Way_Forward)
				{
					myLogger.debugLog("no way forward, delaying next run by 10 seconds", "Wrapper_CheckPath()");
					nextRun = DateTime.UtcNow + new TimeSpan(0, 0, 10);
				}
				else
					nextRun = DateTime.UtcNow + new TimeSpan(0, 0, 1);
				//myLogger.debugLog("next run is in: " + nextRun, "Wrapper_CheckPath");
			}
		}

		private float DistanceToDest;

		private void CheckPath()
		{
			Vector3? pointOfObstruction = null;
			Vector3D WayDest = Destination;
			Vector3D NavBlockPosition = NavigationBlock.GetPosition();
			DistanceToDest = (float)(Destination - NavBlockPosition).Length();

			IMyEntity ObstructingEntity = null;
			if (SpecialFyingInstructions == NavSettings.SpecialFlying.None || Waypoint == null)
			{
				myLogger.debugLog("testing path to destination", "CheckPath()");
				ObstructingEntity = myPathChecker.TestPath(Destination, NavigationBlock, IgnoreAsteroids, out pointOfObstruction, DestGrid);
				if (ObstructingEntity == null)
				{ 
					if (Waypoint == null)
					{
						myLogger.debugLog("Path to destination is clear", "CheckPath()", Logger.severity.DEBUG);
						SetOutput(new PathfinderOutput(myPathChecker, PathfinderOutput.Result.Path_Clear));
					}
					else
					{
						myLogger.debugLog("Re-routing to destination, path is now clear", "CheckPath()", Logger.severity.DEBUG);
						SetOutput(new PathfinderOutput(myPathChecker, PathfinderOutput.Result.Alternate_Path, null, Destination));
					}
					return;
				}
				CheckInterrupt();
				myLogger.debugLog("Path to destination is obstructed by " + ObstructingEntity.getBestName(), "CheckPath()", Logger.severity.DEBUG);
				IMyVoxelMap ObsAsVoxel = ObstructingEntity as IMyVoxelMap;
				if (ObsAsVoxel != null)
					myLogger.debugLog("Voxel AABB = " + ObsAsVoxel.WorldAABB + ", Volume = " + ObsAsVoxel.WorldVolume, "CheckPath()");
			}

			if (Waypoint != null)
			{
				WayDest = (Vector3)Waypoint;
				myLogger.debugLog("testing path to waypoint", "CheckPath()");
				ObstructingEntity = myPathChecker.TestPath((Vector3D)Waypoint, NavigationBlock, IgnoreAsteroids, out pointOfObstruction, DestGrid);
				if (ObstructingEntity == null)
				{
					myLogger.debugLog("Path to waypoint is clear", "CheckPath()", Logger.severity.DEBUG);
					SetOutput(new PathfinderOutput(myPathChecker, PathfinderOutput.Result.Path_Clear));
					return;
				}
			}

			if (ObstructingEntity == null)
			{
				myLogger.debugLog("Neither Destination nor Waypoint were tested", "CheckPath()", Logger.severity.DEBUG);
				return;
			}

			CheckInterrupt();
			switch (SpecialFyingInstructions)
			{
				case NavSettings.SpecialFlying.Line_Any:
				case NavSettings.SpecialFlying.Line_SidelForward:
					{
						if (CanMoveInDirection(NavBlockPosition, WayDest - NavBlockPosition, "forward"))
							return;

						myLogger.debugLog("NoAlternateRoute, Obstruction = " + ObstructingEntity.getBestName(), "CheckPath()", Logger.severity.DEBUG);
						SetOutput(new PathfinderOutput(myPathChecker, PathfinderOutput.Result.No_Way_Forward, ObstructingEntity));
						return;
					}
			}

			SetOutput(new PathfinderOutput(myPathChecker, PathfinderOutput.Result.Searching_Alt, ObstructingEntity));
			myLogger.debugLog("Path to Way/Dest is obstructed by " + ObstructingEntity.getBestName() + " at " + pointOfObstruction, "CheckPath()", Logger.severity.TRACE);

			Vector3 lineToWayDest = WayDest - CubeGrid.GetCentre();
			Vector3 newPath_v1, newPath_v2;
			lineToWayDest.CalculatePerpendicularVector(out newPath_v1);
			newPath_v2 = lineToWayDest.Cross(newPath_v1);
			newPath_v1 = Vector3.Normalize(newPath_v1);
			newPath_v2 = Vector3.Normalize(newPath_v2);
			Vector3[] NewPathVectors = { newPath_v1, newPath_v2, Vector3.Negate(newPath_v1), Vector3.Negate(newPath_v2) };

			for (int newPathDistance = 8; newPathDistance < 10000; newPathDistance *= 2)
			{
				myLogger.debugLog("newPathDistance = " + newPathDistance, "CheckPath()", Logger.severity.TRACE);

				// check far enough & sort alternate paths
				SortedDictionary<float, Vector3> Alternate_Path = new SortedDictionary<float, Vector3>();
				foreach (Vector3 PathVector in NewPathVectors)
				{
					pointOfObstruction.throwIfNull_variable("pointOfObstruction");
					Vector3 Alternate = (Vector3)pointOfObstruction + newPathDistance * PathVector;
					myLogger.debugLog("Alternate = " + Alternate, "CheckPath()");
					CNS.throwIfNull_variable("CNS");
					if (!CNS.waypointFarEnough(Alternate))
					{
						myLogger.debugLog("waypoint is too close, throwing out: " + Alternate, "CheckPath()");
						continue;
					}
					float distanceFromDest = Vector3.Distance(Destination, Alternate);
					Alternate_Path.throwIfNull_variable("Alternate_Path");
					while (Alternate_Path.ContainsKey(distanceFromDest))
						distanceFromDest = distanceFromDest.IncrementSignificand();
					Alternate_Path.Add(distanceFromDest, Alternate);

					myLogger.debugLog("Added alt path: " + Alternate + " with distance of " + distanceFromDest, "CheckPath()");
				}

				foreach (Vector3 AlternatePoint in Alternate_Path.Values)
					if (TestAltPath(AlternatePoint))
						return;
			}

			// before going to No Way Forward, check if we can move around
			// try moving forward
			Vector3D GridCentre = CubeGrid.GetCentre();
			Vector3 Forward = NavigationBlock.WorldMatrix.Forward;
			if (CanMoveInDirection(GridCentre, Forward, "forward"))
				return;
			// try moving backward
			Vector3 Backward = NavigationBlock.WorldMatrix.Backward;
			if (CanMoveInDirection(GridCentre, Backward, "backward"))
				return;

			SetOutput(new PathfinderOutput(myPathChecker, PathfinderOutput.Result.No_Way_Forward, ObstructingEntity));
			myLogger.debugLog("No Way Forward: " + ObstructingEntity.getBestName(), "CheckPath()", Logger.severity.INFO);
		}

		private bool CanMoveInDirection(Vector3D NavBlockPos, Vector3D direction, string dirName, bool countDown = true)
		{
			direction = Vector3D.Normalize(direction);

			int distance;
			if (countDown)
				distance = 128;
			else
				distance = 2;

			while (true)
			{
				Vector3 testPoint = NavBlockPos + distance * direction;
				if (!CNS.waypointFarEnough(testPoint))
				{
					myLogger.debugLog(dirName + " too close: distance = " + distance + ", testPoint = " + testPoint, "CanMoveInDirection()", Logger.severity.TRACE);
					return false;
				}
				myLogger.debugLog("testing " + dirName + ": distance = " + distance + ", testPoint = " + testPoint, "CanMoveInDirection()", Logger.severity.TRACE);
				if (TestAltPath(testPoint, false))
					return true;

				if (countDown)
				{
					distance /= 2;
					if (distance < 2)
						break;
				}
				else
				{
					distance *= 2;
					if (distance > 128)
						break;
				}
			}

			return false;
		}

		///<summary>Test if a path can be flown. From current position to AlternatePoint</summary>
		/// <returns>true iff the path is flyable</returns>
		private bool TestAltPath(Vector3 AlternatePoint, bool usefulTest = true)
		{
			myLogger.debugLog("testing alternate path: " + AlternatePoint, "TestAltPath()");
			Vector3? alt_pointOfObstruction;
			IMyEntity Alt_ObstructingEntity = myPathChecker.TestPath(AlternatePoint, NavigationBlock, IgnoreAsteroids, out alt_pointOfObstruction, DestGrid);
			if (Alt_ObstructingEntity == null) // found a new path
			{
				CheckInterrupt();

				if (usefulTest)
				{
					// Is this a useful path; can we get closer to the destination by flying it?
					float pathwiseDistanceToDest = myPathChecker.distanceCanTravel(new Line(AlternatePoint, Destination, false));
					CheckInterrupt();

					if (pathwiseDistanceToDest >= DistanceToDest + CNS.destinationRadius)
					{
						myLogger.debugLog("path is unobstructed but not useful enough. distance to dest = " + DistanceToDest + ", pathwise = " + pathwiseDistanceToDest, "TestAltPath()", Logger.severity.TRACE);
						CheckInterrupt();
						return false;
					}
					myLogger.debugLog("path is useful. distance to dest = " + DistanceToDest + ", pathwise = " + pathwiseDistanceToDest, "TestAltPath()", Logger.severity.TRACE);
				}

				SetOutput(new PathfinderOutput(myPathChecker, PathfinderOutput.Result.Alternate_Path, null, AlternatePoint));
				myLogger.debugLog("Found a new path: " + AlternatePoint, "CheckPath()", Logger.severity.DEBUG);
				return true;
			}
			myLogger.debugLog("Alternate path is obstructed by " + Alt_ObstructingEntity.getBestName() + " at " + alt_pointOfObstruction, "TestAltPath()", Logger.severity.TRACE);
			CheckInterrupt();
			return false;
		}

		#region Interrupt

		/// <summary>
		/// throws an InterruptException if Interrupt
		/// </summary>
		private void CheckInterrupt()
		{
			if (myPathChecker.Interrupt)
				throw new InterruptException();
		}

		#endregion
	}
}
