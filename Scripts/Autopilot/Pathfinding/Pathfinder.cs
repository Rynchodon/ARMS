using System;
using System.Collections.Generic;
using System.Reflection;
using Rynchodon.AntennaRelay;
using Rynchodon.Attached;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Rynchodon.Settings;
using Rynchodon.Threading;
using Rynchodon.Utility;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinding
{
	public partial class Pathfinder
	{

		public const float SpeedFactor = 3f, VoxelAdd = 10f;
		private const float DefaultNodeDistance = 100f;

		public enum State : byte
		{
			Unobstructed, SearchingForPath, FollowingPath, FailedToFindPath, Crashed
		}

		private class StaticVariables
		{
			public ThreadManager ThreadForeground, ThreadBackground;
			public MethodInfo RequestJump;
			public FieldInfo JumpDisplacement;

			public StaticVariables()
			{
			Logger.DebugLog("entered", Logger.severity.TRACE);
				byte allowedThread = ServerSettings.GetSetting<byte>(ServerSettings.SettingName.yParallelPathfinder);
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

		private readonly Logger m_logger;
		private readonly PathTester m_tester;

		private readonly FastResourceLock m_runningLock = new FastResourceLock();
		private bool m_runInterrupt, m_runHalt;
		private ulong m_waitUntil, m_nextJumpAttempt;
		private LineSegmentD m_lineSegment = new LineSegmentD();
		private MyGridJumpDriveSystem m_jumpSystem;

		// Inputs: rarely change

		private MyCubeGrid m_autopilotGrid { get { return m_tester.AutopilotGrid; } set { m_tester.AutopilotGrid = value; } }
		private PseudoBlock m_navBlock;
		private Destination m_destination; // only match speed with entity if there is no obstruction
		private MyEntity m_ignoreEntity;
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

		// Low Level

		private Path m_path = new Path(false);
		private float m_nodeDistance = DefaultNodeDistance;
		private double m_destRadiusSq { get { return m_nodeDistance * m_nodeDistance * 0.04f; } }
		private PathNodeSet m_forward = new PathNodeSet(false), m_backward = new PathNodeSet(false);
		private bool value_pathfinding;
#if PROFILE
		private DateTime m_timeStartPathfinding;
#endif
		private bool m_pathfinding
		{
			get { return value_pathfinding; }
			set
			{
				if (value_pathfinding == value)
					return;
				value_pathfinding = value;
				if (value)
				{
					m_forward.Clear();
					m_backward.Clear();
					m_nodeDistance = DefaultNodeDistance;
					//Logger.DebugNotify("Started Pathfinding", level: Logger.severity.INFO);
					m_logger.debugLog("Started pathfinding", Logger.severity.INFO);
				}
				else
					m_waitUntil = 0uL;
#if PROFILE
				if (value)
					m_timeStartPathfinding = DateTime.UtcNow;
				else
				{
					TimeSpan timeSpentPathfinding = DateTime.UtcNow - m_timeStartPathfinding;
					m_logger.debugLog("Spent " + PrettySI.makePretty(timeSpentPathfinding) + " pathfinding");
				}
#endif
			}
		}

		// Results

		private Obstruction m_obstructingEntity;
		private MyCubeBlock m_obstructingBlock;
		private bool m_holdPosition = true;

		private FastResourceLock lock_moveDisplacment = new FastResourceLock();
		private Destination value_moveDestination;

		private Destination m_moveDisplacement
		{
			get { using (lock_moveDisplacment.AcquireSharedUsing()) return value_moveDestination; }
			set { using (lock_moveDisplacment.AcquireExclusiveUsing()) value_moveDestination = value; }
		}

		public MyEntity ReportedObstruction { get { return m_obstructingBlock ?? m_obstructingEntity.Entity; } }
		public Mover Mover { get; private set; }
		public AllNavigationSettings NavSet { get { return Mover.NavSet; } }
		public RotateChecker RotateCheck { get { return Mover.RotateCheck; } }
		public State CurrentState { get; private set; }
		public InfoString.StringId_Jump JumpComplaint { get; private set; }

		public Pathfinder(ShipControllerBlock block)
		{
			m_logger = new Logger(() => m_autopilotGrid.getBestName(), () => {
				if (m_navBlock != null)
					return m_navBlock.DisplayName;
				return "N/A";
			}, () => CurrentState.ToString());
			Mover = new Mover(block, new RotateChecker(block.CubeBlock, CollectEntities));
			m_tester = new PathTester(Mover.Block);
		}

		// Methods

		public void Halt()
		{
			m_runHalt = true;
		}

		public void HoldPosition(Vector3 velocity)
		{
			m_logger.debugLog("Not on autopilot thread: " + ThreadTracker.ThreadName, Logger.severity.ERROR, condition: !ThreadTracker.ThreadName.StartsWith("Autopilot"));

			m_runInterrupt = true;
			m_runHalt = false;
			Mover.CalcMove(NavSet.Settings_Current.NavigationBlock, ref Vector3.Zero, ref velocity);
		}

		public void MoveTo(PseudoBlock navBlock, LastSeen targetEntity, Vector3D offset = default(Vector3D), Vector3 addToVelocity = default(Vector3))
		{
			m_runHalt = false;

			Destination dest;
			if (targetEntity.isRecent())
			{
				dest = new Destination(targetEntity.Entity, offset);
				m_logger.debugLog("recent. dest: " + dest);
			}
			else
			{
				dest = new Destination(targetEntity.LastKnownPosition + offset);
				addToVelocity += targetEntity.LastKnownVelocity;
				m_logger.debugLog("not recent. dest: " + dest + ", actual position: " + targetEntity.Entity.GetPosition());
			}
			MoveTo(navBlock, ref dest, addToVelocity);
		}

		public void MoveTo(PseudoBlock navBlock, ref Destination destination, Vector3 addToVelocity = default(Vector3))
		{
			m_runHalt = false;
			Move();

			AllNavigationSettings.SettingsLevel level = Mover.NavSet.Settings_Current;
			MyEntity ignoreEntity = (MyEntity)level.DestinationEntity;
			bool ignoreVoxel = level.IgnoreAsteroid;
			bool canChangeCourse = level.PathfinderCanChangeCourse;

			m_addToVelocity = addToVelocity;

			if (m_navBlock == navBlock && m_autopilotGrid == navBlock.Grid && m_destination.Equals(ref destination) && m_ignoreEntity == ignoreEntity && m_ignoreVoxel == ignoreVoxel && m_canChangeCourse == canChangeCourse)
			{
				if (m_destination.Position != destination.Position)
					// values are close so a partial write shouldn't cause weirdness
					m_destination.Position = destination.Position;
				Static.ThreadForeground.EnqueueAction(Run);
				return;
			}

			m_logger.debugLog("nav block changed from " + (m_navBlock == null ? "N/A" : m_navBlock.DisplayName) + " to " + navBlock.DisplayName, condition: m_navBlock != navBlock);
			m_logger.debugLog("grid changed from " + m_autopilotGrid.getBestName() + " to " + navBlock.Grid.getBestName(), condition: m_autopilotGrid != navBlock.Grid);
			m_logger.debugLog("destination changed from " + m_destination + " to " + destination, condition: !m_destination.Equals(ref destination));
			m_logger.debugLog("ignore entity changed from " + m_ignoreEntity.getBestName() + " to " + ignoreEntity.getBestName(), condition: m_ignoreEntity != ignoreEntity);
			m_logger.debugLog("ignore voxel changed from " + m_ignoreVoxel + " to " + ignoreVoxel, condition: m_ignoreVoxel != ignoreVoxel);
			m_logger.debugLog("can change course changed from " + m_canChangeCourse + " to " + canChangeCourse, condition: m_canChangeCourse != canChangeCourse);

			using (m_runningLock.AcquireExclusiveUsing())
			{
				m_runInterrupt = true;
				m_navBlock = navBlock;
				m_autopilotGrid = (MyCubeGrid)m_navBlock.Grid;
				m_destination = destination;
				m_ignoreEntity = ignoreEntity;
				m_ignoreVoxel = ignoreVoxel;
				m_canChangeCourse = canChangeCourse;
				m_holdPosition = true;
			}

			Static.ThreadForeground.EnqueueAction(Run);
		}

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
					m_pathfinding = false;
					m_runInterrupt = false;
				}
				else if (m_pathfinding)
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
							m_logger.debugLog("Jump completed, distance travelled: " + (distance - NavSet.Settings_Current.Distance), Logger.severity.INFO);
							m_nextJumpAttempt = 0uL;
							JumpComplaint = InfoString.StringId_Jump.None;
						}
						else
						{
							m_logger.debugLog("Jump failed, distance travelled: " + (distance - NavSet.Settings_Current.Distance), Logger.severity.WARNING);
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
				CurrentState = State.Crashed;
				throw;
			}
			finally { m_runningLock.ReleaseExclusive(); }

			PostRun();
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
			else if (m_pathfinding)
				Static.ThreadBackground.EnqueueAction(ContinuePathfinding);
		}

		private void FillDestWorld()
		{
			m_currentPosition = m_autopilotGrid.GetCentre();
			Vector3D finalDestWorld = m_destination.WorldPosition() + m_currentPosition - m_navBlock.WorldPosition;
			double distance; Vector3D.Distance(ref finalDestWorld, ref m_currentPosition, out distance);
			NavSet.Settings_Current.Distance = (float)distance;

			m_logger.debugLog("Missing obstruction", Logger.severity.ERROR, condition: m_path.HasTarget && m_obstructingEntity.Entity == null);

			m_destWorld = m_path.HasTarget ? m_path.GetTarget() + m_obstructingEntity.GetPosition() : finalDestWorld;

			//m_logger.debugLog("final dest world: " + finalDestWorld + ", distance: " + distance + ", is final: " + (m_path.Count == 0) + ", dest world: " + m_destWorld);
		}

		private void OnComplete()
		{
			m_entitiesPruneAvoid.Clear();
			m_entitiesRepulse.Clear();
		}

		private MyEntity GetRelativeEntity()
		{
			Obstruction obstruct = m_obstructingEntity;
			return obstruct.Entity != null && obstruct.MatchPosition ? obstruct.Entity : (MyEntity)m_destination.Entity;
		}

		/// <summary>
		/// Call on autopilot thread. Instructs Mover to calculate movement.
		/// </summary>
		private void Move()
		{
			Destination moveDisp = m_moveDisplacement;
			Vector3 targetVelocity = moveDisp.Entity != null && moveDisp.Entity.Physics != null && !moveDisp.Entity.Physics.IsStatic ? moveDisp.Entity.Physics.LinearVelocity : Vector3.Zero;
			if (!m_path.HasTarget)
			{
				Vector3 temp; Vector3.Add(ref targetVelocity, ref m_addToVelocity, out temp);
				targetVelocity = temp;
			}

			if (m_holdPosition)
			{
				//m_logger.debugLog("Holding position");
				Mover.CalcMove(m_navBlock, ref Vector3.Zero, ref targetVelocity);
				return;
			}

			m_logger.debugLog("Should not be moving! moveDisp: " + moveDisp, Logger.severity.ERROR, condition: CurrentState != State.Unobstructed && CurrentState != State.FollowingPath);
			Vector3D currentPosition = m_autopilotGrid.GetCentre();
			Vector3D destDisp = moveDisp.WorldPosition();
			Vector3 disp = destDisp;
			//m_logger.debugLog("moving: " + disp);
			Mover.CalcMove(m_navBlock, ref disp, ref targetVelocity);
		}

		private void TestCurrentPath()
		{
			if (m_path.HasTarget)
			{
				// don't chase an obstructing entity unless it is the destination
				if (m_obstructingEntity.Entity != m_destination.Entity && ObstructionMovingAway())
				{
					m_logger.debugLog("Obstruction is moving away, I'll just wait here", Logger.severity.DEBUG);
					m_path.Clear();
					m_obstructingEntity = new Obstruction();
					m_obstructingBlock = null;
					m_holdPosition = true;
					return;
				}

				CurrentState = State.FollowingPath;

				// if near waypoint, pop it
				double distanceToDest; Vector3D.DistanceSquared(ref m_currentPosition, ref m_destWorld, out distanceToDest);
				if (distanceToDest < m_destRadiusSq)
				{
					m_path.ReachedTarget();
					m_logger.debugLog("Reached waypoint: " + m_path.GetReached() + ", remaining: " + (m_path.Count - 1), Logger.severity.DEBUG);
					if (m_path.IsFinished)
					{
						//Logger.DebugNotify("Completed path", level: Logger.severity.INFO);
						m_nodeDistance = DefaultNodeDistance;
					}
					else
						SetNextPathTarget();
					FillDestWorld();
				}
			}

			//m_logger.debugLog("Current position: " + m_currentPosition + ", destination: " + m_destWorld);

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
					//m_logger.debugLog("Scaled repulsion: " + repulsion + " * " + (distance * 0.001f) + " = " + scaledRepulsion + ", dispF: " + dispF + ", m_targetDirection: " + m_moveDirection);
				}
				else
				{
					Vector3.Add(ref dispF, ref repulsion, out m_moveDirection);
					//m_logger.debugLog("Repulsion: " + repulsion + ", dispF: " + dispF + ", m_targetDirection: " + m_moveDirection);
				}
			}
			else
			{
				m_moveDirection = dispF;
			}

			MyEntity relative = GetRelativeEntity();
			m_moveDisplacement = relative != null ? Destination.FromWorld(relative, m_moveDirection) : new Destination(m_moveDirection);
			m_moveLength = m_moveDirection.Normalize();

			MyEntity obstructing;
			MyCubeBlock block;
			if (CurrentObstructed(out obstructing, out block) && (!m_path.HasTarget || !TryRepairPath(ref disp)))
			{
				m_obstructingEntity = new Obstruction() { Entity = obstructing, MatchPosition = m_canChangeCourse };
				m_obstructingBlock = block;

				m_forward.Clear();
				m_backward.Clear();

				m_holdPosition = true;
				if (ObstructionMovingAway())
					return;
				FindAPath();
				return;
			}

			//m_logger.debugLog("Move direction: " + m_moveDirection + ", move distance: " + m_moveLength);
			if (!m_path.HasTarget)
			{
				CurrentState = State.Unobstructed;
				m_obstructingEntity = new Obstruction();
				m_obstructingBlock = null;
			}

			m_holdPosition = false;
		}

		private bool TryRepairPath(ref Vector3D disp)
		{
			// if autopilot is off course, try to move back towards the line from last reached to current node

			//m_logger.debugLog("entered");

			Vector3D obstructPosition = m_obstructingEntity.GetPosition();
			Vector3D lastReached; m_path.GetReached(out lastReached);
			Vector3D lastReachedWorldPos; Vector3D.Add(ref lastReached, ref obstructPosition, out lastReachedWorldPos);

			m_lineSegment.From = lastReachedWorldPos;
			m_lineSegment.To = m_destWorld;

			Vector3D moveToPoint; m_lineSegment.ClosestPoint(ref m_currentPosition, out moveToPoint);
			double distSquared; Vector3D.DistanceSquared(ref m_currentPosition, ref moveToPoint, out distSquared);

			if (distSquared > 1d)
			{
				Line moveLine = new Line(m_currentPosition, moveToPoint);
				if (!CanTravelSegment(ref Vector3D.Zero, ref moveLine))
				{
					m_logger.debugLog("Cannot reach point on line");
					return SetNextPathTarget();
				}
				else
				{
					Vector3D.Subtract(ref moveToPoint, ref m_currentPosition, out disp);
					m_moveDirection = disp;
					m_moveLength = m_moveDirection.Normalize();
					m_logger.debugLog("Moving to point(" + moveToPoint + ") on line between last reached(" + m_lineSegment.From + ") and current node(" + m_lineSegment.To + ") that is closest to current position(" + m_currentPosition + ")." +
						" m_moveDirection: " + m_moveDirection);
					return true;
				}
			}
			else
			{
				m_logger.debugLog("Near point on line between current and next node, failed to follow path", Logger.severity.DEBUG);
				return SetNextPathTarget();
			}
		}

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
				m_entitiesRepulse.Add(entity);

			Profiler.EndProfileBlock();
		}

		/// <summary>
		/// Enumerable for entities which will not be ignored.
		/// </summary>
		private IEnumerable<MyEntity> CollectEntities(List<MyEntity> fromPruning)
		{
			for (int i = fromPruning.Count - 1; i >= 0; i--)
			{
				MyEntity entity = fromPruning[i];
				if (m_ignoreEntity != null && m_ignoreEntity == entity)
					continue;
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

				m_logger.debugLog("entity is null", Logger.severity.FATAL, condition: entity == null);
				m_logger.debugLog("entity is not top-most", Logger.severity.FATAL, condition: entity.Hierarchy != null && entity.Hierarchy.Parent != null);

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
					if (distCentreToCurrent - apRadius - linearSpeedFactor < planet.MaximumRadius)
						m_checkVoxel = true;
					if (calcRepulse)
					{
						float gravLimit = planet.GetGravityLimit() + 4000f;
						float gllsf = gravLimit + linearSpeedFactor;
						if (gllsf * gllsf < distSqCentreToDest)
						{
							// destination is not near planet, use gravity limit
							m_logger.debugLog("Far planet sphere: " + new SphereClusters.RepulseSphere() { Centre = centre, FixedRadius = gravLimit, VariableRadius = linearSpeedFactor });
							m_clusters.Add(ref centre, gravLimit, linearSpeedFactor);
						}
						else
						{
							m_logger.debugLog("Nearby planet sphere: " + new SphereClusters.RepulseSphere() { Centre = centre, FixedRadius = Math.Sqrt(distSqCentreToDest), VariableRadius = distAutopilotToFinalDest * 0.1f });
							m_clusters.Add(ref centre, Math.Sqrt(distSqCentreToDest), Math.Min(linearSpeedFactor + distAutopilotToFinalDest * 0.1f, distAutopilotToFinalDest * 0.25f));
						}
					}
					continue;
				}

				//boundingRadius += entity.PositionComp.LocalVolume.Radius;

				float fixedRadius  = apRadius + entity.PositionComp.LocalVolume.Radius;

				if (distCentreToCurrent < fixedRadius + linearSpeedFactor)
				{
					// Entity is too close to autopilot for repulsion.
					if (calcRepulse)
					{
						float distBetween = (float)distCentreToCurrent - fixedRadius;
						if (distBetween < closestEntityDist)
							closestEntityDist = distBetween;
					}
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
					//m_logger.debugLog(entity.getBestName() + " sphere: " + new SphereClusters.RepulseSphere() { Centre = centre, FixedRadius = fixedRadius, VariableRadius = linearSpeedFactor });
					m_clusters.Add(ref centre, fixedRadius, linearSpeedFactor);
				}
			}

			// when following a path, only collect entites to avoid, do not repulse
			if (!calcRepulse)
			{
				Profiler.EndProfileBlock();
				repulsion = Vector3.Zero;
				return;
			}

			NavSet.Settings_Task_NavWay.SpeedMaxRelative = Math.Max(closestEntityDist * Mover.DistanceSpeedFactor, 5f);

			m_clusters.AddMiddleSpheres();

			//m_logger.debugLog("repulsion spheres: " + m_clusters.Clusters.Count);

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
		/// <param name="entity"></param>
		private void AvoidEntity(MyEntity entity)
		{
			if (entity is MyVoxelBase)
				m_checkVoxel = true;
			else
				m_entitiesPruneAvoid.Add(entity);
		}

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
			//m_logger.debugLog("repulsion of sphere: " + sphere + " is " + sphereRepulsion);// + ", entity: "+ entity.getBestName());
			repulsion.X += sphereRepulsion.X;
			repulsion.Y += sphereRepulsion.Y;
			repulsion.Z += sphereRepulsion.Z;
		}

		/// <summary>
		/// For testing if the current path is obstructed by any entity in m_entitiesPruneAvoid.
		/// </summary>
		private bool CurrentObstructed(out MyEntity obstructingEntity, out MyCubeBlock obstructBlock)
		{
			m_logger.debugLog("m_moveDirection: " + m_moveDirection, Logger.severity.FATAL, condition: Math.Abs(m_moveDirection.LengthSquared() - 1f) > 0.01f);
			//m_logger.debugLog("m_moveLength: " + m_moveLength, Logger.severity.FATAL, condition: Math.Abs(m_moveLength) < 0.1f);

			MyCubeBlock ignoreBlock = m_ignoreEntity as MyCubeBlock;

			// if destination is obstructing it needs to be checked first, so we would match speed with destination

			MyEntity destTop;
			if (m_destination.Entity != null)
			{
				//m_logger.debugLog("checking destination entity");

				destTop = (MyEntity)m_destination.Entity.GetTopMostParent();
				if (m_entitiesPruneAvoid.Contains(destTop))
				{
					if (m_tester.ObstructedBy(destTop, ignoreBlock, ref m_moveDirection, m_moveLength, out obstructBlock))
					{
						m_logger.debugLog("Obstructed by " + destTop.nameWithId() + "." + obstructBlock, Logger.severity.DEBUG);
						obstructingEntity = destTop;
						return true;
					}
				}
			}
			else
				destTop = null;

			// check voxel next so that the ship will not match an obstruction that is on a collision course

			if (m_checkVoxel)
			{
				//m_logger.debugLog("raycasting voxels");

				Vector3 autopilotVelocity = m_autopilotVelocity;
				Vector3 velocityMulti; Vector3.Multiply(ref autopilotVelocity, SpeedFactor, out velocityMulti);
				Vector3 directionMulti; Vector3.Multiply(ref m_moveDirection, VoxelAdd, out directionMulti);
				Vector3 rayDirection; Vector3.Add(ref velocityMulti, ref directionMulti, out rayDirection);
				MyVoxelBase hitVoxel;
				Vector3D hitPosition;
				if (m_tester.RayCastIntersectsVoxel(ref Vector3D.Zero, ref rayDirection, out hitVoxel, out hitPosition))
				{
					m_logger.debugLog("Obstructed by voxel " + hitVoxel + " at " + hitPosition+ ", autopilotVelocity: " + autopilotVelocity + ", m_moveDirection: " + m_moveDirection);
					obstructingEntity = hitVoxel;
					obstructBlock = null;
					return true;
				}
			}

			// check remaining entities

			//m_logger.debugLog("checking " + m_entitiesPruneAvoid.Count + " entites - destination entity - voxels");

			if (m_entitiesPruneAvoid.Count != 0)
				for (int i = m_entitiesPruneAvoid.Count - 1; i >= 0; i--)
				{
					//m_logger.debugLog("entity: " + m_entitiesPruneAvoid[i]);
					obstructingEntity = m_entitiesPruneAvoid[i];
					if (obstructingEntity == destTop || obstructingEntity is MyVoxelBase)
						// already checked
						continue;
					if (m_tester.ObstructedBy(obstructingEntity, ignoreBlock, ref m_moveDirection, m_moveLength, out obstructBlock))
					{
						m_logger.debugLog("Obstructed by " + obstructingEntity.nameWithId() + "." + obstructBlock, Logger.severity.DEBUG);
						return true;
					}
				}

			//m_logger.debugLog("No obstruction");
			obstructingEntity = null;
			obstructBlock = null;
			return false;
		}

		private bool ObstructionMovingAway()
		{
			Vector3D velocity = m_obstructingEntity.LinearVelocity;
			if (velocity.LengthSquared() < 0.01f)
				return false;
			Vector3D position = m_obstructingEntity.GetCentre();
			Vector3D nextPosition; Vector3D.Add(ref position, ref velocity, out nextPosition);
			double current; Vector3D.DistanceSquared(ref m_currentPosition, ref position, out current);
			double next; Vector3D.DistanceSquared(ref m_currentPosition, ref nextPosition, out next);
			m_logger.debugLog("Obstruction is moving away", condition: current < next);
			return current < next;
		}

		#region Jump

		private bool TryJump()
		{
			if (Globals.UpdateCount < m_nextJumpAttempt)
				return false;
			m_nextJumpAttempt = Globals.UpdateCount + 100uL;
			if (NavSet.Settings_Current.MinDistToJump < MyGridJumpDriveSystem.MIN_JUMP_DISTANCE || NavSet.Settings_Current.Distance < NavSet.Settings_Current.MinDistToJump)
			{
				//m_logger.debugLog("not allowed to jump, distance: " + NavSet.Settings_Current.Distance + ", min dist: " + NavSet.Settings_Current.MinDistToJump);
				JumpComplaint = InfoString.StringId_Jump.None;
				return false;
			}

			// search for a drive
			foreach (IMyCubeGrid grid in AttachedGrid.AttachedGrids(Mover.Block.CubeGrid, AttachedGrid.AttachmentKind.Terminal, true))
			{
				CubeGridCache cache = CubeGridCache.GetFor(grid);
				if (cache == null)
				{
					m_logger.debugLog("Missing a CubeGridCache");
					return false;
				}
				foreach (MyJumpDrive jumpDrive in cache.BlocksOfType(typeof(MyObjectBuilder_JumpDrive)))
					if (jumpDrive.CanJumpAndHasAccess(Mover.Block.CubeBlock.OwnerId))
					{
						m_nextJumpAttempt = Globals.UpdateCount + 1000uL;
						return RequestJump();
					}
			}

			//m_logger.debugLog("No usable jump drives", Logger.severity.TRACE);
			JumpComplaint = InfoString.StringId_Jump.NotCharged;
			return false;
		}

		/// <summary>
		/// Based on MyGridJumpDriveSystem.RequestJump
		/// </summary>
		private bool RequestJump()
		{
			MyCubeBlock apBlock = (MyCubeBlock)Mover.Block.CubeBlock;
			MyCubeGrid apGrid = apBlock.CubeGrid;

			if (!Vector3.IsZero(MyGravityProviderSystem.CalculateNaturalGravityInPoint(apGrid.WorldMatrix.Translation)))
			{
				//m_logger.debugLog("Cannot jump, in gravity");
				JumpComplaint = InfoString.StringId_Jump.InGravity;
				return false;
			}

			// check for static grids or grids already jumping
			foreach (MyCubeGrid grid in AttachedGrid.AttachedGrids(apGrid, AttachedGrid.AttachmentKind.Physics, true))
			{
				if (grid.MarkedForClose)
				{
					m_logger.debugLog("Cannot jump with closing grid: " + grid.nameWithId(), Logger.severity.WARNING);
					JumpComplaint = InfoString.StringId_Jump.ClosingGrid;
					return false;
				}
				if (grid.IsStatic)
				{
					m_logger.debugLog("Cannot jump with static grid: " + grid.nameWithId(), Logger.severity.WARNING);
					JumpComplaint = InfoString.StringId_Jump.StaticGrid;
					return false;
				}
				if (grid.GridSystems.JumpSystem.GetJumpDriveDirection().HasValue)
				{
					m_logger.debugLog("Cannot jump, grid already jumping: " + grid.nameWithId(), Logger.severity.WARNING);
					JumpComplaint = InfoString.StringId_Jump.AlreadyJumping;
					return false;
				}
			}

			Vector3D finalDest = m_destination.WorldPosition();

			if (MySession.Static.Settings.WorldSizeKm > 0 && finalDest.Length() > MySession.Static.Settings.WorldSizeKm * 500)
			{
				m_logger.debugLog("Cannot jump outside of world", Logger.severity.WARNING);
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
				m_logger.debugLog("Jump drives do not have sufficient power to jump the desired distance", Logger.severity.DEBUG);
				Vector3D newJumpDisp; Vector3D.Multiply(ref jumpDisp, maxJumpDistance / Math.Sqrt(jumpDistSq), out newJumpDisp);
				jumpDisp = newJumpDisp;

				if (newJumpDisp.LengthSquared() < MyGridJumpDriveSystem.MIN_JUMP_DISTANCE * MyGridJumpDriveSystem.MIN_JUMP_DISTANCE)
				{
					m_logger.debugLog("Jump drives do not have sufficient power to jump the minimum distance", Logger.severity.WARNING);
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
						m_logger.debugLog("planet gravity does not hit line: " + planet.getBestName());
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
				m_logger.debugLog("jump would hit: " + closest.Element.nameWithId() + ", shortening jump from " + m_lineSegment.Length + " to " + closest.Distance, Logger.severity.DEBUG);

				if (closest.Distance < MyGridJumpDriveSystem.MIN_JUMP_DISTANCE)
				{
					m_logger.debugLog("Jump is obstructed", Logger.severity.DEBUG);
					JumpComplaint = InfoString.StringId_Jump.Obstructed;
					return false;
				}

				m_lineSegment.To = m_lineSegment.From + m_lineSegment.Direction * closest.Distance;
			}

			m_logger.debugLog("Requesting jump to " + m_lineSegment.To, Logger.severity.DEBUG);
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
