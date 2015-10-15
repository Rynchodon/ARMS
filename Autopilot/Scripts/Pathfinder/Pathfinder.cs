using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRage.ModAPI;
using VRageMath;
using Rynchodon.Autopilot.Data;
using Rynchodon.Settings;
using Rynchodon.Threading;

namespace Rynchodon.Autopilot.Pathfinder
{
	/// <summary>
	/// Finds alternate paths if the current path is blocked.
	/// </summary>
	internal class Pathfinder
	{
		private static readonly ThreadManager Thread_High = new ThreadManager(ServerSettings.GetSetting<byte>(ServerSettings.SettingName.yParallelPathfinder), false, "Path_High");
		private static readonly ThreadManager Thread_Low = new ThreadManager(ServerSettings.GetSetting<byte>(ServerSettings.SettingName.yParallelPathfinder), true, "Path_Low");

		private readonly Logger m_logger;
		private readonly IMyCubeGrid m_grid;
		private readonly PathChecker m_pathChecker;
		private readonly AllNavigationSettings m_navSet;
		private readonly FastResourceLock lock_update = new FastResourceLock("Path_lock_update");
		private readonly FastResourceLock lock_myOutput = new FastResourceLock("Path_lock_myOutput");

		private Action m_pathHigh;
		private readonly LockedQueue<Action> m_pathLow = new LockedQueue<Action>();

		//#region Variables from m_navSet

		//private PseudoBlock m_navigationBlock;
		//private bool m_ignoreAsteroid;

		//#endregion

		//private PathChecker m_pathChecker; // using multiple now
		//private PathfinderOutput m_output;
		///// <summary>next time CheckPath() is allowed to run</summary>
		//private DateTime m_nextRun = DateTime.MinValue;

		//public PathfinderOutput Output
		//{
		//	get
		//	{
		//		using (lock_myOutput.AcquireSharedUsing())
		//		{
		//			PathfinderOutput temp = m_output;
		//			if (temp.PathfinderResult != PathfinderOutput.Result.Incomplete)
		//				m_output = new PathfinderOutput(PathfinderOutput.Result.Incomplete);
		//			return temp;
		//		}
		//	}
		//	private set
		//	{
		//		if (m_pathChecker != null && m_pathChecker.Interrupt)
		//			return;
		//		using (lock_myOutput.AcquireExclusiveUsing())
		//			m_output = value;
		//	}
		//}

		public Pathfinder(IMyCubeGrid grid, AllNavigationSettings navSet)
		{
			grid.throwIfNull_argument("grid");
			m_grid = grid;
			m_logger = new Logger("Pathfinder", () => grid.DisplayName);
			m_pathChecker = new PathChecker(grid);
			m_navSet = navSet;
		}

		public void Update()
		{
			if (m_pathHigh != null || m_pathLow.Count != 0)
			{
				m_logger.debugLog("already running", "Update()");
				return;
			}

			PseudoBlock navBlock = m_navSet.Settings_Current.NavigationBlock;
			bool ignoreAsteroid = m_navSet.Settings_Current.IgnoreAsteroid;

			// given velocity and distance, calculate destination
			float distance = m_navSet.Settings_Current.Distance;
			if (float.IsNaN(distance))
			{
				m_logger.debugLog("distance is NaN", "Update()");
				return;
			}
			Vector3 direction = m_grid.Physics.LinearVelocity;
			if (direction.LengthSquared() < 1)
			{
				direction = m_grid.Physics.LinearAcceleration;
				if (direction.LengthSquared() < 1)
				{
					m_logger.debugLog("not moving, velocity: " + m_grid.Physics.LinearVelocity + ", accel: " + m_grid.Physics.LinearAcceleration, "Update()");
					return;
				}
			}
			direction.Normalize();

			Vector3D destination = navBlock.WorldPosition + direction * distance;

			//Vector3D destination = m_navSet.Settings_Current.Destination;
			//if (!destination.IsValid())
			//{
			//	m_logger.debugLog("destination is not valid", "Update()");
			//	return;
			//}

			EnqueueAction(() => TestPath(navBlock, destination, ignoreAsteroid), true);
			NextAction();
		}

		private void TestPath(PseudoBlock navBlock,  Vector3D destination, bool ignoreAsteroid)
		{
			if (m_pathChecker.TestFast(navBlock, destination, ignoreAsteroid))
			{
				m_logger.debugLog("path is clear (fast)", "TestPath()", Logger.severity.DEBUG);
				// TODO: let someone know
				return;
			}
			IMyEntity obstructing;
			Vector3? pointOfObstruction;
			if (m_pathChecker.TestSlow(out obstructing, out pointOfObstruction))
			{
				m_logger.debugLog("path is clear (slow)", "TestPath()", Logger.severity.DEBUG);
				// TODO: let someone know
				return;
			}

			m_logger.debugLog("path is blocked by " + obstructing.getBestName() + " at " + pointOfObstruction, "TestPath()", Logger.severity.TRACE);
			Logger.debugNotify("Path blocked", 50);
			// TODO: something useful
		}

		/// <summary>
		/// Adds an item to m_pathHigh or m_pathLow.
		/// </summary>
		private void EnqueueAction(Action act, bool highPriority = false)
		{
			Action wrapped = () => {
				try
				{ act.Invoke(); }
				//catch (InterruptException)
				//{
				//	m_logger.debugLog("Pathfinder interrupted", "EnqueueAction()", Logger.severity.INFO);
				//	return;
				//}
				catch (Exception ex)
				{
					m_logger.alwaysLog("Exception: " + ex, "EnqueueAction()", Logger.severity.ERROR);
					// TODO: fail-safe plan
					return;
				}
				finally { lock_update.ReleaseExclusive(); }
				NextAction();
			};

			if (highPriority)
				m_pathHigh = wrapped;
			else
				m_pathLow.Enqueue(wrapped);
		}

		/// <summary>
		/// Transfers one item from either m_pathHigh or m_pathLow to Thread_High or Thread_Low respectively.
		/// </summary>
		private void NextAction()
		{
			if (lock_update.TryAcquireExclusive())
			{
				if (m_pathHigh != null)
				{
					//m_logger.debugLog("transfering action to Thread_High", "NextAction()");
					Thread_High.EnqueueAction(m_pathHigh);
				}
				else if (m_pathLow.Count != 0)
				{
					//m_logger.debugLog("transfering action to Thread_Low", "NextAction()");
					Action act = m_pathLow.Dequeue();
					Thread_Low.EnqueueAction(act);
				}
				else
				{
					//m_logger.debugLog("nothing to do", "NextAction()");
					lock_update.ReleaseExclusive();
				}
			}
			//else
			//	m_logger.debugLog("already running, not transfering action", "NextAction()");
		}

		//internal void Run(NavSettings CNS, IMyCubeBlock NavigationBlock, Rynchodon.Weapons.Engager myEngager)
		//{
		//	if (!CNS.getWayDest(false).HasValue)
		//	{
		//		m_logger.debugLog("no destination", "Run()");
		//		return;
		//	}

		//	Vector3D destination = CNS.getWayDest(false).Value;
		//	Vector3D? waypoint = CNS.myWaypoint;
		//	bool ignoreAsteroids = CNS.ignoreAsteroids;
		//	NavSettings.SpecialFlying SpecialFyingInstructions = CNS.SpecialFlyingInstructions;
		//	if (waypoint == destination)
		//		waypoint = null;

		//	// if something has changed, interrupt previous
		//	if (this.Destination != destination || this.Waypoint != waypoint || this.m_navigationBlock != NavigationBlock || this.m_ignoreAsteroid != ignoreAsteroids || this.SpecialFyingInstructions != SpecialFyingInstructions || m_output == null)
		//	{
		//		if (m_pathChecker != null)
		//			m_pathChecker.Interrupt = true;
		//		this.CNS = CNS;
		//		this.Destination = destination;
		//		this.Waypoint = waypoint;
		//		this.m_navigationBlock = NavigationBlock;
		//		this.m_ignoreAsteroid = ignoreAsteroids;
		//		this.SpecialFyingInstructions = SpecialFyingInstructions;

		//		// decide whether to use collision avoidance or slowdown
		//		this.DestGrid = null;
		//		switch (CNS.getTypeOfWayDest())
		//		{
		//			case NavSettings.TypeOfWayDest.BLOCK:
		//			case NavSettings.TypeOfWayDest.GRID:
		//				// hostile grids should always be avoided (slowdown not available)
		//				if (!CNS.target_locked)
		//				{
		//					// run slowdown. see Navigator.calcMoveAndRotate()
		//					this.DestGrid = CNS.CurrentGridDest.Grid;
		//				}
		//				break;
		//			case NavSettings.TypeOfWayDest.LAND:
		//			default:
		//				if (CNS.landingState != NavSettings.LANDING.OFF && CNS.CurrentGridDest != null)
		//					this.DestGrid = CNS.CurrentGridDest.Grid;
		//				break;
		//		}
		//	}

		//	// not a race, this function is only called from one thread
		//	if (DateTime.UtcNow < m_nextRun)
		//		return;
		//	m_nextRun = DateTime.MaxValue;

		//	PathFinderThread.EnqueueAction(Wrapper_CheckPath);
		//}

		//private void Wrapper_CheckPath()
		//{
		//	try
		//	{ CheckPath(); }
		//	catch (InterruptException)
		//	{ m_logger.debugLog("Caught Interrupt", "Wrapper_CheckPath", Logger.severity.DEBUG); }
		//	catch (Exception other)
		//	{ m_logger.debugLog("Exception: " + other, "Wrapper_CheckPath", Logger.severity.ERROR); }
		//	finally
		//	{
		//		if (SpecialFyingInstructions == NavSettings.SpecialFlying.None && m_output != null && m_output.PathfinderResult == PathfinderOutput.Result.No_Way_Forward)
		//		{
		//			m_logger.debugLog("no way forward, delaying next run by 10 seconds", "Wrapper_CheckPath()");
		//			m_nextRun = DateTime.UtcNow + new TimeSpan(0, 0, 10);
		//		}
		//		else
		//			m_nextRun = DateTime.UtcNow + new TimeSpan(0, 0, 1);
		//		//myLogger.debugLog("next run is in: " + nextRun, "Wrapper_CheckPath");
		//	}
		//}

		//private float DistanceToDest;

		//private void CheckPath()
		//{
		//	Vector3? pointOfObstruction = null;
		//	Vector3D WayDest = Destination;
		//	Vector3D NavBlockPosition = m_navigationBlock.GetPosition();
		//	DistanceToDest = (float)(Destination - NavBlockPosition).Length();

		//	IMyEntity ObstructingEntity = null;
		//	if (SpecialFyingInstructions == NavSettings.SpecialFlying.None || Waypoint == null)
		//	{
		//		m_logger.debugLog("testing path to destination", "CheckPath()");
		//		ObstructingEntity = m_pathChecker.TestPath(Destination, m_navigationBlock, m_ignoreAsteroid, out pointOfObstruction, DestGrid);
		//		if (ObstructingEntity == null)
		//		{ 
		//			if (Waypoint == null)
		//			{
		//				m_logger.debugLog("Path to destination is clear", "CheckPath()", Logger.severity.DEBUG);
		//				SetOutput(new PathfinderOutput(m_pathChecker, PathfinderOutput.Result.Path_Clear));
		//			}
		//			else
		//			{
		//				m_logger.debugLog("Re-routing to destination, path is now clear", "CheckPath()", Logger.severity.DEBUG);
		//				SetOutput(new PathfinderOutput(m_pathChecker, PathfinderOutput.Result.Alternate_Path, null, Destination));
		//			}
		//			return;
		//		}
		//		CheckInterrupt();
		//		m_logger.debugLog("Path to destination is obstructed by " + ObstructingEntity.getBestName(), "CheckPath()", Logger.severity.DEBUG);
		//		IMyVoxelMap ObsAsVoxel = ObstructingEntity as IMyVoxelMap;
		//		if (ObsAsVoxel != null)
		//			m_logger.debugLog("Voxel AABB = " + ObsAsVoxel.WorldAABB + ", Volume = " + ObsAsVoxel.WorldVolume, "CheckPath()");
		//	}

		//	if (Waypoint != null)
		//	{
		//		WayDest = (Vector3)Waypoint;
		//		m_logger.debugLog("testing path to waypoint", "CheckPath()");
		//		ObstructingEntity = m_pathChecker.TestPath((Vector3D)Waypoint, m_navigationBlock, m_ignoreAsteroid, out pointOfObstruction, DestGrid);
		//		if (ObstructingEntity == null)
		//		{
		//			m_logger.debugLog("Path to waypoint is clear", "CheckPath()", Logger.severity.DEBUG);
		//			SetOutput(new PathfinderOutput(m_pathChecker, PathfinderOutput.Result.Path_Clear));
		//			return;
		//		}
		//	}

		//	if (ObstructingEntity == null)
		//	{
		//		m_logger.debugLog("Neither Destination nor Waypoint were tested", "CheckPath()", Logger.severity.DEBUG);
		//		return;
		//	}

		//	CheckInterrupt();
		//	switch (SpecialFyingInstructions)
		//	{
		//		case NavSettings.SpecialFlying.Line_Any:
		//		case NavSettings.SpecialFlying.Line_SidelForward:
		//			{
		//				if (CanMoveInDirection(NavBlockPosition, WayDest - NavBlockPosition, "forward"))
		//					return;

		//				m_logger.debugLog("NoAlternateRoute, Obstruction = " + ObstructingEntity.getBestName(), "CheckPath()", Logger.severity.DEBUG);
		//				SetOutput(new PathfinderOutput(m_pathChecker, PathfinderOutput.Result.No_Way_Forward, ObstructingEntity));
		//				return;
		//			}
		//	}

		//	SetOutput(new PathfinderOutput(m_pathChecker, PathfinderOutput.Result.Searching_Alt, ObstructingEntity));
		//	m_logger.debugLog("Path to Way/Dest is obstructed by " + ObstructingEntity.getBestName() + " at " + pointOfObstruction, "CheckPath()", Logger.severity.TRACE);

		//	Vector3 lineToWayDest = WayDest - m_grid.GetCentre();
		//	Vector3 newPath_v1, newPath_v2;
		//	lineToWayDest.CalculatePerpendicularVector(out newPath_v1);
		//	newPath_v2 = lineToWayDest.Cross(newPath_v1);
		//	newPath_v1 = Vector3.Normalize(newPath_v1);
		//	newPath_v2 = Vector3.Normalize(newPath_v2);
		//	Vector3[] NewPathVectors = { newPath_v1, newPath_v2, Vector3.Negate(newPath_v1), Vector3.Negate(newPath_v2) };

		//	for (int newPathDistance = 8; newPathDistance < 10000; newPathDistance *= 2)
		//	{
		//		m_logger.debugLog("newPathDistance = " + newPathDistance, "CheckPath()", Logger.severity.TRACE);

		//		// check far enough & sort alternate paths
		//		SortedDictionary<float, Vector3> Alternate_Path = new SortedDictionary<float, Vector3>();
		//		foreach (Vector3 PathVector in NewPathVectors)
		//		{
		//			pointOfObstruction.throwIfNull_variable("pointOfObstruction");
		//			Vector3 Alternate = (Vector3)pointOfObstruction + newPathDistance * PathVector;
		//			m_logger.debugLog("Alternate = " + Alternate, "CheckPath()");
		//			CNS.throwIfNull_variable("CNS");
		//			if (!CNS.waypointFarEnough(Alternate))
		//			{
		//				m_logger.debugLog("waypoint is too close, throwing out: " + Alternate, "CheckPath()");
		//				continue;
		//			}
		//			float distanceFromDest = Vector3.Distance(Destination, Alternate);
		//			Alternate_Path.throwIfNull_variable("Alternate_Path");
		//			while (Alternate_Path.ContainsKey(distanceFromDest))
		//				distanceFromDest = distanceFromDest.IncrementSignificand();
		//			Alternate_Path.Add(distanceFromDest, Alternate);

		//			m_logger.debugLog("Added alt path: " + Alternate + " with distance of " + distanceFromDest, "CheckPath()");
		//		}

		//		foreach (Vector3 AlternatePoint in Alternate_Path.Values)
		//			if (TestAltPath(AlternatePoint))
		//				return;
		//	}

		//	// before going to No Way Forward, check if we can move around
		//	// try moving forward
		//	Vector3D GridCentre = m_grid.GetCentre();
		//	Vector3 Forward = m_navigationBlock.WorldMatrix.Forward;
		//	if (CanMoveInDirection(GridCentre, Forward, "forward"))
		//		return;
		//	// try moving backward
		//	Vector3 Backward = m_navigationBlock.WorldMatrix.Backward;
		//	if (CanMoveInDirection(GridCentre, Backward, "backward"))
		//		return;

		//	SetOutput(new PathfinderOutput(m_pathChecker, PathfinderOutput.Result.No_Way_Forward, ObstructingEntity));
		//	m_logger.debugLog("No Way Forward: " + ObstructingEntity.getBestName(), "CheckPath()", Logger.severity.INFO);
		//}

		//private bool CanMoveInDirection(Vector3D NavBlockPos, Vector3D direction, string dirName, bool countDown = true)
		//{
		//	direction = Vector3D.Normalize(direction);

		//	int distance;
		//	if (countDown)
		//		distance = 128;
		//	else
		//		distance = 2;

		//	while (true)
		//	{
		//		Vector3 testPoint = NavBlockPos + distance * direction;
		//		if (!CNS.waypointFarEnough(testPoint))
		//		{
		//			m_logger.debugLog(dirName + " too close: distance = " + distance + ", testPoint = " + testPoint, "CanMoveInDirection()", Logger.severity.TRACE);
		//			return false;
		//		}
		//		m_logger.debugLog("testing " + dirName + ": distance = " + distance + ", testPoint = " + testPoint, "CanMoveInDirection()", Logger.severity.TRACE);
		//		if (TestAltPath(testPoint, false))
		//			return true;

		//		if (countDown)
		//		{
		//			distance /= 2;
		//			if (distance < 2)
		//				break;
		//		}
		//		else
		//		{
		//			distance *= 2;
		//			if (distance > 128)
		//				break;
		//		}
		//	}

		//	return false;
		//}

		/////<summary>Test if a path can be flown. From current position to AlternatePoint</summary>
		///// <returns>true iff the path is flyable</returns>
		//private bool TestAltPath(Vector3 AlternatePoint, bool usefulTest = true)
		//{
		//	m_logger.debugLog("testing alternate path: " + AlternatePoint, "TestAltPath()");
		//	Vector3? alt_pointOfObstruction;
		//	IMyEntity Alt_ObstructingEntity = m_pathChecker.TestPath(AlternatePoint, m_navigationBlock, m_ignoreAsteroid, out alt_pointOfObstruction, DestGrid);
		//	if (Alt_ObstructingEntity == null) // found a new path
		//	{
		//		CheckInterrupt();

		//		if (usefulTest)
		//		{
		//			// Is this a useful path; can we get closer to the destination by flying it?
		//			float pathwiseDistanceToDest = m_pathChecker.distanceCanTravel(new Line(AlternatePoint, Destination, false));
		//			CheckInterrupt();

		//			if (pathwiseDistanceToDest >= DistanceToDest + CNS.destinationRadius)
		//			{
		//				m_logger.debugLog("path is unobstructed but not useful enough. distance to dest = " + DistanceToDest + ", pathwise = " + pathwiseDistanceToDest, "TestAltPath()", Logger.severity.TRACE);
		//				CheckInterrupt();
		//				return false;
		//			}
		//			m_logger.debugLog("path is useful. distance to dest = " + DistanceToDest + ", pathwise = " + pathwiseDistanceToDest, "TestAltPath()", Logger.severity.TRACE);
		//		}

		//		SetOutput(new PathfinderOutput(m_pathChecker, PathfinderOutput.Result.Alternate_Path, null, AlternatePoint));
		//		m_logger.debugLog("Found a new path: " + AlternatePoint, "CheckPath()", Logger.severity.DEBUG);
		//		return true;
		//	}
		//	m_logger.debugLog("Alternate path is obstructed by " + Alt_ObstructingEntity.getBestName() + " at " + alt_pointOfObstruction, "TestAltPath()", Logger.severity.TRACE);
		//	CheckInterrupt();
		//	return false;
		//}

		//#region Interrupt

		///// <summary>
		///// throws an InterruptException if Interrupt
		///// </summary>
		//private void CheckInterrupt()
		//{
		//	if (m_pathChecker.Interrupt)
		//		throw new InterruptException();
		//}

		//#endregion
	}
}
