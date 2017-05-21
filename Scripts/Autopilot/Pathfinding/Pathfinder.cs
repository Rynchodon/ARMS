#if DEBUG
//#define TRACE
#endif

using System;
using System.Collections.Generic;
using System.Reflection;
using Rynchodon.AntennaRelay;
using Rynchodon.Attached;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Rynchodon.Threading;
using Rynchodon.Utility;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinding
{
	/// <summary>
	/// Figures out how to get to where the navigator wants to go, supplies the direction and distance to Mover.
	/// </summary>
	/// <remarks>
	/// The process is divided into two parts: high-level/repulsion and low-level/pathfinding.
	/// Repulsion treats every potential obstruction as a sphere and tries to keep autopilot away from those spheres.
	/// When autopilot gets too close to a potential obstruction or repulsion is not suitable, Pathfinder switches to pathfinding.
	/// Pathfinding is based on A* but uses waypoints instead of grid cells as they are more suited to the complex environments and ships in Space Engineers.
	/// 
	/// Obviously it is a lot more complicated than that but if anyone is actually reading this and has questions, just ask.
	/// </remarks>
	public partial class Pathfinder
	{

		#region Static

		public const float SpeedFactor = 3f, VoxelAdd = 10f;

		private class StaticVariables
		{
			public ThreadManager ThreadForeground, ThreadBackground;
			public MethodInfo RequestJump;
			public FieldInfo JumpDisplacement;

			public StaticVariables()
			{
			Logger.DebugLog("entered", Logger.severity.TRACE);
				byte allowedThread = (byte)(Environment.ProcessorCount / 2);
				ThreadForeground = new ThreadManager(allowedThread, false, "PathfinderForeground");
				ThreadBackground = new ThreadManager(allowedThread, true, "PathfinderBackground");

				Type[] argTypes = new Type[] { typeof(Vector3D), typeof(long) };
				RequestJump = typeof(MyGridJumpDriveSystem).GetMethod("RequestJump", BindingFlags.NonPublic | BindingFlags.Instance, Type.DefaultBinder, argTypes, null);

				JumpDisplacement = typeof(MyGridJumpDriveSystem).GetField("m_jumpDirection", BindingFlags.NonPublic | BindingFlags.Instance);
			}
		}

		private static StaticVariables value_static;
		private static StaticVariables Static
		{
			get
			{
				if (Globals.WorldClosed)
					throw new Exception("World closed");
				if (value_static == null)
					value_static = new StaticVariables();
				return value_static;
			}
			set { value_static = value; }
		}

		[OnWorldClose]
		private static void Unload()
		{
			Static = null;
		}

		#endregion

		#region Fields
		private readonly PathTester m_tester;

		private readonly FastResourceLock m_runningLock = new FastResourceLock();
		private bool m_runInterrupt, m_runHalt, m_navSetChange = true;
		private ulong m_waitUntil, m_nextJumpAttempt;
		private LineSegmentD m_lineSegment = new LineSegmentD();
		private MyGridJumpDriveSystem m_jumpSystem;

		// Inputs: rarely change

		private MyCubeGrid m_autopilotGrid { get { return m_tester.AutopilotGrid; } set { m_tester.AutopilotGrid = value; } }
		private PseudoBlock m_navBlock;
		private Destination[] m_destinations;
		/// <summary>Initially set to m_destinations[0], pathfinder may change this if one of the other destinations is easier to reach.</summary>
		private Destination m_pickedDestination;
		private bool m_ignoreVoxel, m_canChangeCourse;

		// Inputs: always updated

		private Vector3 m_addToVelocity;
		private Vector3D m_currentPosition, m_destWorld;
		private Vector3 m_autopilotVelocity { get { return m_autopilotGrid.Physics.LinearVelocity; } }
		private float m_autopilotShipBoundingRadius { get { return m_autopilotGrid.PositionComp.LocalVolume.Radius; } }

		// High Level

		private SphereClusters m_clusters = new SphereClusters();
		/// <summary>First this list is populated by pruning structure, when calculating repulsion it is populated with entities to be avoided.</summary>
		private List<MyEntity> m_entitiesPruneAvoid = new List<MyEntity>();
		/// <summary>List of entities that will repulse the autopilot.</summary>
		private List<MyEntity> m_entitiesRepulse = new List<MyEntity>();
		private bool m_checkVoxel;
		private Vector3 m_moveDirection;
		private float m_moveLength;

		// Results

		private Obstruction m_obstructingEntity;
		private MyCubeBlock m_obstructingBlock;
		private bool m_holdPosition = true;

		#endregion

		#region Properties

		private Logable Log { get { return new Logable(m_autopilotGrid.nameWithId(), m_navBlock?.DisplayName, CurrentState.ToString()); } }
		public MyEntity ReportedObstruction { get { return m_obstructingBlock ?? m_obstructingEntity.Entity; } }
		public Mover Mover { get; private set; }
		public AllNavigationSettings NavSet { get { return Mover.NavSet; } }
		public RotateChecker RotateCheck { get { return Mover.RotateCheck; } }
		public InfoString.StringId_Jump JumpComplaint { get; private set; }

		#endregion

		/// <summary>
		/// Creates a Pathfinder for a ShipControllerBlock.
		/// </summary>
		/// <param name="block">The autopilot or remote control that needs to find its way in the world.</param>
		public Pathfinder(ShipControllerBlock block)
		{
			Mover = new Mover(block, new RotateChecker(block.CubeBlock, CollectEntities));
			m_tester = new PathTester(Mover.Block);
			NavSet.AfterTaskComplete += NavSet_AfterTaskComplete;
		}

		/// <summary>
		/// Forces Pathfinder to reset.
		/// </summary>
		private void NavSet_AfterTaskComplete()
		{
			m_navSetChange = true;
			if (CurrentState != State.Crashed)
				CurrentState = State.None;
		}

		#region On Autopilot Thread

		/// <summary>
		/// Forces Pathfinder to stop.
		/// </summary>
		public void Halt()
		{
			m_runHalt = true;
		}

		/// <summary>
		/// Maintain destination radius from an entity.
		/// </summary>
		/// <param name="targetEntity">The entity to maintain position from.</param>
		public void HoldPosition(IMyEntity targetEntity)
		{
			Vector3D myCentre = m_autopilotGrid.GetCentre();
			Vector3D targetCentre = targetEntity.GetCentre();

			double distanceSquared; Vector3D.DistanceSquared(ref myCentre, ref targetCentre, out distanceSquared);
			double targetDist = NavSet.Settings_Current.DestinationRadius + m_autopilotGrid.PositionComp.WorldVolume.Radius + targetEntity.WorldVolume.Radius;
			if (distanceSquared < targetDist * targetDist)
				MoveTo(destinations: Destination.FromWorld(targetEntity, m_navBlock.WorldPosition));
			else
			{
				Vector3D offset; Vector3D.Subtract(ref myCentre, ref targetCentre, out offset);
				offset.Normalize();
				Vector3D.Multiply(ref offset, targetDist, out offset);

				MoveTo(destinations: new Destination(targetEntity, offset));
			}
		}

		/// <summary>
		/// Maintain destination radius from an entity.
		/// </summary>
		/// <param name="targetEntity">The entity to maintain position from.</param>
		public void HoldPosition(LastSeen targetEntity)
		{
			if (targetEntity.isRecent())
			{
				HoldPosition(targetEntity.Entity);
				return;
			}

			double distanceSquared = Vector3D.DistanceSquared(m_autopilotGrid.GetCentre(), targetEntity.LastKnownPosition);
			double targetDist = NavSet.Settings_Current.DestinationRadius + m_autopilotGrid.PositionComp.WorldVolume.Radius + targetEntity.Entity.WorldVolume.Radius;
			if (distanceSquared < targetDist * targetDist)
				MoveTo(targetEntity.LastKnownVelocity, new Destination(m_navBlock.WorldPosition));
			else
				MoveTo(targetEntity.LastKnownVelocity, new Destination(targetEntity.LastKnownPosition));
		}

		/// <summary>
		/// Move to an entity given by a LastSeen, performs recent check so navigator will not have to.
		/// </summary>
		/// <param name="targetEntity">The destination entity.</param>
		/// <param name="offset">Destination's offset from the centre of targetEntity.</param>
		/// <param name="addToVelocity">Velocity to add to autopilot's target velocity.</param>
		public void MoveTo(LastSeen targetEntity, Vector3D offset = default(Vector3D), Vector3 addToVelocity = default(Vector3))
		{
			Destination dest;
			if (targetEntity.isRecent())
			{
				dest = new Destination(targetEntity.Entity, ref offset);
				Log.DebugLog("recent. dest: " + dest);
			}
			else
			{
				dest = new Destination(targetEntity.LastKnownPosition + offset);
				addToVelocity += targetEntity.LastKnownVelocity;
				Log.DebugLog("not recent. dest: " + dest + ", actual position: " + targetEntity.Entity.GetPosition());
			}
			MoveTo(addToVelocity, dest);
		}

		/// <summary>
		/// Pathfind to one or more destinations. Pathfinder will first attempt to move to the first destination and, failing that, will treat all destinations as valid targets.
		/// </summary>
		/// <param name="addToVelocity">Velocity to add to autopilot's target velocity.</param>
		/// <param name="destinations">The destinations that autopilot will try to reach.</param>
		public void MoveTo(Vector3 addToVelocity = default(Vector3), params Destination[] destinations)
		{
			if (CurrentState == State.Crashed)
			{
				m_runHalt = true;
				Mover.MoveAndRotateStop(false);
				return;
			}

			m_runHalt = false;
			Move();

			AllNavigationSettings.SettingsLevel level = Mover.NavSet.Settings_Current;

			m_addToVelocity = addToVelocity;
			m_destinations = destinations;
			m_pickedDestination = m_destinations[0];

			if (!m_navSetChange)
			{
				Static.ThreadForeground.EnqueueAction(Run);
				return;
			}
			
			Log.DebugLog("Nav settings changed");

			using (m_runningLock.AcquireExclusiveUsing())
			{
				m_runInterrupt = true;
				m_navBlock = level.NavigationBlock;
				m_autopilotGrid = (MyCubeGrid)m_navBlock.Grid;
				m_ignoreVoxel = level.IgnoreAsteroid;
				m_canChangeCourse = level.PathfinderCanChangeCourse;
				m_holdPosition = true;
				m_navSetChange = false;
			}

			if (m_navBlock == null)
			{
				Log.DebugLog("No nav block");
				return;
			}

			Static.ThreadForeground.EnqueueAction(Run);
		}

		/// <summary>
		/// Call on autopilot thread. Instructs Mover to calculate movement.
		/// </summary>
		private void Move()
		{
			MyEntity relative = GetRelativeEntity();
			Vector3 disp; Vector3.Multiply(ref m_moveDirection, m_moveLength, out disp);

			Vector3 targetVelocity = relative != null && relative.Physics != null && !relative.Physics.IsStatic ? relative.Physics.LinearVelocity : Vector3.Zero;
			if (!m_path.HasTarget)
			{
				Vector3 temp; Vector3.Add(ref targetVelocity, ref m_addToVelocity, out temp);
				targetVelocity = temp;
			}

			if (m_navSetChange || m_runInterrupt || m_runHalt)
				return;

			if (m_holdPosition)
			{
				//Log.DebugLog("Holding position");
				Mover.CalcMove(m_navBlock, ref Vector3.Zero, ref targetVelocity);
				return;
			}

			Log.TraceLog("Should not be moving! disp: " + disp, Logger.severity.DEBUG, condition: CurrentState != State.Unobstructed && CurrentState != State.FollowingPath);
			Log.TraceLog("moving: " + disp + ", targetVelocity: " + targetVelocity);
			Mover.CalcMove(m_navBlock, ref disp, ref targetVelocity);
		}
		
		#endregion

		#region Flow

		/// <summary>
		/// Main entry point while not pathfinding.
		/// </summary>
		private void Run()
		{
			if (m_waitUntil > Globals.UpdateCount)
			{
				m_holdPosition = true;
				OnComplete();
				return;
			}

			if (!m_runningLock.TryAcquireExclusive())
				return;
			try
			{
				if (m_runHalt)
					return;
				if (m_runInterrupt)
				{
					m_path.Clear();
					CurrentState = State.None;
					m_runInterrupt = false;
				}
				else if (CurrentState == State.SearchingForPath)
					return;

				if (m_jumpSystem != null)
				{
					if (m_jumpSystem.GetJumpDriveDirection().HasValue)
					{
						JumpComplaint = InfoString.StringId_Jump.Jumping;
						FillDestWorld();
						return;
					}
					else
					{
						float distance = NavSet.Settings_Current.Distance;
						FillDestWorld();
						if (NavSet.Settings_Current.Distance < distance - MyGridJumpDriveSystem.MIN_JUMP_DISTANCE * 0.5d)
						{
							Log.DebugLog("Jump completed, distance travelled: " + (distance - NavSet.Settings_Current.Distance), Logger.severity.INFO);
							m_nextJumpAttempt = 0uL;
							JumpComplaint = InfoString.StringId_Jump.None;
						}
						else
						{
							Log.DebugLog("Jump failed, distance travelled: " + (distance - NavSet.Settings_Current.Distance), Logger.severity.WARNING);
							JumpComplaint = InfoString.StringId_Jump.Failed;
						}
						m_jumpSystem = null;
					}
				}
				else
					FillDestWorld();

				TestCurrentPath();
				// if the current path is obstructed, it is not possible to jump
				if (!m_holdPosition)
				{
					if (TryJump())
						m_holdPosition = true;
				}
				else
					JumpComplaint = InfoString.StringId_Jump.None;
				OnComplete();
			}
			catch
			{
				Log.AlwaysLog("Pathfinder crashed", Logger.severity.ERROR);
				CurrentState = State.Crashed;
				throw;
			}
			finally { m_runningLock.ReleaseExclusive(); }

			PostRun();
		}

		/// <summary>
		/// Clears entity lists.
		/// </summary>
		private void OnComplete()
		{
			m_entitiesPruneAvoid.Clear();
			m_entitiesRepulse.Clear();
		}

		/// <summary>
		/// Queues the next action to foreground or background thread.
		/// </summary>
		private void PostRun()
		{
			if (m_runHalt)
				return;

			if (m_runInterrupt)
				Static.ThreadForeground.EnqueueAction(Run);
			else if (CurrentState == State.SearchingForPath)
				Static.ThreadBackground.EnqueueAction(ContinuePathfinding);
		}

		#endregion

		#region Common

		/// <summary>
		/// Sets m_destWorld to the correct value.
		/// </summary>
		private void FillDestWorld()
		{
			m_currentPosition = m_autopilotGrid.GetCentre();
			Vector3D finalDestWorld = m_pickedDestination.WorldPosition() + m_currentPosition - m_navBlock.WorldPosition;
			double distance; Vector3D.Distance(ref finalDestWorld, ref m_currentPosition, out distance);
			NavSet.Settings_Current.Distance = (float)distance;

			Log.DebugLog("Missing obstruction", Logger.severity.ERROR, condition: m_path.HasTarget && m_obstructingEntity.Entity == null);

			m_destWorld = m_path.HasTarget ? m_path.GetTarget() + m_obstructingEntity.GetPosition() : finalDestWorld;

			Log.TraceLog("final dest world: " + finalDestWorld + ", picked: " + m_pickedDestination.WorldPosition() + ", current position: " + m_currentPosition + ", offset: " + (m_currentPosition - m_navBlock.WorldPosition) + 
				", distance: " + distance + ", is final: " + (m_path.Count == 0) + ", dest world: " + m_destWorld);
		}

		/// <summary>
		/// Gets the top most entity of the destination entity, if it exists.
		/// </summary>
		/// <returns>The top most destination entity or null.</returns>
		private MyEntity GetTopMostDestEntity()
		{
			return m_pickedDestination.Entity != null ? (MyEntity)m_pickedDestination.Entity.GetTopMostParent() : null;
		}

		/// <summary>
		/// Get the entity autopilot will navigate relative to.
		/// </summary>
		/// <returns>The entity autopilot will navigate relative to.</returns>
		private MyEntity GetRelativeEntity()
		{
			Obstruction obstruct = m_obstructingEntity;
			return obstruct.Entity != null && obstruct.MatchPosition ? obstruct.Entity : GetTopMostDestEntity();
		}

		/// <summary>
		/// Tests whether of not the specified entity is moving away and is not the top-most destination entity.
		/// </summary>
		/// <param name="obstruction">The entity to test for moving away.</param>
		/// <returns>True iff the entity is moving away.</returns>
		private bool ObstructionMovingAway(MyEntity obstruction)
		{
			Log.DebugLog("obstruction == null", Logger.severity.FATAL, condition: obstruction == null);

			MyEntity topDest = GetTopMostDestEntity();
			Vector3D destVelocity = topDest != null && topDest.Physics != null ? topDest.Physics.LinearVelocity : Vector3.Zero;
			Vector3D obstVlocity = obstruction.Physics != null ? obstruction.Physics.LinearVelocity : Vector3.Zero;

			double distSq; Vector3D.DistanceSquared(ref destVelocity, ref obstVlocity, out distSq);
			if (distSq < 1d)
				return false;

			if (obstVlocity.LengthSquared() < 0.01d)
				return false;
			Vector3D position = obstruction.GetCentre();
			Vector3D nextPosition; Vector3D.Add(ref position, ref obstVlocity, out nextPosition);
			double current; Vector3D.DistanceSquared(ref m_currentPosition, ref position, out current);
			double next; Vector3D.DistanceSquared(ref m_currentPosition, ref nextPosition, out next);
			Log.DebugLog("Obstruction is moving away", condition: current < next);
			return current < next;
		}

		#endregion

		#region Collect Entities

		/// <summary>
		/// Fill m_entitiesPruneAvoid with nearby entities.
		/// Fill m_entitiesRepulse from m_entitiesPruneAvoid, ignoring some entitites.
		/// </summary>
		private void FillEntitiesLists()
		{
			Profiler.StartProfileBlock();

			m_entitiesPruneAvoid.Clear();
			m_entitiesRepulse.Clear();

			BoundingSphereD sphere = new BoundingSphereD() { Center = m_currentPosition, Radius = m_autopilotShipBoundingRadius + (m_autopilotVelocity.Length() + 1000f) * SpeedFactor };
			MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, m_entitiesPruneAvoid);

			foreach (MyEntity entity in CollectEntities(m_entitiesPruneAvoid))
				if (!NavSet.Settings_Current.ShouldIgnoreEntity(entity))
					m_entitiesRepulse.Add(entity);

			Profiler.EndProfileBlock();
		}

		/// <summary>
		/// Enumerable for entities which are not normally ignored.
		/// </summary>
		/// <param name="fromPruning">The list of entities from MyGamePruningStrucure</param>
		/// <returns>Enumerable for entities which are not normally ignored.</returns>
		private IEnumerable<MyEntity> CollectEntities(List<MyEntity> fromPruning)
		{
			for (int i = fromPruning.Count - 1; i >= 0; i--)
			{
				MyEntity entity = fromPruning[i];
				if (entity is MyCubeGrid)
				{
					MyCubeGrid grid = (MyCubeGrid)entity;
					if (grid == m_autopilotGrid)
						continue;
					if (!grid.Save)
						continue;
					if (AttachedGrid.IsGridAttached(m_autopilotGrid, grid, AttachedGrid.AttachmentKind.Physics))
						continue;
				}
				else if (entity is MyVoxelBase)
				{
					if (!m_ignoreVoxel && (entity is MyVoxelMap || entity is MyPlanet))
						yield return entity;
					continue;
				}
				else if (entity is MyAmmoBase)
				{
					yield return entity;
					continue;
				}
				else if (!(entity is MyFloatingObject))
					continue;

				if (entity.Physics != null && entity.Physics.Mass > 0f && entity.Physics.Mass < 1000f)
					continue;

				yield return entity;
			}
		}

		#endregion

		#region Test Path

		/// <summary>
		/// Test the current path for obstructions, sets the move direction and distance, and starts pathfinding if the current path cannot be travelled.
		/// </summary>
		private void TestCurrentPath()
		{
			if (m_path.HasTarget)
			{
				// don't chase an obstructing entity unless it is the destination
				if (ObstructionMovingAway(m_obstructingEntity.Entity))
				{
					Log.DebugLog("Obstruction is moving away, I'll just wait here", Logger.severity.TRACE);
					m_path.Clear();
					m_obstructingEntity = new Obstruction();
					m_obstructingBlock = null;
					m_holdPosition = true;
					return;
				}

				// if near waypoint, pop it
				double distSqCurToDest; Vector3D.DistanceSquared(ref m_currentPosition, ref m_destWorld, out distSqCurToDest);

				bool reachedDest;
				if (distSqCurToDest < 1d)
					reachedDest = true;
				else
				{
					Vector3D reached; m_path.GetReached(out reached);
					Vector3D obstructPosition = m_obstructingEntity.GetPosition();
					Vector3D reachedWorld; Vector3D.Add(ref reached, ref obstructPosition, out reachedWorld);
					double distSqReachToDest; Vector3D.DistanceSquared(ref reachedWorld, ref m_destWorld, out distSqReachToDest);
					reachedDest = distSqCurToDest < distSqReachToDest * 0.04d;
				}

				if (reachedDest)
				{
					m_path.ReachedTarget();
					Log.DebugLog("Reached waypoint: " + m_path.GetReached() + ", remaining: " + (m_path.Count - 1), Logger.severity.DEBUG);
					FillDestWorld();
					if (!m_path.IsFinished)
					{
						FillEntitiesLists();
						if (!SetNextPathTarget())
						{
							Log.DebugLog("Failed to set next target, clearing path");
							m_path.Clear();
						}
					}
				}
			}

			//Log.DebugLog("Current position: " + m_currentPosition + ", destination: " + m_destWorld);

			FillEntitiesLists();

			Vector3 repulsion;
			Vector3D disp; Vector3D.Subtract(ref m_destWorld, ref m_currentPosition, out disp);
			Vector3 dispF = disp;

			CalcRepulsion(!m_path.HasTarget && m_canChangeCourse, out repulsion);

			if (repulsion != Vector3.Zero)
			{
				if (dispF.LengthSquared() > 1e6f)
				{
					float distance = dispF.Length();
					Vector3 scaledRepulsion; Vector3.Multiply(ref repulsion, distance * 0.001f, out scaledRepulsion);
					Vector3.Add(ref dispF, ref scaledRepulsion, out m_moveDirection);
					Log.TraceLog("Scaled repulsion: " + repulsion + " * " + (distance * 0.001f) + " = " + scaledRepulsion + ", dispF: " + dispF + ", m_targetDirection: " + m_moveDirection);
				}
				else
				{
					Vector3.Add(ref dispF, ref repulsion, out m_moveDirection);
					Log.TraceLog("Repulsion: " + repulsion + ", dispF: " + dispF + ", m_targetDirection: " + m_moveDirection);
				}
			}
			else
			{
				Log.TraceLog("No repulsion, direction is displacement: " + dispF);
				m_moveDirection = dispF;
			}

			if (m_moveDirection == Vector3.Zero)
				m_moveLength = 0f;
			else
				m_moveLength = m_moveDirection.Normalize();

			MyEntity obstructing;
			MyCubeBlock block;
			float distReached;
			if (CurrentObstructed(out obstructing, out block, out distReached) && (!m_path.HasTarget || !TryRepairPath(distReached)))
			{
				if (ObstructionMovingAway(obstructing))
				{
					m_holdPosition = true;
					return;
				}

				m_obstructingEntity = new Obstruction() { Entity = obstructing, MatchPosition = m_canChangeCourse }; // when not MatchPosition, ship will still match with destination
				m_obstructingBlock = block;
				m_holdPosition = true;

				Log.DebugLog("Current path is obstructed by " + obstructing.getBestName() + "." + block.getBestName() + ", starting pathfinding", Logger.severity.DEBUG);
				StartPathfinding();
				return;
			}

			Log.TraceLog("Move direction: " + m_moveDirection + ", move distance: " + m_moveLength + ", disp: " + dispF);
			if (!m_path.HasTarget)
			{
				CurrentState = State.Unobstructed;
				m_obstructingEntity = new Obstruction();
				m_obstructingBlock = null;
			}

			m_holdPosition = false;
		}

		/// <summary>
		/// if autopilot is off course, try to move back towards the line from last reached to current node
		/// </summary>
		/// <param name="distReached">Estimated distance along the path before encountering an obstruction. </param>
		private bool TryRepairPath(float distReached)
		{
			if (distReached < 1f)
				return false;

			Vector3D lastReached; m_path.GetReached(out lastReached);
			Vector3D obstructPosition = m_obstructingEntity.GetPosition();
			Vector3D lastReachWorld; Vector3D.Add(ref lastReached, ref obstructPosition, out lastReachWorld);

			m_lineSegment.From = lastReachWorld;
			m_lineSegment.To = m_destWorld;

			Vector3D canTravelTo = m_currentPosition + m_moveDirection * distReached;
			Vector3D target;
			double tValue = m_lineSegment.ClosestPoint(ref canTravelTo, out target);

			PathTester.TestInput input;
			input.Offset = Vector3D.Zero;

			while (true)
			{
				Vector3D direction; Vector3D.Subtract(ref target, ref m_currentPosition, out direction);
				input.Length = (float)direction.Normalize();
				input.Direction = direction;

				if (input.Length < 1f)
				{
					Log.DebugLog("Already on line");
					break;
				}

				float proximity;
				if (CanTravelSegment(ref input, out proximity))
				{
					Log.DebugLog("can travel to line");
					m_moveDirection = input.Direction;
					m_moveLength = input.Length;
					return true;
				}

				tValue *= 0.5d;
				if (tValue <= 1d)
				{
					Log.DebugLog("Cannot reach line");
					break;
				}
				Vector3D disp; Vector3D.Multiply(ref direction, tValue, out disp);
				Vector3D.Add(ref lastReachWorld, ref disp, out target);
			}

			return SetNextPathTarget();
		}

		#endregion

		#region Calculate Repulsion

		/// <summary>
		/// Speed test entities in m_entitiesRepulse to determine which can be ignored, populates m_entitiesPruneAvoid.
		/// Optionally, calculates the repulsion from entities.
		/// </summary>
		/// <param name="calcRepulse">Iff true, repulsion from entities will be calculated.</param>
		/// <param name="repulsion">The repulsion vector from entities.</param>
		private void CalcRepulsion(bool calcRepulse, out Vector3 repulsion)
		{
			Profiler.StartProfileBlock();
			m_entitiesPruneAvoid.Clear();
			m_clusters.Clear();

			float distAutopilotToFinalDest = NavSet.Settings_Current.Distance;
			Vector3 autopilotVelocity = m_autopilotVelocity;
			m_checkVoxel = false;
			float apRadius = m_autopilotShipBoundingRadius;
			float closestEntityDist = float.MaxValue;

			for (int index = m_entitiesRepulse.Count - 1; index >= 0; index--)
			{
				MyEntity entity = m_entitiesRepulse[index];

				Log.DebugLog("entity is null", Logger.severity.FATAL, condition: entity == null);
				Log.DebugLog("entity is not top-most", Logger.severity.FATAL, condition: entity.Hierarchy != null && entity.Hierarchy.Parent != null);

				Vector3D centre = entity.GetCentre();
				Vector3D toCentreD;
				Vector3D.Subtract(ref centre, ref m_currentPosition, out toCentreD);
				double distCentreToCurrent = toCentreD.Normalize();
				Vector3 toCentre = toCentreD;
				float linearSpeedFactor;
				double distSqCentreToDest; Vector3D.DistanceSquared(ref centre, ref m_destWorld, out distSqCentreToDest);

				// determine speed of convergence
				if (entity.Physics == null)
					Vector3.Dot(ref autopilotVelocity, ref toCentre, out linearSpeedFactor);
				else
				{
					Vector3 obVel = entity.Physics.LinearVelocity;
					if (entity is MyAmmoBase)
						// missiles accelerate and have high damage regardless of speed so avoid them more
						obVel.X *= 10f; obVel.Y *= 10f; obVel.Z *= 10f;

					Vector3 relVel;
					Vector3.Subtract(ref autopilotVelocity, ref  obVel, out relVel);
					Vector3.Dot(ref relVel, ref toCentre, out linearSpeedFactor);
				}
				if (linearSpeedFactor <= 0f)
					linearSpeedFactor = 0f;
				else
					linearSpeedFactor *= SpeedFactor;

				MyPlanet planet = entity as MyPlanet;
				if (planet != null)
				{
					float maxRadius = planet.MaximumRadius;
					if (distCentreToCurrent - apRadius - linearSpeedFactor < maxRadius)
						m_checkVoxel = true;
					if (calcRepulse)
					{
						float gravLimit = planet.GetGravityLimit() + 4000f;
						float gllsf = gravLimit + linearSpeedFactor;
						if (gllsf * gllsf < distSqCentreToDest)
						{
							// destination is not near planet
							SphereClusters.RepulseSphere sphere = new SphereClusters.RepulseSphere()
							{
								Centre = centre,
								FixedRadius = maxRadius,
								VariableRadius = gravLimit - maxRadius + linearSpeedFactor
							};
							sphere.SetEntity(entity);
							//Log.TraceLog("Far planet sphere: " + sphere);
							m_clusters.Add(ref sphere);
						}
						else
						{
							// destination is near planet, keep autopilot further from the planet based on speed and distance to destination
							SphereClusters.RepulseSphere sphere = new SphereClusters.RepulseSphere()
							{
								Centre = centre,
								FixedRadius = Math.Sqrt(distSqCentreToDest),
								VariableRadius = Math.Min(linearSpeedFactor + distAutopilotToFinalDest * 0.1f, distAutopilotToFinalDest * 0.25f)
							};
							sphere.SetEntity(entity);
							//Log.TraceLog("Nearby planet sphere: " + sphere);
							m_clusters.Add(ref sphere);
						}
					}
					continue;
				}

				//boundingRadius += entity.PositionComp.LocalVolume.Radius;

				float fixedRadius  = apRadius + entity.PositionComp.LocalVolume.Radius;

				if (distCentreToCurrent < fixedRadius + linearSpeedFactor)
				{
					// Entity is too close to autopilot for repulsion.
					float distBetween = (float)distCentreToCurrent - fixedRadius;
					if (distBetween < closestEntityDist)
						closestEntityDist = distBetween;
					AvoidEntity(entity);
					continue;
				}

				fixedRadius = Math.Min(fixedRadius * 4f, fixedRadius + 2000f);

				if (distSqCentreToDest < fixedRadius * fixedRadius)
				{
					if (distAutopilotToFinalDest < distCentreToCurrent)
					{
						// Entity is near destination and autopilot is nearing destination
						AvoidEntity(entity);
						continue;
					}

					double minGain = entity.PositionComp.LocalVolume.Radius;
					if (minGain <= 10d)
						minGain = 10d;

					if (distSqCentreToDest < minGain * minGain)
					{
						// Entity is too close to destination for cicling it to be much use
						AvoidEntity(entity);
						continue;
					}
				}

				if (calcRepulse)
				{
					SphereClusters.RepulseSphere sphere = new SphereClusters.RepulseSphere()
					{
						Centre = centre,
						FixedRadius = fixedRadius,
						VariableRadius = linearSpeedFactor
					};
					sphere.SetEntity(entity);
					//Log.TraceLog("sphere: " + sphere);
					m_clusters.Add(ref sphere);
				}
			}

			NavSet.Settings_Task_NavWay.SpeedMaxRelative = Math.Max(closestEntityDist * Mover.DistanceSpeedFactor, 5f);

			// when following a path, only collect entites to avoid, do not repulse
			if (!calcRepulse)
			{
				Profiler.EndProfileBlock();
				repulsion = Vector3.Zero;
				return;
			}

			m_clusters.AddMiddleSpheres();

			//Log.DebugLog("repulsion spheres: " + m_clusters.Clusters.Count);

			repulsion = Vector3.Zero;
			for (int indexO = m_clusters.Clusters.Count - 1; indexO >= 0; indexO--)
			{
				List<SphereClusters.RepulseSphere> cluster = m_clusters.Clusters[indexO];
				for (int indexI = cluster.Count - 1; indexI >= 0; indexI--)
				{
					SphereClusters.RepulseSphere sphere = cluster[indexI];
					CalcRepulsion(ref sphere, ref repulsion);
				}
			}

			Profiler.EndProfileBlock();
		}

		/// <summary>
		/// For most entities add it to m_entitiesPruneAvoid. For voxel set m_checkVoxel.
		/// </summary>
		/// <param name="entity">If it is a voxel, set m_checkVoxel = true, otherwise added to m_entitiesPruneAvoid.</param>
		private void AvoidEntity(MyEntity entity)
		{
			Log.TraceLog("Avoid: " + entity.nameWithId());
			if (entity is MyVoxelBase)
				m_checkVoxel = true;
			else
				m_entitiesPruneAvoid.Add(entity);
		}

		/// <summary>
		/// Calculate repulsion for a specified sphere.
		/// </summary>
		/// <param name="sphere">The sphere which is repulsing the autopilot.</param>
		/// <param name="repulsion">Vector to which the repulsive force of sphere will be added.</param>
		private void CalcRepulsion(ref SphereClusters.RepulseSphere sphere, ref Vector3 repulsion)
		{
			Vector3D toCurrentD;
			Vector3D.Subtract(ref m_currentPosition, ref sphere.Centre, out toCurrentD);
			Vector3 toCurrent = toCurrentD;
			float toCurrentLenSq = toCurrent.LengthSquared();

			float maxRepulseDist = (float)sphere.RepulseRadius;
			float maxRepulseDistSq = maxRepulseDist * maxRepulseDist;

			if (toCurrentLenSq > maxRepulseDistSq)
			{
				// Autopilot is outside the maximum bounds of repulsion.
				return;
			}

			float toCurrentLen = (float)Math.Sqrt(toCurrentLenSq);
			float repulseMagnitude = maxRepulseDist - toCurrentLen;

			Vector3 sphereRepulsion; Vector3.Multiply(ref toCurrent, repulseMagnitude / toCurrentLen, out sphereRepulsion);
			Log.TraceLog("repulsion of " + sphereRepulsion + " from sphere: " + sphere);
			repulsion.X += sphereRepulsion.X;
			repulsion.Y += sphereRepulsion.Y;
			repulsion.Z += sphereRepulsion.Z;
		}

		#endregion

		#region Obstruction Test

		/// <summary>
		/// For testing if the current path is obstructed by any entity in m_entitiesPruneAvoid.
		/// </summary>
		/// <param name="obstructingEntity">An entity that is blocking the current path.</param>
		/// <param name="obstructBlock">The block to report as blocking the path.</param>
		/// <param name="distance">Estimated distance along the path before encountering an obstruction.</param>
		/// <returns>True iff the current path is obstructed.</returns>
		private bool CurrentObstructed(out MyEntity obstructingEntity, out MyCubeBlock obstructBlock, out float distance)
		{
			//Log.DebugLog("m_moveDirection: " + m_moveDirection, Logger.severity.FATAL, condition: Math.Abs(m_moveDirection.LengthSquared() - 1f) > 0.01f);
			//Log.DebugLog("m_moveLength: " + m_moveLength, Logger.severity.FATAL, condition: Math.Abs(m_moveLength) < 0.1f);

			MyCubeBlock ignoreBlock = NavSet.Settings_Current.DestinationEntity as MyCubeBlock;

			// if destination is obstructing it needs to be checked first, so we would match speed with destination

			MyEntity destTop = GetTopMostDestEntity();
			PathTester.TestInput input = new PathTester.TestInput() { Direction = m_moveDirection, Length = m_moveLength };
			PathTester.TestInput adjustedInput;

			if (destTop != null && m_pickedDestination.Position != Vector3D.Zero && m_entitiesPruneAvoid.Contains(destTop))
			{
				m_tester.AdjustForCurrentVelocity(ref input, out adjustedInput, destTop, true);
				if (adjustedInput.Length != 0f)
				{
					PathTester.GridTestResult result;
					if (m_tester.ObstructedBy(destTop, ignoreBlock, ref adjustedInput, out result))
					{
						//Log.DebugLog("Obstructed by " + destTop.nameWithId() + "." + obstructBlock, Logger.severity.DEBUG);
						obstructingEntity = destTop;
						obstructBlock = result.ObstructingBlock;
						distance = result.Distance;
						return true;
					}
				}
			}

			// check voxel next so that the ship will not match an obstruction that is on a collision course

			if (m_checkVoxel)
			{
				//Log.DebugLog("raycasting voxels");
				m_tester.AdjustForCurrentVelocity(ref input, out adjustedInput, null, false);
				if (adjustedInput.Length != 0f)
				{
					PathTester.VoxelTestResult voxelResult;
					if (m_tester.RayCastIntersectsVoxel(ref adjustedInput, out voxelResult))
					{
						//Log.DebugLog("Obstructed by voxel " + hitVoxel + " at " + hitPosition+ ", autopilotVelocity: " + autopilotVelocity + ", m_moveDirection: " + m_moveDirection);
						obstructingEntity = voxelResult.ObstructingVoxel;
						obstructBlock = null;
						distance = voxelResult.Distance;
						return true;
					}
				}
			}

			// check remaining entities

			//Log.DebugLog("checking " + m_entitiesPruneAvoid.Count + " entites - destination entity - voxels");

			if (m_entitiesPruneAvoid.Count != 0)
				for (int i = m_entitiesPruneAvoid.Count - 1; i >= 0; i--)
				{
					//Log.DebugLog("entity: " + m_entitiesPruneAvoid[i]);
					obstructingEntity = m_entitiesPruneAvoid[i];
					if (obstructingEntity == destTop || obstructingEntity is MyVoxelBase)
						// already checked
						continue;
					m_tester.AdjustForCurrentVelocity(ref input, out adjustedInput, obstructingEntity, false);
					if (adjustedInput.Length != 0f)
					{
						PathTester.GridTestResult result;
						if (m_tester.ObstructedBy(obstructingEntity, ignoreBlock, ref adjustedInput, out result))
						{
							//Log.DebugLog("Obstructed by " + obstructingEntity.nameWithId() + "." + obstructBlock, Logger.severity.DEBUG);
							obstructBlock = (MyCubeBlock)result.ObstructingBlock;
							distance = result.Distance;
							return true;
						}
					}
				}

			//Log.DebugLog("No obstruction");
			obstructingEntity = null;
			obstructBlock = null;
			distance = 0f;
			return false;
		}

		#endregion

		#region Jump

		/// <summary>
		/// Attempt to jump the ship towards m_pickedDestination.
		/// </summary>
		/// <returns>True if the ship is going to jump.</returns>
		private bool TryJump()
		{
			if (Globals.UpdateCount < m_nextJumpAttempt)
				return false;
			m_nextJumpAttempt = Globals.UpdateCount + 100uL;
			if (NavSet.Settings_Current.MinDistToJump < MyGridJumpDriveSystem.MIN_JUMP_DISTANCE || NavSet.Settings_Current.Distance < NavSet.Settings_Current.MinDistToJump)
			{
				//Log.DebugLog("not allowed to jump, distance: " + NavSet.Settings_Current.Distance + ", min dist: " + NavSet.Settings_Current.MinDistToJump);
				JumpComplaint = InfoString.StringId_Jump.None;
				return false;
			}

			// search for a drive
			foreach (IMyCubeGrid grid in AttachedGrid.AttachedGrids(Mover.Block.CubeGrid, AttachedGrid.AttachmentKind.Terminal, true))
			{
				CubeGridCache cache = CubeGridCache.GetFor(grid);
				if (cache == null)
				{
					Log.DebugLog("Missing a CubeGridCache");
					return false;
				}
				foreach (MyJumpDrive jumpDrive in cache.BlocksOfType(typeof(MyObjectBuilder_JumpDrive)))
					if (jumpDrive.CanJumpAndHasAccess(Mover.Block.CubeBlock.OwnerId))
					{
						m_nextJumpAttempt = Globals.UpdateCount + 1000uL;
						return RequestJump();
					}
			}

			//Log.DebugLog("No usable jump drives", Logger.severity.TRACE);
			JumpComplaint = InfoString.StringId_Jump.NotCharged;
			return false;
		}

		/// <summary>
		/// Based on MyGridJumpDriveSystem.RequestJump. If the ship is allowed to jump, requests the jump system jump the ship.
		/// </summary>
		/// <returns>True if Pathfinder has requested a jump.</returns>
		private bool RequestJump()
		{
			MyCubeBlock apBlock = (MyCubeBlock)Mover.Block.CubeBlock;
			MyCubeGrid apGrid = apBlock.CubeGrid;

			if (!Vector3.IsZero(MyGravityProviderSystem.CalculateNaturalGravityInPoint(apGrid.WorldMatrix.Translation)))
			{
				//Log.DebugLog("Cannot jump, in gravity");
				JumpComplaint = InfoString.StringId_Jump.InGravity;
				return false;
			}

			// check for static grids or grids already jumping
			foreach (MyCubeGrid grid in AttachedGrid.AttachedGrids(apGrid, AttachedGrid.AttachmentKind.Physics, true))
			{
				if (grid.MarkedForClose)
				{
					Log.DebugLog("Cannot jump with closing grid: " + grid.nameWithId(), Logger.severity.WARNING);
					JumpComplaint = InfoString.StringId_Jump.ClosingGrid;
					return false;
				}
				if (grid.IsStatic)
				{
					Log.DebugLog("Cannot jump with static grid: " + grid.nameWithId(), Logger.severity.WARNING);
					JumpComplaint = InfoString.StringId_Jump.StaticGrid;
					return false;
				}
				if (grid.GridSystems.JumpSystem.GetJumpDriveDirection().HasValue)
				{
					Log.DebugLog("Cannot jump, grid already jumping: " + grid.nameWithId(), Logger.severity.WARNING);
					JumpComplaint = InfoString.StringId_Jump.AlreadyJumping;
					return false;
				}
			}

			Vector3D finalDest = m_pickedDestination.WorldPosition();

			if (MySession.Static.Settings.WorldSizeKm > 0 && finalDest.Length() > MySession.Static.Settings.WorldSizeKm * 500)
			{
				Log.DebugLog("Cannot jump outside of world", Logger.severity.WARNING);
				JumpComplaint = InfoString.StringId_Jump.DestOutsideWorld;
				return false;
			}

			MyGridJumpDriveSystem jdSystem =apGrid.GridSystems.JumpSystem;
			Vector3D jumpDisp; Vector3D.Subtract(ref finalDest, ref m_currentPosition, out jumpDisp);

			// limit jump to maximum allowed by jump drive capacity
			double maxJumpDistance = jdSystem.GetMaxJumpDistance(apBlock.OwnerId);
			double jumpDistSq = jumpDisp.LengthSquared();
			if (maxJumpDistance * maxJumpDistance < jumpDistSq)
			{
				Log.DebugLog("Jump drives do not have sufficient power to jump the desired distance", Logger.severity.DEBUG);
				Vector3D newJumpDisp; Vector3D.Multiply(ref jumpDisp, maxJumpDistance / Math.Sqrt(jumpDistSq), out newJumpDisp);
				jumpDisp = newJumpDisp;

				if (newJumpDisp.LengthSquared() < MyGridJumpDriveSystem.MIN_JUMP_DISTANCE * MyGridJumpDriveSystem.MIN_JUMP_DISTANCE)
				{
					Log.DebugLog("Jump drives do not have sufficient power to jump the minimum distance", Logger.severity.WARNING);
					JumpComplaint = InfoString.StringId_Jump.CannotJumpMin;
					return false;
				}
			}

			// limit jump based on obstruction
			m_lineSegment.From = m_currentPosition;
			m_lineSegment.To = finalDest;
			double apRadius = apGrid.PositionComp.LocalVolume.Radius;
			LineD line = m_lineSegment.Line;
			List<MyLineSegmentOverlapResult<MyEntity>> overlappingEntities; ResourcePool.Get(out overlappingEntities);
			MyGamePruningStructure.GetTopmostEntitiesOverlappingRay(ref line, overlappingEntities);
			MyLineSegmentOverlapResult<MyEntity> closest = new MyLineSegmentOverlapResult<MyEntity>() { Distance = double.MaxValue };
			foreach (MyLineSegmentOverlapResult<MyEntity> overlap in overlappingEntities)
			{
				MyPlanet planet = overlap.Element as MyPlanet;
				if (planet != null)
				{
					BoundingSphereD gravSphere = new BoundingSphereD(planet.PositionComp.GetPosition(), planet.GetGravityLimit() + apRadius);
					double t1, t2;
					if (m_lineSegment.Intersects(ref gravSphere, out t1, out t2))
					{
						if (t1 < closest.Distance)
							closest = new MyLineSegmentOverlapResult<MyEntity>() { Distance = t1, Element = planet };
					}
					else
					{
						Log.DebugLog("planet gravity does not hit line: " + planet.getBestName());
						continue;
					}
				}
				else
				{
					if (overlap.Element == apGrid)
						continue;
					if (overlap.Distance < closest.Distance)
						closest = overlap;
				}
			}
			overlappingEntities.Clear();
			ResourcePool.Return(overlappingEntities);

			if (closest.Element != null)
			{
				Log.DebugLog("jump would hit: " + closest.Element.nameWithId() + ", shortening jump from " + m_lineSegment.Length + " to " + closest.Distance, Logger.severity.DEBUG);

				if (closest.Distance < MyGridJumpDriveSystem.MIN_JUMP_DISTANCE)
				{
					Log.DebugLog("Jump is obstructed", Logger.severity.DEBUG);
					JumpComplaint = InfoString.StringId_Jump.Obstructed;
					return false;
				}

				m_lineSegment.To = m_lineSegment.From + m_lineSegment.Direction * closest.Distance;
			}

			Log.DebugLog("Requesting jump to " + m_lineSegment.To, Logger.severity.DEBUG);
			JumpComplaint = InfoString.StringId_Jump.Jumping;
			// if jump fails after request, wait a long time before trying again
			m_nextJumpAttempt = 10000uL;
			Static.JumpDisplacement.SetValue(jdSystem, m_lineSegment.To - m_lineSegment.From);
			Static.RequestJump.Invoke(jdSystem, new object[] { m_lineSegment.To, apBlock.OwnerId });
			m_jumpSystem = jdSystem;
			return true;
		}

		#endregion

	}
}
