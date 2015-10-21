using System;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Rynchodon.Autopilot.Navigator;
using Rynchodon.Threading;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinder
{
	/// <summary>
	/// Finds alternate paths if the current path is blocked.
	/// </summary>
	internal class Pathfinder
	{

		private enum PathState : byte { Not_Run, No_Obstruction, Searching, No_Way_Forward }

		private const float SqRtHalf = 0.70710678f;
		private const float SqRtThird = 0.57735026f;

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
		private static ThreadManager Thread_Low = new ThreadManager(1, true, "Path_Low");

		private static short RunIdPool;

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
		private readonly AllNavigationSettings m_navSet;
		private readonly Mover m_mover;
		private readonly FastResourceLock lock_testPath = new FastResourceLock("Path_lock_path");
		private readonly FastResourceLock lock_testRotate = new FastResourceLock("Path_lock_rotate");

		private readonly LockedQueue<Action> m_pathLow = new LockedQueue<Action>();

		private PseudoBlock m_navBlock;
		private MyEntity m_destEntity;
		private bool m_ignoreAsteroid, m_landing, m_canChangeCourse;

		private PathState m_pathState = PathState.Not_Run;
		private PathState m_rotateState = PathState.No_Obstruction;

		private ulong m_nextRun;
		private short m_runId;

		public bool CanMove { get { return m_pathState == PathState.No_Obstruction; } }
		public bool CanRotate { get { return m_rotateState == PathState.No_Obstruction; } }
		public bool Pathfinding { get { return m_pathState == PathState.Searching; } }

		public Pathfinder(IMyCubeGrid grid, AllNavigationSettings navSet, Mover mover)
		{
			grid.throwIfNull_argument("grid");
			m_grid = grid;
			m_logger = new Logger("Pathfinder", () => grid.DisplayName, () => m_pathState.ToString(), () => m_rotateState.ToString());
			m_pathChecker = new PathChecker(grid);
			m_navSet = navSet;
			m_mover = mover;
		}

		public void TestPath(Vector3D destination, bool landing)
		{
			if (m_navSet.Settings_Current.DestinationChanged)
			{
				m_logger.debugLog("new destination: " + destination, "TestPath()");
				m_navSet.Settings_Task_NavWay.DestinationChanged = false;
				m_runId = RunIdPool++;
				m_pathLow.Clear();
				m_pathState = PathState.Not_Run;
			}
			//else
			//	m_logger.debugLog("destination unchanged", "TestPath()");

			if (Globals.UpdateCount < m_nextRun)
				return;
			m_nextRun = Globals.UpdateCount + 10ul;

			if (m_pathLow.Count != 0)
			{
				m_logger.debugLog("path low is running", "TestPath()");
				return;
			}

			m_navBlock = m_navSet.Settings_Current.NavigationBlock;
			m_destEntity = m_navSet.Settings_Current.DestinationEntity as MyEntity;
			m_ignoreAsteroid = m_navSet.Settings_Current.IgnoreAsteroid;
			m_landing = landing;
			m_canChangeCourse = m_navSet.Settings_Current.PathfinderCanChangeCourse;

			Vector3 displacement = destination - m_navBlock.WorldPosition;
			float distanceSquared = displacement.LengthSquared();
			if (distanceSquared > 10000f)
			{
				// only look ahead 10 s / 100 m
				float speedSquared = m_navBlock.Grid.GetLinearVelocity().LengthSquared();
				float testDistance = speedSquared < 100f ? 100f : (float)Math.Sqrt(speedSquared) * 10f;
				Vector3 direction; Vector3.Normalize(ref displacement, out direction);
				destination = m_navBlock.WorldPosition + testDistance * direction;
			}

			Thread_High.EnqueueAction(() => TestPath(destination, alternate: false, runId: m_runId));

			// this may provide a better alternative to slowing down when near something
			//// given velocity and distance, calculate destination
			//float distance = m_navSet.Settings_Current.Distance;
			//if (float.IsNaN(distance))
			//{
			//	m_logger.debugLog("distance is NaN", "Update()");
			//	return;
			//}
			//Vector3 direction = m_grid.Physics.LinearVelocity;
			//if (direction.LengthSquared() < 1)
			//{
			//	direction = m_grid.Physics.LinearAcceleration;
			//	if (direction.LengthSquared() < 1)
			//	{
			//		m_logger.debugLog("not moving, velocity: " + m_grid.Physics.LinearVelocity + ", accel: " + m_grid.Physics.LinearAcceleration, "Update()");
			//		return;
			//	}
			//}
			//direction.Normalize();
		}

		public void TestRotate(Vector3 displacement)
		{
			//PseudoBlock navBlock = m_navSet.Settings_Current.NavigationBlock;
			//Thread_High.EnqueueAction(() => TestRotate(navBlock, displacement));
		}

		/// <summary>
		/// Test a path between current position and destination.
		/// </summary>
		private void TestPath(Vector3D destination, bool alternate, short runId)
		{
			if (runId != m_runId)
			{
				m_logger.debugLog("destination changed, abort", "TestPath()");
				return;
			}

			if (m_pathChecker.TestFast(m_navBlock, destination, m_ignoreAsteroid, m_destEntity, m_landing))
			{
				m_logger.debugLog("path is clear (fast)", "TestPath()", Logger.severity.DEBUG);
				if (runId == m_runId)
				{
					if (alternate)
						SetWaypoint(destination);
					m_pathState = PathState.No_Obstruction;
					m_pathLow.Clear();
				}
				else
					m_logger.debugLog("destination changed, abort", "TestPath()");
				return;
			}
			MyEntity obstructing;
			IMyCubeGrid obsGrid;
			Vector3? pointOfObstruction;
			if (m_pathChecker.TestSlow(out obstructing, out obsGrid, out pointOfObstruction))
			{
				m_logger.debugLog("path is clear (slow)", "TestPath()", Logger.severity.DEBUG);
				if (runId == m_runId)
				{
					if (alternate)
						SetWaypoint(destination);
					m_pathState = PathState.No_Obstruction;
					m_pathLow.Clear();
				}
				else
					m_logger.debugLog("destination changed, abort", "TestPath()");
				return;
			}

			if (runId != m_runId)
			{
				m_logger.debugLog("destination changed, abort", "TestPath()");
				return;
			}

			m_pathState = PathState.Searching;

			m_logger.debugLog("path is blocked by " + obstructing.getBestName() + " at " + pointOfObstruction + ", target: " + m_destEntity.getBestName(), "TestPath()", Logger.severity.TRACE);

			if (!alternate)
			{
				Vector3 displacement = pointOfObstruction.Value - m_navBlock.WorldPosition;

				if (m_canChangeCourse)
				{
					FindAlternate_AroundObstruction(displacement, pointOfObstruction.Value, runId);
					FindAlternate_JustMove(runId);
				}
				FindAlternate_HalfwayObstruction(displacement, runId);
			}
			RunAlternate();
		}

		private void FindAlternate_AroundObstruction(Vector3 displacement, Vector3 pointOfObstruction, short runId)
		{
			Vector3 direction; Vector3.Normalize(ref displacement, out direction);
			Vector3 newPath_v1, newPath_v2;
			direction.CalculatePerpendicularVector(out newPath_v1);
			newPath_v2 = direction.Cross(newPath_v1);
			Vector3[] NewPathVectors = { newPath_v1, newPath_v2, Vector3.Negate(newPath_v1), Vector3.Negate(newPath_v2) };

			float destRadius = m_navSet.Settings_Current.DestinationRadius * 1.5f;
			destRadius *= destRadius;

			for (int newPathDistance = 8; newPathDistance < 10000; newPathDistance *= 2)
				foreach (Vector3 PathVector in NewPathVectors)
				{
					Vector3 tryDest = pointOfObstruction + newPathDistance * PathVector;

					if (Vector3.DistanceSquared(m_navBlock.WorldPosition, tryDest) > destRadius)
						m_pathLow.Enqueue(() => TestPath(tryDest, alternate: true, runId: runId));
				}
		}

		private void FindAlternate_HalfwayObstruction(Vector3 displacement, short runId)
		{
			Vector3 halfway = displacement * 0.5f + m_navBlock.WorldPosition;
			m_pathLow.Enqueue(() => TestPath(halfway, alternate: true, runId: runId));
		}

		private void FindAlternate_JustMove(short runId)
		{
			Vector3D worldPosition = m_navBlock.WorldPosition;
			float distance = m_navSet.Settings_Current.DestinationRadius * 2f;
			for (int i = 0; i < Base25Directions.Length; i++)
			{
				Vector3 tryDest = worldPosition + Base25Directions[i] * (distance + i);
				m_pathLow.Enqueue(() => TestPath(tryDest, alternate: true, runId: runId));
			}
		}

		private void TestRotate(PseudoBlock navBlock, Vector3 displacement)
		{
			//BoundingBoxD WorldAABB = navBlock.Grid.WorldAABB;
			//BoundingSphereD WorldVolume = navBlock.Grid.WorldVolume;
			//List<MyEntity> entities = new List<MyEntity>();
			//MyGamePruningStructure.GetAllTopMostEntitiesInBox(ref WorldAABB, entities);
			//foreach (MyEntity entity in entities)
			//	if (PathChecker.collect_Entity(m_grid, entity))
			//	{
			//		IMyVoxelMap voxel = entity as IMyVoxelMap;
			//		if (voxel != null && !voxel.GetIntersectionWithSphere(ref WorldVolume))
			//			continue;

			//		m_logger.debugLog("Blocked by: " + entity.getBestName() + ", volume: " + WorldVolume, "TestRotate()");
			//	}
			//m_rotateState = PathState.No_Obstruction;
		}

		private void RunAlternate()
		{
			if (m_pathLow.Count != 0)
			{
				m_logger.debugLog("Adding item to thread_low", "RunAlternate()");
				Action act = m_pathLow.Dequeue();
				Thread_Low.EnqueueAction(act);
			}
		}

		private void SetWaypoint(Vector3D waypoint)
		{
			m_logger.debugLog("Setting waypoint: " + waypoint, "SetWaypoint()");
			new GOLIS(m_mover, m_navSet, waypoint, true);
			//m_navSet.Settings_Task_NavWay.DestinationChanged = false;
		}

	}
}
