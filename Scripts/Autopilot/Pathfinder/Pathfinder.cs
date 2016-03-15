using System;
using System.Collections.Generic;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Rynchodon.Autopilot.Navigator;
using Rynchodon.Settings;
using Rynchodon.Threading;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinder
{
	/// <summary>
	/// Finds alternate paths if the current path is blocked.
	/// </summary>
	public class Pathfinder
	{

		private enum PathState : byte { Not_Running, No_Obstruction, Searching, Path_Blocked }

		private const float SqRtHalf = 0.70710678f;
		private const float SqRtThird = 0.57735026f;
		private const float LookAheadSpeed_Seconds = 3f;
		private const int AlternatesToCheck = 25;

		#region Base25Directions
		/// <remarks>
		/// <para>Why not use Base27Directions? Because it contains 64 directions...</para>
		/// <para>Does not contain zero or forward.</para>
		/// </remarks>
		private static Vector3[] Base25Directions = new Vector3[]{
			#region Forward
			new Vector3(0f, SqRtHalf, -SqRtHalf),
			new Vector3(SqRtThird, SqRtThird, -SqRtThird),
			new Vector3(SqRtHalf, 0f, -SqRtHalf),
			new Vector3(SqRtThird, -SqRtThird, -SqRtThird),
			new Vector3(0f, -SqRtHalf, -SqRtHalf),
			new Vector3(-SqRtThird, -SqRtThird, -SqRtThird),
			new Vector3(-SqRtHalf, 0f, -SqRtHalf),
			new Vector3(-SqRtThird, SqRtThird, -SqRtThird),
			#endregion

			new Vector3(0f, 1f, 0f),
			new Vector3(SqRtHalf, SqRtHalf, 0f),
			new Vector3(1f, 0f, 0f),
			new Vector3(SqRtHalf, -SqRtHalf, 0f),
			new Vector3(0f, -1f, 0f),
			new Vector3(-SqRtHalf, -SqRtHalf, 0f),
			new Vector3(-1f, 0f, 0f),
			new Vector3(-SqRtHalf, SqRtHalf, 0f),

			#region Backward
			new Vector3(0f, SqRtHalf, SqRtHalf),
			new Vector3(SqRtThird, SqRtThird, SqRtThird),
			new Vector3(SqRtHalf, 0f, SqRtHalf),
			new Vector3(SqRtThird, -SqRtThird, SqRtThird),
			new Vector3(0f, -SqRtHalf, SqRtHalf),
			new Vector3(-SqRtThird, -SqRtThird, SqRtThird),
			new Vector3(-SqRtHalf, 0f, SqRtHalf),
			new Vector3(-SqRtThird, SqRtThird, SqRtThird),
			new Vector3(0f, 0f, 1f)
			#endregion
		};
		#endregion

		private static ThreadManager Thread_High = new ThreadManager(1, false, "Path_High");
		private static ThreadManager Thread_Low = new ThreadManager(ServerSettings.GetSetting<byte>(ServerSettings.SettingName.yParallelPathfinder), true, "Path_Low");

		static Pathfinder()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			Thread_High = null;
			Thread_Low = null;
			Base25Directions = null;
		}

		private readonly Logger m_logger;
		private readonly IMyCubeGrid m_grid;
		private readonly PathChecker m_pathChecker;
		private readonly RotateChecker m_rotateChecker;
		private readonly AllNavigationSettings m_navSet;
		private readonly Mover m_mover;
		private readonly FastResourceLock lock_testPath = new FastResourceLock("Path_lock_path");
		//private readonly FastResourceLock lock_testRotate = new FastResourceLock("Path_lock_rotate");

		private readonly LockedQueue<Action> m_pathHigh = new LockedQueue<Action>();
		private readonly LockedQueue<Action> m_pathLow = new LockedQueue<Action>();

		private PseudoBlock m_navBlock;
		private Vector3 m_destination;
		private bool m_ignoreAsteroid, m_landing, m_canChangeCourse;

		private PathState m_pathState = PathState.Not_Running;
		private PathState m_rotateState = PathState.Not_Running;

		private ulong m_nextRunPath;
		private ulong m_nextRunRotate;
		private byte m_runId;

		/// <summary>Number of alternates tried, count starts after the first alternate is found that is better than base.</summary>
		public int m_altPath_AlternatesFound;
		/// <summary>How close a path can get the ship to its true destination.</summary>
		public float m_altPath_ClosenessToDest;
		/// <summary>The best waypoint found so far.</summary>
		public Vector3 m_altPath_Waypoint;

		public bool CanMove { get { return m_pathState == PathState.No_Obstruction; } }
		public bool CanRotate { get { return m_rotateState == PathState.No_Obstruction; } }
		public string PathStatus { get { return m_pathState.ToString(); } }
		public IMyEntity RotateObstruction { get; private set; }
		public IMyEntity MoveObstruction { get; private set; }

		public Pathfinder(IMyCubeGrid grid, AllNavigationSettings navSet, Mover mover)
		{
			grid.throwIfNull_argument("grid");
			m_grid = grid;
			m_logger = new Logger("Pathfinder", () => grid.DisplayName, () => m_pathState.ToString(), () => m_rotateState.ToString());
			m_pathChecker = new PathChecker(grid);
			m_rotateChecker = new RotateChecker(grid);
			m_navSet = navSet;
			m_mover = mover;
			m_logger.debugLog("Initialized, grid: " + grid.DisplayName, "Pathfinder()");
		}

		public void TestPath(Vector3D destination, bool landing)
		{
			if (m_navSet.Settings_Current.DestinationChanged)
			{
				m_logger.debugLog("new destination: " + destination, "TestPath()", Logger.severity.INFO);
				m_navSet.Settings_Task_NavWay.DestinationChanged = false;
				m_runId++;
				m_pathLow.Clear();
				ClearAltPath();
				m_pathState = PathState.Not_Running;
			}
			//else
			//	m_logger.debugLog("destination unchanged", "TestPath()");

			if (Globals.UpdateCount < m_nextRunPath)
				return;
			m_nextRunPath = Globals.UpdateCount + 10ul;

			if (m_pathLow.Count != 0)
			{
				m_logger.debugLog("path low is running", "TestPath()");
				return;
			}

			m_navBlock = m_navSet.Settings_Current.NavigationBlock;
			m_destination = destination;
			m_ignoreAsteroid = m_navSet.Settings_Current.IgnoreAsteroid;
			m_landing = landing;
			m_canChangeCourse = m_navSet.Settings_Current.PathfinderCanChangeCourse;
			MyEntity destEntity = m_navSet.Settings_Current.DestinationEntity as MyEntity;
			m_logger.debugLog("DestinationEntity: " + m_navSet.Settings_Current.DestinationEntity.getBestName(), "TestPath()");
			byte runId = m_runId;

			const float minimumDistance = 100f;
			const float minDistSquared = minimumDistance * minimumDistance;
			const float seconds = 10f;
			const float distOverSeconds = minimumDistance / seconds;

			Vector3 displacement = destination - m_navBlock.WorldPosition;
			float distanceSquared = displacement.LengthSquared();
			float testDistance;
			Vector3 move_direction = m_grid.Physics.LinearVelocity;
			float speedSquared = move_direction.LengthSquared();
			if (distanceSquared > minDistSquared)
			{
				// only look ahead 10 s / 100 m
				testDistance = speedSquared < distOverSeconds ? minimumDistance : (float)Math.Sqrt(speedSquared) * seconds;
				if (testDistance * testDistance < distanceSquared)
				{
					Vector3 direction = displacement / (float)Math.Sqrt(distanceSquared);
					destination = m_navBlock.WorldPosition + testDistance * direction;
					m_logger.debugLog("moved destination: " + destination + ", distance: " + testDistance + ", direction: " + direction, "TestPath()");
				}
			}
			else
				m_logger.debugLog("using actual destination: " + destination, "TestPath()");

			m_pathHigh.Enqueue(() => TestPath(destination, destEntity, runId, isAlternate: false, tryAlternates: true));

			// given velocity and distance, calculate destination
			if (speedSquared > 1f)
			{
				Vector3D moveDest = m_navBlock.WorldPosition + move_direction * LookAheadSpeed_Seconds;
				m_pathHigh.Enqueue(() => TestPath(moveDest, destEntity, runId, isAlternate: false, tryAlternates: false));
				m_pathHigh.Enqueue(() => TestPath(moveDest, null, runId, isAlternate: false, tryAlternates: false, slowDown: true));
			}
			else
				m_navSet.Settings_Task_NavWay.SpeedMaxRelative = float.MaxValue;

			RunItem();
		}

		public void TestRotate(Vector3 displacement)
		{
			if (Globals.UpdateCount < m_nextRunRotate)
				return;
			m_nextRunRotate = Globals.UpdateCount + 10ul;

			m_navBlock = m_navSet.Settings_Current.NavigationBlock;
			m_pathHigh.Enqueue(() => {
				Vector3 axis; Vector3.Normalize(ref displacement, out axis);
				IMyEntity obstruction;
				if (m_rotateChecker.TestRotate(axis, m_ignoreAsteroid, out obstruction))
					m_rotateState = PathState.No_Obstruction;
				else
					m_rotateState = PathState.Path_Blocked;
				RotateObstruction = obstruction;

				RunItem();
			});

			RunItem();
		}

		/// <summary>
		/// Test a path between current position and destination.
		/// </summary>
		private void TestPath(Vector3D destination, MyEntity destEntity, byte runId, bool isAlternate, bool tryAlternates, bool slowDown = false)
		{
			if (runId != m_runId)
			{
				m_logger.debugLog("destination changed, abort", "TestPath()", Logger.severity.DEBUG);
				return;
			}

			if (!lock_testPath.TryAcquireExclusive())
			{
				m_logger.debugLog("Already running, requeue (destination:" + destination + ", destEntity: " + destEntity + ", runId :" + runId
					+ ", isAlternate: " + isAlternate + ", tryAlternates: " + tryAlternates + ", slowDown: " + slowDown + ")", "TestPath()");
				LockedQueue<Action> queue = isAlternate ? m_pathLow : m_pathHigh;
				queue.Enqueue(() => TestPath(destination, destEntity, runId, isAlternate, tryAlternates));
				return;
			}
			try
			{
				if (m_grid != m_navBlock.Grid)
				{
					m_logger.debugLog("Grid has changed, from " + m_grid.DisplayName + " to " + m_navBlock.Grid.DisplayName + ", nav block: " + m_navBlock.Block.getBestName(), "TestPath()", Logger.severity.WARNING);
					return;
				}
				m_logger.debugLog("Running, (destination:" + destination + ", destEntity: " + destEntity + ", runId :" + runId
						+ ", isAlternate: " + isAlternate + ", tryAlternates: " + tryAlternates + ", slowDown: " + slowDown + ")", "TestPath()");
				
				MyEntity obstructing;
				Vector3? pointOfObstruction;

				if ((isAlternate || tryAlternates) && !m_pathChecker.GravityTest(new LineSegmentD(m_navBlock.WorldPosition, destination), m_destination, out obstructing, out pointOfObstruction))
				{
					m_pathState = PathState.Searching;
					m_logger.debugLog("blocked by gravity at " + pointOfObstruction, "TestGravity()", Logger.severity.DEBUG);
					if (tryAlternates)
					{
						Vector3D point = pointOfObstruction.Value;
						Vector3 direction = m_navBlock.WorldPosition - obstructing.GetCentre(); direction.Normalize();
						float distance = Vector3.Distance(m_navBlock.WorldPosition, pointOfObstruction.Value);
						float minDist = m_navSet.Settings_Current.DestinationRadius;
						if (minDist < 1000f)
						{
							minDist = 1500f;
							m_navSet.Settings_Current.DestinationRadius = 1000f;
						}
						else
							minDist *= 1.5f;

						m_pathHigh.Clear();
						FindAlternate_AroundObstruction(pointOfObstruction.Value, direction, distance, minDist * minDist, runId);
						m_pathLow.Enqueue(() => m_pathState = PathState.Path_Blocked);
					}
					return;
				}

				if (m_pathChecker.TestFast(m_navBlock, destination, m_ignoreAsteroid, destEntity, m_landing))
				{
					m_logger.debugLog("path is clear (fast)", "TestPath()", Logger.severity.TRACE);
					PathClear(ref destination, runId, isAlternate, slowDown);
					return;
				}

				if (m_pathChecker.TestSlow(out obstructing, out pointOfObstruction))
				{
					m_logger.debugLog("path is clear (slow)", "TestPath()", Logger.severity.TRACE);
					PathClear(ref destination, runId, isAlternate, slowDown);
					return;
				}

				if (runId != m_runId)
				{
					m_logger.debugLog("destination changed, abort", "TestPath()", Logger.severity.DEBUG);
					return;
				}

				if (isAlternate && m_altPath_AlternatesFound != 0)
					IncrementAlternatesFound();

				if (slowDown)
				{
					Vector3 displacement = pointOfObstruction.Value - m_navBlock.WorldPosition;
					m_navSet.Settings_Task_NavWay.SpeedMaxRelative = Vector3.Distance(pointOfObstruction.Value, m_navBlock.WorldPosition) / LookAheadSpeed_Seconds;
					m_logger.debugLog("Set MaxRelativeSpeed to " + m_navSet.Settings_Task_NavWay.SpeedMaxRelative, "TestPath()", Logger.severity.TRACE);
					return;
				}

				m_pathState = PathState.Searching;

				m_logger.debugLog("path is blocked by " + obstructing.getBestName() + " at " + pointOfObstruction + ", destEntity: " + destEntity.getBestName(), "TestPath()", isAlternate ? Logger.severity.TRACE : Logger.severity.DEBUG);
				m_logger.debugLog(obstructing is IMyCubeBlock, "grid: " + obstructing.GetTopMostParent().DisplayName, "TestPath()", isAlternate ? Logger.severity.TRACE : Logger.severity.DEBUG);

				if (tryAlternates)
				{
					ClearAltPath();
					MoveObstruction = obstructing;
					TryAlternates(runId, pointOfObstruction.Value, obstructing);
				}
			}
			finally
			{
				lock_testPath.ReleaseExclusive();
				RunItem();
			}
		}

		private void TryAlternates(byte runId, Vector3 pointOfObstruction, IMyEntity obstructing)
		{
			try
			{
				m_pathHigh.Clear();
			}
			catch (IndexOutOfRangeException ioore)
			{
				m_logger.debugLog("Caught IndexOutOfRangeException", "TryAlternates()", Logger.severity.ERROR);
				m_logger.debugLog("Count: " + m_pathHigh.Count, "TryAlternates()", Logger.severity.ERROR);
				m_logger.debugLog("Exception: " + ioore, "TryAlternates()", Logger.severity.ERROR);

				throw ioore;
			}

			Vector3 displacement = pointOfObstruction - m_navBlock.WorldPosition;

			if (m_canChangeCourse)
			{
				// using a halfway point works much better when the obstuction is near the destination
				FindAlternate_AroundObstruction(displacement, (m_navBlock.WorldPosition + pointOfObstruction) * 0.5f, obstructing.GetLinearVelocity(), runId);
				//FindAlternate_AroundObstruction(displacement, pointOfObstruction, obstructing.GetLinearVelocity(), runId);
				FindAlternate_JustMove(runId);
			}
			FindAlternate_HalfwayObstruction(displacement, runId);
			m_pathLow.Enqueue(() => {
				if (m_altPath_AlternatesFound != 0)
				{
					m_logger.debugLog("Setting waypoint: " + m_altPath_Waypoint + ", reachable distance: " + m_altPath_ClosenessToDest, "TryAlternates()", Logger.severity.DEBUG);
					new GOLIS(m_mover, m_navSet, m_altPath_Waypoint, true);
				}
				RunItem();
			});
			m_pathLow.Enqueue(() => m_pathState = PathState.Path_Blocked);
		}

		private void FindAlternate_AroundObstruction(Vector3 displacement, Vector3 pointOfObstruction, Vector3 obstructionSpeed, byte runId)
		{
			float distance = displacement.Length();
			Vector3 direction; direction = displacement / distance;
			Vector3 newPath_v1, newPath_v2;
			direction.CalculatePerpendicularVector(out newPath_v1);
			newPath_v2 = direction.Cross(newPath_v1);

			Vector3[] NewPathVectors = { newPath_v1, newPath_v2, Vector3.Negate(newPath_v1), Vector3.Negate(newPath_v2) };
			ICollection<Vector3> AcceptableDirections;
			if (obstructionSpeed == Vector3.Zero)
				AcceptableDirections = NewPathVectors;
			else
			{
				AcceptableDirections = new List<Vector3>();
				Vector3 SpeedRejection = Vector3.Reject(obstructionSpeed, direction);
				float zeroDistSquared = Vector3.DistanceSquared(Vector3.Zero, SpeedRejection);
				foreach (Vector3 vector in NewPathVectors)
				{
					float distSquared = Vector3.DistanceSquared(vector, SpeedRejection);
					if (distSquared <= zeroDistSquared)
						AcceptableDirections.Add(vector);
				}
			}

			float destRadius = m_navSet.Settings_Current.DestinationRadius;
			destRadius = destRadius < 20f ? destRadius + 10f : destRadius * 1.5f;
			destRadius *= destRadius;

			foreach (Vector3 PathVector in AcceptableDirections)
				FindAlternate_AroundObstruction(pointOfObstruction, PathVector, distance, destRadius, runId);
		}

		private void FindAlternate_AroundObstruction(Vector3 pointOfObstruction, Vector3 direction, float distance, float minDistSq, byte runId)
		{
			m_logger.debugLog("point: " + pointOfObstruction + ", direction: " + direction + ", distance: " + distance + ", dest radius: " + minDistSq, "FindAlternate_AroundObstruction()");
			for (int newPathDistance = 8; newPathDistance < 1000000; newPathDistance *= 2)
			{
				if (newPathDistance > distance * 10f)
					break;
				Vector3 tryDest = pointOfObstruction + newPathDistance * direction;
				if (Vector3.DistanceSquared(m_navBlock.WorldPosition, tryDest) > minDistSq)
					m_pathLow.Enqueue(() => TestPath(tryDest, null, runId, isAlternate: true, tryAlternates: false));
			}
		}

		private void FindAlternate_HalfwayObstruction(Vector3 displacement, byte runId)
		{
			Vector3 halfway = displacement * 0.5f + m_navBlock.WorldPosition;
			m_pathLow.Enqueue(() => TestPath(halfway, null, runId, isAlternate: true, tryAlternates: false));
			halfway -= displacement * 0.25f;
			m_pathLow.Enqueue(() => TestPath(halfway, null, runId, isAlternate: true, tryAlternates: false));
		}

		private void FindAlternate_JustMove(byte runId)
		{
			Vector3D worldPosition = m_navBlock.WorldPosition;
			float distance = m_navSet.Settings_Current.DestinationRadius * 2f;
			for (int i = 0; i < Base25Directions.Length; i++)
			{
				Vector3 tryDest = worldPosition + Base25Directions[i] * (distance + i);
				m_pathLow.Enqueue(() => TestPath(tryDest, null, runId, isAlternate: true, tryAlternates: false));
			}
		}

		private void RunItem()
		{
			m_logger.debugLog("entered, high count: " + m_pathHigh.Count + ", low count: " + m_pathLow.Count + ", high parallel: " + Thread_High.ParallelTasks + ", low parallel: " + Thread_Low.ParallelTasks, "RunItem()");
			if (m_pathHigh.Count != 0)
			{
				m_logger.debugLog("Adding item to Thread_High, count: " + (Thread_High.ParallelTasks + 1), "RunItem()");
				Action act = m_pathHigh.Dequeue();
				Thread_High.EnqueueAction(act);
			}
			else if (m_pathLow.Count != 0)
			{
				m_logger.debugLog("Adding item to Thread_Low, count: " + (Thread_Low.ParallelTasks + 1), "RunItem()");
				Action act = m_pathLow.Dequeue();
				Thread_Low.EnqueueAction(act);
			}
		}

		private void PathClear(ref Vector3D destination, byte runId, bool isAlternate, bool slowDown)
		{
			if (runId == m_runId)
			{
				if (isAlternate)
				{
					LineSegment line = new LineSegment() { From = destination, To = m_destination };
					float closeness = m_pathChecker.distanceCanTravel(line);
					m_logger.debugLog("for point: " + line.From + ", waypoint distance: " + Vector3D.Distance(m_grid.GetCentre(), destination) + ", reachable distance: " + closeness + ", required: " + m_altPath_ClosenessToDest, "PathClear()", Logger.severity.TRACE);

					if (!AddAlternatePath(closeness, ref destination))
						m_logger.debugLog("throwing out: " + line.From + ", reachable distance: " + closeness + ", required: " + m_altPath_ClosenessToDest, "PathClear()", Logger.severity.TRACE);

					if (m_altPath_AlternatesFound != AlternatesToCheck && m_pathLow.Count != 0 && m_altPath_ClosenessToDest != 0f)
					{
						m_logger.debugLog("keep searching, m_altPath_AlternatesFound: " + m_altPath_AlternatesFound, "PathClear()");
						return;
					}
				}
				if (slowDown)
				{
					//m_logger.debugLog("Removing speed limit", "PathClear()");
					m_navSet.Settings_Task_NavWay.SpeedMaxRelative = float.MaxValue;
					return;
				}
				MoveObstruction = null;
				m_pathState = PathState.No_Obstruction;
				m_pathLow.Clear();
			}
			else
				m_logger.debugLog("destination changed, abort", "PathClear()", Logger.severity.DEBUG);
			return;
		}

		public void ClearAltPath()
		{
			this.m_altPath_AlternatesFound = 0;
			this.m_altPath_ClosenessToDest = float.MaxValue;
		}

		public bool AddAlternatePath(float closeness, ref Vector3D waypoint)
		{
			if (closeness < this.m_altPath_ClosenessToDest)
			{
				this.m_altPath_ClosenessToDest = closeness;
				this.m_altPath_Waypoint = waypoint;
				IncrementAlternatesFound();
				return true;
			}
			else if (this.m_altPath_AlternatesFound != 0)
				IncrementAlternatesFound();
			return false;
		}

		public void IncrementAlternatesFound()
		{
			this.m_altPath_AlternatesFound++;
			if (this.m_altPath_AlternatesFound == AlternatesToCheck || m_altPath_ClosenessToDest == 0f)
			{
				m_logger.debugLog("Setting waypoint: " + m_altPath_Waypoint + ", reachable distance: " + m_altPath_ClosenessToDest, "IncrementAlternatesFound()", Logger.severity.DEBUG);
				new GOLIS(m_mover, m_navSet, m_altPath_Waypoint, true);
			}
		}

		public void SetAlternateBase(float closeness)
		{
			this.m_altPath_AlternatesFound = 0;
			this.m_altPath_ClosenessToDest = float.MaxValue;
		}

	}
}
