#define LOG_ENABLED
#define PROFILE

using System;
using System.Collections;
using System.Collections.Generic;
using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Rynchodon.Settings;
using Rynchodon.Threading;
using Rynchodon.Utility;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinding
{
	public partial class Pathfinder
	{

		// TODO: gravity avoidance/repulsion, atmospheric avoidance/repulsion

		public const float SpeedFactor = 3f, VoxelAdd = 10f;
		private const float DefaultNodeDistance = 100f;

		public enum State : byte
		{
			Unobstructed, SearchingForPath, FollowingPath, FailedToFindPath, Crashed
		}

		private static ThreadManager ThreadForeground;
		private static ThreadManager ThreadBackground;

		static Pathfinder()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
			byte allowedThread = ServerSettings.GetSetting<byte>(ServerSettings.SettingName.yParallelPathfinder);
			ThreadForeground = new ThreadManager(allowedThread, false, "PathfinderForeground");
			ThreadBackground = new ThreadManager(allowedThread, true, "PathfinderBackground");
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			ThreadForeground = null;
			ThreadBackground = null;
		}

		private readonly Logger m_logger;
		private readonly PathTester m_tester = new PathTester();

		private readonly FastResourceLock m_runningLock = new FastResourceLock();
		private bool m_runInterrupt, m_runHalt;
		private ulong m_waitUntil;

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
		private LineSegmentD m_lineSegment = new LineSegmentD();

		// Results

		private Vector3 m_targetDirection = Vector3.Invalid;
		private float m_targetDistance;
		private Obstruction m_obstructingEntity;
		private MyCubeBlock m_obstructingBlock;

		public MyEntity ReportedObstruction { get { return m_obstructingBlock ?? m_obstructingEntity.Entity; } }
		public Mover Mover { get; private set; }
		public AllNavigationSettings NavSet { get { return Mover.NavSet; } }
		public RotateChecker RotateCheck { get { return Mover.RotateCheck; } }
		public State CurrentState { get; private set; }

		public Pathfinder(ShipControllerBlock block)
		{
			m_logger = new Logger(() => m_autopilotGrid.getBestName(), () => {
				if (m_navBlock != null)
					return m_navBlock.DisplayName;
				return "N/A";
			}, () => CurrentState.ToString());
			Mover = new Mover(block, new RotateChecker(block.CubeBlock, CollectEntities));
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
			Mover.CalcMove(NavSet.Settings_Current.NavigationBlock, ref Vector3.Zero, 0f, ref velocity);
		}

		public void MoveTo(PseudoBlock navBlock, LastSeen targetEntity, Vector3D offset = default(Vector3D), Vector3 addToVelocity = default(Vector3))
		{
			m_runHalt = false;

			Destination dest;
			if (targetEntity.isRecent())
			{
				dest = new Destination(targetEntity.Entity, offset);
			}
			else
			{
				dest = new Destination(targetEntity.LastKnownPosition);
				addToVelocity += targetEntity.LastKnownVelocity;
			}
			MoveTo(navBlock, ref dest, addToVelocity);
		}

		public void MoveTo(PseudoBlock navBlock, ref Destination destination, Vector3 addToVelocity = default(Vector3))
		{
			m_runHalt = false;

			AllNavigationSettings.SettingsLevel level = Mover.NavSet.Settings_Current;
			MyEntity ignoreEntity = (MyEntity)level.DestinationEntity;
			bool ignoreVoxel = level.IgnoreAsteroid;
			bool canChangeCourse = level.PathfinderCanChangeCourse;

			m_addToVelocity = addToVelocity;

			if (m_navBlock == navBlock && m_autopilotGrid == navBlock.Grid && m_destination.Equals(ref destination) && m_ignoreEntity == ignoreEntity && m_ignoreVoxel == ignoreVoxel && m_canChangeCourse == canChangeCourse)
			{
				ThreadForeground.EnqueueAction(Run);
				return;
			}

			m_logger.debugLog("nav block changed from " + (m_navBlock == null ? "N/A" : m_navBlock.DisplayName) + " to " + navBlock.DisplayName, condition: m_navBlock != navBlock);
			m_logger.debugLog("grid changed from " + m_autopilotGrid.getBestName() + " to " + navBlock.Grid.getBestName(), condition: m_autopilotGrid != navBlock.Grid);
			m_logger.debugLog("destination changed from " + m_destination + " to " + destination, condition: !m_destination.Equals(ref destination));
			m_logger.debugLog("ignore entity changed from " + m_ignoreEntity.getBestName() + " to " + ignoreEntity.getBestName(), condition: m_ignoreEntity != ignoreEntity);
			m_logger.debugLog("ignore voxel changed from " + m_ignoreVoxel + " to " + ignoreVoxel, condition: m_ignoreVoxel != ignoreVoxel);
			m_logger.debugLog("can change course changed from " + m_canChangeCourse + " to " + canChangeCourse, condition: m_canChangeCourse != canChangeCourse);

			m_runInterrupt = true;
			m_navBlock = navBlock;
			m_autopilotGrid = (MyCubeGrid)m_navBlock.Grid;
			m_destination = destination;
			m_ignoreEntity = ignoreEntity;
			m_ignoreVoxel = ignoreVoxel;
			m_canChangeCourse = canChangeCourse;
			m_pathfinding = false;

			ThreadForeground.EnqueueAction(Run);
		}

		private void Run()
		{
			if (m_waitUntil > Globals.UpdateCount)
			{
				m_targetDirection = Vector3.Invalid;
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
					m_path.Clear();
				else if (m_pathfinding)
					return;

				m_runInterrupt = false;

				FillDestWorld();
				TestCurrentPath();
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
				ThreadForeground.EnqueueAction(Run);
			else if (m_pathfinding)
				ThreadBackground.EnqueueAction(ContinuePathfinding);
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

		/// <summary>
		/// Instructs Mover to calculate movement.
		/// </summary>
		private void OnComplete()
		{
			m_entitiesPruneAvoid.Clear();
			m_entitiesRepulse.Clear();

			if (m_runInterrupt || m_runHalt)
				return;

			MyEntity relativeEntity = m_obstructingEntity.Entity != null && m_obstructingEntity.MatchPosition ? m_obstructingEntity.Entity : (MyEntity)m_destination.Entity;
			Vector3 targetVelocity = relativeEntity != null && relativeEntity.Physics != null && !relativeEntity.Physics.IsStatic ? relativeEntity.Physics.LinearVelocity : Vector3.Zero;
			if (!m_path.HasTarget)
			{
				Vector3 temp; Vector3.Add(ref targetVelocity, ref m_addToVelocity, out temp);
				targetVelocity = temp;
			}

			if (!m_targetDirection.IsValid())
			{
				// while pathfinding, move to start node
				if (m_pathfinding)
				{
					Vector3D obstructPosition = m_obstructingEntity.GetPosition();
					Vector3D lastReached; m_path.GetReached(out lastReached);
					Vector3D firstNodeWorld; Vector3D.Add(ref lastReached, ref obstructPosition, out firstNodeWorld);
					Vector3D toFirst; Vector3D.Subtract(ref firstNodeWorld, ref m_currentPosition, out toFirst);
					double distance = toFirst.Normalize();
					//m_logger.debugLog("moving to start node, disp: " + (toFirst * (float)distance) + ", dist: " + distance + ", obstruct: " + obstructPosition + ", first node: " + m_lastReachedPathPosition + ", first node world: " + firstNodeWorld + ", current: " + m_currentPosition);
					if (distance < 1d)
					{
						//m_logger.debugLog("maintaining position");
						ShipAutopilot.AutopilotThread.EnqueueAction(() => Mover.CalcMove(m_navBlock, ref Vector3.Zero, 0f, ref targetVelocity));
						return;
					}
					Vector3 direction = toFirst;
					ShipAutopilot.AutopilotThread.EnqueueAction(() => Mover.CalcMove(m_navBlock, ref direction, (float)distance, ref targetVelocity));
				}
				else
				{
					// maintain postion to obstruction / destination
					//m_logger.debugLog("maintaining position");
					ShipAutopilot.AutopilotThread.EnqueueAction(() => Mover.CalcMove(m_navBlock, ref Vector3.Zero, 0f, ref targetVelocity));
				}
			}
			else
			{
				//m_logger.debugLog("move to position: " + (m_navBlock.WorldPosition + m_targetDirection * m_targetDistance));
				ShipAutopilot.AutopilotThread.EnqueueAction(() => Mover.CalcMove(m_navBlock, ref m_targetDirection, m_targetDistance, ref targetVelocity));
			}
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
					m_targetDirection = Vector3.Invalid;
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
			m_targetDirection = dispF;
			m_targetDistance = m_targetDirection.Normalize();

			CalcRepulsion(!m_path.HasTarget && m_canChangeCourse, out repulsion);

			if (repulsion != Vector3.Zero)
			{
				float repLenSq = repulsion.LengthSquared();

				if (m_targetDistance * m_targetDistance < repLenSq)
				{
					// allow repulsion to dominate
					Vector3.Add(ref dispF, ref repulsion, out m_targetDirection);
					m_targetDistance = m_targetDirection.Normalize();
				}
				else
				{
					// do not allow displacement to dominate
					m_targetDistance += repulsion.Normalize();
					Vector3 newDirection; Vector3.Add(ref m_targetDirection, ref repulsion, out newDirection);
					m_targetDirection = newDirection;
					m_targetDirection.Normalize();
				}
			}

			MyEntity obstructing;
			MyCubeBlock block;
			Vector3D pointOfObstruction;
			if (CurrentObstructed(out obstructing, out pointOfObstruction, out block))
			{
				if (m_path.HasTarget)
					if (TryRepairPath(ref disp))
						return;

				m_obstructingEntity = new Obstruction() { Entity = obstructing, MatchPosition = m_canChangeCourse };
				m_obstructingBlock = block;

				m_forward.Clear();
				m_backward.Clear();

				m_targetDirection = Vector3.Invalid;
				if (ObstructionMovingAway())
					return;
				FindAPath();
				return;
			}

			//m_logger.debugLog("Target direction: " + m_targetDirection + ", target distance: " + m_targetDistance);
			if (!m_path.HasTarget)
			{
				CurrentState = State.Unobstructed;
				m_obstructingEntity = new Obstruction();
				m_obstructingBlock = null;
			}
		}

		private bool TryRepairPath(ref Vector3D disp)
		{
			// if autopilot is off course, try to move back towards the line from last reached to current node

			m_logger.debugLog("entered");

			Vector3D obstructPosition = m_obstructingEntity.GetPosition();
			Vector3D lastReached; m_path.GetReached(out lastReached);
			Vector3D lastReachedWorldPos; Vector3D.Add(ref lastReached, ref obstructPosition, out lastReachedWorldPos);

			m_lineSegment.From = lastReachedWorldPos;
			m_lineSegment.To = m_destWorld;

			Vector3D moveToPoint; m_lineSegment.ClosestPoint(ref m_currentPosition, out moveToPoint);
			double distSquared; Vector3D.DistanceSquared(ref m_currentPosition, ref moveToPoint, out distSquared);

			if (distSquared > 1d)
			{
				m_logger.debugLog("Moving to point(" + moveToPoint + ") on line between last reached(" + m_lineSegment.From + ") and current node(" + m_lineSegment.To + ") that is closest to current position(" + m_currentPosition + ").");

				Line moveLine = new Line(m_currentPosition, moveToPoint);
				if (!CanTravelSegment(ref Vector3D.Zero, ref moveLine))
				{
					m_logger.debugLog("Cannot reach point on line");
					return SetNextPathTarget();
				}
				else
				{
					double targetDistance = Math.Sqrt(distSquared);
					m_targetDistance = (float)targetDistance;

					Vector3D.Subtract(ref moveToPoint, ref m_currentPosition, out disp);
					Vector3D direct; Vector3D.Divide(ref disp, targetDistance, out direct);
					m_targetDirection = direct;
					m_logger.debugLog("Target direction: " + m_targetDirection + ", target distance: " + m_targetDistance);
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
					if (Attached.AttachedGrid.IsGridAttached(m_autopilotGrid, grid, Attached.AttachedGrid.AttachmentKind.Physics))
						continue;
				}
				else if (entity is MyVoxelBase)
				{
					if (!m_ignoreVoxel)
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

			for (int index = m_entitiesRepulse.Count - 1; index >= 0; index--)
			{
				MyEntity entity = m_entitiesRepulse[index];

				m_logger.debugLog("entity is null", Logger.severity.FATAL, condition: entity == null);
				m_logger.debugLog("entity is not top-most", Logger.severity.FATAL, condition: entity.Hierarchy.Parent != null);

				Vector3D centre = entity.GetCentre();
				Vector3D toCentreD;
				Vector3D.Subtract(ref centre, ref m_currentPosition, out toCentreD);
				double distCentreToCurrent = toCentreD.Normalize();
				Vector3 toCentre = toCentreD;
				float boundingRadius;
				float linearSpeed;
				double distSqCentreToDest; Vector3D.DistanceSquared(ref centre, ref m_destWorld, out distSqCentreToDest);

				// determine speed of convergence
				if (entity.Physics == null)
					Vector3.Dot(ref autopilotVelocity, ref toCentre, out linearSpeed);
				else
				{
					Vector3 obVel = entity.Physics.LinearVelocity;
					if (entity is MyAmmoBase)
						obVel.X *= 10f; obVel.Y *= 10f; obVel.Z *= 10f;

					Vector3 relVel;
					Vector3.Subtract(ref autopilotVelocity, ref  obVel, out relVel);
					Vector3.Dot(ref relVel, ref toCentre, out linearSpeed);
				}
				if (linearSpeed <= 0f)
					linearSpeed = 0f;
				else
					linearSpeed *= SpeedFactor;
				boundingRadius = m_autopilotShipBoundingRadius + linearSpeed;

				MyPlanet planet = entity as MyPlanet;
				if (planet != null)
				{
					if (distCentreToCurrent + boundingRadius < planet.MaximumRadius)
						m_checkVoxel = true;
					if (calcRepulse)
					{
						// avoid planet gravity to minimum of current altitude, destination altitude, and gravity limit * 2
						float gravLimit = ((MySphericalNaturalGravityComponent)planet.Components.Get<MyGravityProviderComponent>()).GravityLimit * 2f;
						boundingRadius += distCentreToCurrent * distCentreToCurrent < distSqCentreToDest ?
							Math.Min((float)distCentreToCurrent, gravLimit) :
							gravLimit * gravLimit < distSqCentreToDest ?
							gravLimit :
							(float)Math.Sqrt(distSqCentreToDest);

						m_logger.debugLog("gravity limit: " + gravLimit + ", dist to current: " + distCentreToCurrent + ", dist to dest: " + Math.Sqrt(distSqCentreToDest) + ", bounding radius: " + boundingRadius);

						BoundingSphereD entitySphere = new BoundingSphereD(centre, boundingRadius);
						m_clusters.Add(ref entitySphere);
					}
					continue;
				}

				boundingRadius += entity.PositionComp.LocalVolume.Radius;

				if (distCentreToCurrent < boundingRadius)
				{
					// Entity is too close to autopilot for repulsion.
					AvoidEntity(entity);
					continue;
				}

				boundingRadius = Math.Min(boundingRadius * 4f, boundingRadius + 1000f);

				if (distSqCentreToDest < boundingRadius * boundingRadius)
				{
					if (distAutopilotToFinalDest < distCentreToCurrent)
					{
						// Entity is near destination and autopilot is nearing destination
						AvoidEntity(entity);
						continue;
					}

					double minGain = entity.PositionComp.LocalVolume.Radius;
					if (minGain <= 100d)
						minGain = 10d;
					else
						minGain *= 0.1d;

					if (distSqCentreToDest < minGain * minGain)
					{
						// Entity is too close to destination for cicling it to be much use
						AvoidEntity(entity);
						continue;
					}
				}

				if (calcRepulse)
				{
					BoundingSphereD entitySphere = new BoundingSphereD(centre, boundingRadius);
					m_clusters.Add(ref entitySphere);
				}
			}

			// when following a path, only collect entites to avoid, do not repulse
			if (!calcRepulse)
			{
				Profiler.EndProfileBlock();
				repulsion = Vector3.Zero;
				return;
			}

			m_clusters.AddMiddleSpheres();

			//m_logger.debugLog("repulsion spheres: " + m_clusters.Clusters.Count);

			repulsion = Vector3.Zero;
			for (int indexO = m_clusters.Clusters.Count - 1; indexO >= 0; indexO--)
			{
				List<BoundingSphereD> cluster = m_clusters.Clusters[indexO];
				for (int indexI = cluster.Count - 1; indexI >= 0; indexI--)
				{
					BoundingSphereD sphere = cluster[indexI];
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
		///// <param name="repulsion">A directional vector with length between 0 and 1, indicating the repulsive force.</param>
		private void CalcRepulsion(ref BoundingSphereD sphere, ref Vector3 repulsion)
		{
			Vector3D toCurrentD;
			Vector3D.Subtract(ref m_currentPosition, ref sphere.Center, out toCurrentD);
			Vector3 toCurrent = toCurrentD;
			float toCurrentLenSq = toCurrent.LengthSquared();

			float maxRepulseDist = (float)sphere.Radius;
			float maxRepulseDistSq = maxRepulseDist * maxRepulseDist;

			//m_logger.debugLog("current position: " + m_currentPosition + ", sphere centre: " + sphere.Center + ", to current: " + toCurrent.Length() + ", sphere radius: " + sphere.Radius + ", to dest: " + Vector3D.Distance(sphere.Center, m_destWorld));

			if (toCurrentLenSq > maxRepulseDistSq)
			{
				// Autopilot is outside the maximum bounds of repulsion.
				return;
			}

			float toCurrentLen = (float)Math.Sqrt(toCurrentLenSq);
			float repulseMagnitude = maxRepulseDist - toCurrentLen;

			Vector3 sphereRepulsion; Vector3.Multiply(ref toCurrent, repulseMagnitude / toCurrentLen, out sphereRepulsion);
			repulsion.X += sphereRepulsion.X;
			repulsion.Y += sphereRepulsion.Y;
			repulsion.Z += sphereRepulsion.Z;
		}

		/// <summary>
		/// For testing if the current path is obstructed by any entity in m_entitiesPruneAvoid.
		/// </summary>
		private bool CurrentObstructed(out MyEntity obstructingEntity, out Vector3D pointOfObstruction, out MyCubeBlock obstructBlock)
		{
			m_logger.debugLog("m_targetDirection is invalid", Logger.severity.FATAL, condition: !m_targetDirection.IsValid());
			m_logger.debugLog("m_targetDistance == 0f", Logger.severity.FATAL, condition: m_targetDistance == 0f);

			MyCubeBlock ignoreBlock = m_ignoreEntity as MyCubeBlock;

			// if destination is obstructing it needs to be checked first, so we would match speed with destination

			MyEntity destTop;
			if (m_destination.Entity != null)
			{
				//m_logger.debugLog("checking destination entity");

				destTop = (MyEntity)m_destination.Entity.GetTopMostParent();
				if (m_entitiesPruneAvoid.Contains(destTop))
				{
					if (m_tester.ObstructedBy(destTop, ignoreBlock, ref m_targetDirection, m_targetDistance, out obstructBlock, out pointOfObstruction))
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
				Vector3 directionMulti; Vector3.Multiply(ref m_targetDirection, VoxelAdd, out directionMulti);
				Vector3 rayDirection; Vector3.Add(ref velocityMulti, ref directionMulti, out rayDirection);
				MyVoxelBase hitVoxel;
				Vector3D hitPosition;
				if (m_tester.RayCastIntersectsVoxel(ref Vector3D.Zero, ref rayDirection, out hitVoxel, out hitPosition))
				{
					m_logger.debugLog("Obstructed by voxel " + hitVoxel + " at " + hitPosition);
					obstructingEntity = hitVoxel;
					pointOfObstruction = hitPosition;
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
					if (m_tester.ObstructedBy(obstructingEntity, ignoreBlock, ref m_targetDirection, m_targetDistance, out obstructBlock, out pointOfObstruction))
					{
						m_logger.debugLog("Obstructed by " + obstructingEntity.nameWithId() + "." + obstructBlock, Logger.severity.DEBUG);
						return true;
					}
				}

			//m_logger.debugLog("No obstruction");
			obstructingEntity = null;
			pointOfObstruction = Vector3.Invalid;
			obstructBlock = null;
			return false;
		}

		private bool ObstructionMovingAway()
		{
			Vector3D velocity = m_obstructingEntity.LinearVelocity;
			if (velocity.LengthSquared() < 10f)
				return false;
			Vector3D position = m_obstructingEntity.GetCentre();
			Vector3D nextPosition; Vector3D.Add(ref position, ref velocity, out nextPosition);
			double current; Vector3D.DistanceSquared(ref m_currentPosition, ref position, out current);
			double next; Vector3D.DistanceSquared(ref m_currentPosition, ref nextPosition, out next);
			m_logger.debugLog("Obstruction is moving away", condition: current < next);
			return current < next;
		}

		/// <summary>
		/// Starts the pathfinding.
		/// </summary>
		private void FindAPath()
		{
			m_logger.debugLog("entered");

			m_path.Clear();
			//PurgeTempGPS();
			m_pathfinding = true;
			CurrentState = State.SearchingForPath;

			FillDestWorld();

			m_logger.debugLog("m_obstructingEntity == null", Logger.severity.ERROR, condition: m_obstructingEntity.Entity == null);

			PathNode startNode;
			Vector3D relativePosition;
			Vector3D obstructPosition = m_obstructingEntity.GetPosition();

			Vector3D.Subtract(ref m_destWorld, ref obstructPosition, out relativePosition);
			Vector3D backStartPos = new Vector3D(Math.Round(relativePosition.X), Math.Round(relativePosition.Y), Math.Round(relativePosition.Z));

			if (!m_canChangeCourse)
			{
				m_logger.debugLog("No course change permitted, setting up line");

				m_forward.Clear();
				m_backward.Clear();

				Vector3D currentToDest; Vector3D.Subtract(ref m_destWorld, ref m_currentPosition, out currentToDest);
				currentToDest.Normalize();
				Vector3D.Subtract(ref m_currentPosition, ref obstructPosition, out m_forward.m_startPosition);
				PathNode foreFirstNode = new PathNode() { Position = m_forward.m_startPosition, DirectionFromParent = currentToDest };
				m_forward.m_reachedNodes.Add(foreFirstNode.Key, foreFirstNode);
				m_forward.m_openNodes.Insert(foreFirstNode, 0f);
				m_path.AddBack(ref m_forward.m_startPosition);

				PathNode backFirstNode = new PathNode() { Position = backStartPos };
				m_backward.m_startPosition = backStartPos;
				m_backward.m_reachedNodes.Add(backFirstNode.Key, backFirstNode);
				return;
			}

			double diffSquared; Vector3D.DistanceSquared(ref backStartPos, ref m_backward.m_startPosition, out diffSquared);
			float destRadius = NavSet.Settings_Current.DestinationRadius;
			destRadius *= destRadius;

			if (diffSquared > destRadius || m_backward.m_reachedNodes.Count == 0)
			{
				m_logger.debugLog("Rebuilding backward.. Previous start : " + m_backward.m_startPosition + ", new start: " + backStartPos +
					", dest world: " + m_destWorld + ", obstruct: " + obstructPosition + ", relative: " + relativePosition);
				m_backward.Clear();
				m_backward.m_startPosition = backStartPos;
				startNode = new PathNode() { Position = backStartPos };
				m_backward.m_openNodes.Insert(startNode, 0f);
				m_backward.m_reachedNodes.Add(startNode.Position.GetHash(), startNode);
			}
			else
			{
				m_logger.debugLog("Reusing backward: " + m_backward.m_reachedNodes.Count + ", " + m_backward.m_openNodes.Count);
				//Logger.DebugNotify("Reusing backward nodes");
			}

			Vector3D currentRelative; Vector3D.Subtract(ref m_currentPosition, ref obstructPosition, out currentRelative);
			// forward start node's children needs to be a discrete number of steps from backward startNode
			Vector3D finishToStart; Vector3D.Subtract(ref currentRelative, ref backStartPos, out finishToStart);

			Vector3D.DistanceSquared(ref currentRelative, ref m_forward.m_startPosition, out diffSquared);
			if (diffSquared > destRadius || m_forward.m_reachedNodes.Count == 0)
			{
				m_logger.debugLog("Rebuilding forward. Previous start: " + m_forward.m_startPosition + ", new start: " + currentRelative);
				m_forward.Clear();
				m_forward.m_startPosition = currentRelative;
				m_path.AddBack(ref currentRelative);

				PathNode parentNode = new PathNode() { Position = currentRelative };
				long parentKey = parentNode.Position.GetHash();
				m_forward.m_reachedNodes.Add(parentKey, parentNode);

				double parentPathDist = MinPathDistance(ref finishToStart);

				Vector3D steps; Vector3D.Divide(ref finishToStart, m_nodeDistance, out steps);
				Vector3D stepsFloor = new Vector3D() { X = Math.Floor(steps.X), Y = Math.Floor(steps.Y), Z = Math.Floor(steps.Z) };
				Vector3D currentStep;
				for (currentStep.X = stepsFloor.X; currentStep.X <= stepsFloor.X + 1; currentStep.X++)
					for (currentStep.Y = stepsFloor.Y; currentStep.Y <= stepsFloor.Y + 1; currentStep.Y++)
						for (currentStep.Z = stepsFloor.Z; currentStep.Z <= stepsFloor.Z + 1; currentStep.Z++)
						{
							Vector3D.Multiply(ref currentStep, m_nodeDistance, out finishToStart);
							Vector3D.Add(ref backStartPos, ref finishToStart, out relativePosition);

							Vector3D dispToDest; Vector3D.Subtract(ref backStartPos, ref relativePosition, out dispToDest);
							double childPathDist = MinPathDistance(ref dispToDest);
				
							//if (childPathDist > parentPathDist)
							//{
							//	m_logger.debugLog("Skipping child: " + ReportRelativePosition(relativePosition) + ", childPathDist: " + childPathDist + ", parentPathDist: " + parentPathDist);
							//	continue;
							//}

							PathNode childNode = new PathNode(ref parentNode, ref relativePosition);

							float resultKey = childNode.DistToCur + (float)childPathDist;

							//m_logger.debugLog("Initial child: " + ReportRelativePosition(childNode.Position));
							m_forward.m_openNodes.Insert(childNode, resultKey);
						}
			}
			else
			{
				m_logger.debugLog("Reusing forward: " + m_forward.m_reachedNodes.Count + ", " + m_forward.m_openNodes.Count);
				//Logger.DebugNotify("Reusing forward nodes");
				m_path.AddBack(ref currentRelative);
			}
		}

		private void ContinuePathfinding()
		{
			if (m_waitUntil > Globals.UpdateCount)
			{
				m_targetDirection = Vector3.Invalid;
				OnComplete();
				return;
			}

			if (!m_runningLock.TryAcquireExclusive())
				return;
			try
			{
				if (m_runHalt || m_runInterrupt)
					return;
				FillDestWorld();

				if (!m_canChangeCourse)
					FindAPath(true);
				else if (m_forward.m_blueSkyNodes.Count != m_backward.m_blueSkyNodes.Count && NavSet.Settings_Current.Distance > 1000f)
				{
					if (m_forward.m_blueSkyNodes.Count < m_backward.m_blueSkyNodes.Count)
						FindAPath(true);
					else
						FindAPath(false);
				}
				else if (m_forward.m_openNodes.Count <= m_backward.m_openNodes.Count)
					FindAPath(true);
				else
					FindAPath(false);
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
		/// Continues pathfinding.
		/// </summary>
		private void FindAPath(bool isForwardSet)
		{
			PathNodeSet pnSet = isForwardSet ? m_forward : m_backward;

			m_logger.debugLog("m_obstructingEntity == null", Logger.severity.ERROR, condition: m_obstructingEntity.Entity == null, secondaryState: SetName(pnSet));

			if (pnSet.m_openNodes.Count == 0)
			{
				OutOfNodes();
				return;
			}

			PathNode currentNode = pnSet.m_openNodes.RemoveMin();
			if (currentNode.DistToCur == 0f)
			{
				m_logger.debugLog("first node: " + ReportRelativePosition(currentNode.Position), secondaryState: SetName(pnSet));
				CreatePathNodes(ref currentNode, isForwardSet);
				return;
			}

			if (pnSet.m_reachedNodes.ContainsKey(currentNode.Position.GetHash()))
			{
				//m_logger.debugLog("Already reached: " + ReportRelativePosition(currentNode.Position), secondaryState: SetName(direction));
				return;
			}

			FillEntitiesLists();
			Vector3 repulsion;
			CalcRepulsion(false, out repulsion);
			m_logger.debugLog("Calculated repulsion for some reason: " + repulsion, Logger.severity.WARNING, condition: repulsion != Vector3.Zero, secondaryState: SetName(pnSet));

			Vector3D obstructPosition = m_obstructingEntity.GetPosition();
			PathNode parent = pnSet.m_reachedNodes[currentNode.ParentKey];
			Vector3D worldParent;
			Vector3D.Add(ref obstructPosition, ref parent.Position, out worldParent);
			Vector3D offset; Vector3D.Subtract(ref worldParent, ref m_currentPosition, out offset);
			Line line = new Line() { From = parent.Position, To = currentNode.Position, Direction = currentNode.DirectionFromParent, Length = currentNode.DistToCur - parent.DistToCur };

			if (!CanTravelSegment(ref offset, ref line))
			{
#if PROFILE
				pnSet.m_unreachableNodes++;
#endif
				//m_logger.debugLog("Not reachable: " + ReportRelativePosition(currentNode.Position), secondaryState: SetName(pnSet));
				//ShowPosition(currentNode);
				return;
			}
			m_logger.debugLog("Reached node: " + ReportRelativePosition(currentNode.Position) + " from " + ReportRelativePosition(pnSet.m_reachedNodes[currentNode.ParentKey].Position) + ", reached: " + pnSet.m_reachedNodes.Count + ", open: " + pnSet.m_openNodes.Count, secondaryState: SetName(pnSet));
			long cNodePosHash = currentNode.Position.GetHash();
			pnSet.m_reachedNodes.Add(cNodePosHash, currentNode);
			//ShowPosition(currentNode, SetName(pnSet));

			if (!m_canChangeCourse)
			{
				m_logger.debugLog("Running backwards search", Logger.severity.ERROR, condition: !isForwardSet, secondaryState: SetName(pnSet));
				Vector3D.Subtract(ref m_currentPosition, ref m_backward.m_startPosition, out offset);
				line = new Line() { From = currentNode.Position, To = m_backward.m_startPosition, Direction = currentNode.DirectionFromParent, Length = currentNode.DistToCur - parent.DistToCur };
				if (CanTravelSegment(ref offset, ref line))
				{
					m_logger.debugLog("Reached destination from node: " + ReportRelativePosition(currentNode.Position), secondaryState: SetName(pnSet));
					BuildPath(currentNode.Key, m_backward.m_startPosition.GetHash());
				}
				return;
			}

			PathNodeSet otherDirection = isForwardSet ? m_backward : m_forward;
			if (otherDirection.m_reachedNodes.ContainsKey(cNodePosHash))
			{
				m_logger.debugLog("Other direction has same position", secondaryState: SetName(pnSet));
				BuildPath(cNodePosHash, cNodePosHash);
				return;
			}

			// blue sky test

			Vector3D currentNodeWorld; Vector3D.Add(ref obstructPosition, ref currentNode.Position, out currentNodeWorld);
			BoundingSphereD sphere = new BoundingSphereD() { Center = currentNodeWorld, Radius = m_autopilotShipBoundingRadius + 100f };
			m_entitiesRepulse.Clear(); // use repulse list as prune/avoid is needed for CanTravelSegment
			MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, m_entitiesRepulse);
			if (m_entitiesRepulse.Count == 0)
			{
				//Logger.DebugNotify(SetName(pnSet) + " Blue Sky");
				m_logger.debugLog("Blue sky node: " + ReportRelativePosition(currentNode.Position), secondaryState: SetName(pnSet));

				Vector3D.Subtract(ref currentNodeWorld, ref m_currentPosition, out offset);

				line = new Line(currentNode.Position, otherDirection.m_startPosition, false);
				if (CanTravelSegment(ref offset, ref line))
				{
					m_logger.debugLog("Blue sky to opposite start", secondaryState: SetName(pnSet));
					//Logger.DebugNotify("Blue Sky to Opposite", level: Logger.severity.INFO);
					if (isForwardSet)
						BuildPath(currentNode.Key, otherDirection.m_startPosition.GetHash());
					else
						BuildPath(otherDirection.m_startPosition.GetHash(), currentNode.Key);
					return;
				}

				foreach (Vector3D otherBlueSky in otherDirection.m_blueSkyNodes)
				{
					line = new Line(currentNode.Position, otherBlueSky, false);
					if (CanTravelSegment(ref offset, ref line))
					{
						m_logger.debugLog("Blue sky path", secondaryState: SetName(pnSet));
						//Logger.DebugNotify("Blue Sky Path", level: Logger.severity.INFO);
						BuildPath(pnSet.m_blueSkyNodes == m_forward.m_blueSkyNodes ? currentNode.Position.GetHash() : otherBlueSky.GetHash());
						return;
					}
				}

				pnSet.m_blueSkyNodes.Add(currentNode.Position);
			}

			//DebugRectClose(ref otherDirection, currentNode.Position);
			CreatePathNodes(ref currentNode, isForwardSet);
		}

		/// <param name="offset">Difference between start of segment and current position.</param>
		/// <param name="line">Relative from and relative to</param>
		private bool CanTravelSegment(ref Vector3D offset, ref Line line)
		{
			Profiler.StartProfileBlock();

			MyCubeBlock ignoreBlock = m_ignoreEntity as MyCubeBlock;

			if (m_checkVoxel)
			{
				//m_logger.debugLog("raycasting voxels");

				Vector3 adjustment; Vector3.Multiply(ref line.Direction, VoxelAdd, out adjustment);
				Vector3 disp; Vector3.Subtract(ref line.To, ref line.From, out disp);
				Vector3 rayTest; Vector3.Add(ref disp, ref adjustment, out rayTest);
				MyVoxelBase hitVoxel;
				Vector3D hitPosition;
				if (m_tester.RayCastIntersectsVoxel(ref offset, ref rayTest, out hitVoxel, out hitPosition))
				{
					m_logger.debugLog("Obstructed by voxel " + hitVoxel + " at " + hitPosition, Logger.severity.DEBUG);
					Profiler.EndProfileBlock();
					return false;
				}
			}

			//m_logger.debugLog("checking " + m_entitiesPruneAvoid.Count + " entites - voxels");

			if (m_entitiesPruneAvoid.Count != 0)
				for (int i = m_entitiesPruneAvoid.Count - 1; i >= 0; i--)
				{
					MyEntity entity = m_entitiesPruneAvoid[i];
					if (entity is MyVoxelBase)
						// already checked
						continue;
					MyCubeBlock obstructBlock;
					Vector3D pointOfObstruction;
					if (m_tester.ObstructedBy(entity, ignoreBlock, ref offset, ref line.Direction, line.Length, out obstructBlock, out pointOfObstruction))
					{
						m_logger.debugLog("Obstructed by " + entity.nameWithId() + "." + obstructBlock, Logger.severity.DEBUG);
						Profiler.EndProfileBlock();
						return false;
					}
				}

			//m_logger.debugLog("No obstruction. Start: " + (m_currentPosition + offset) + ", finish: " + (m_currentPosition + offset + (line.To - line.From)));
			Profiler.EndProfileBlock();
			return true;
		}

		private void CreatePathNodes(ref PathNode currentNode, bool isForwardSet)
		{
			if (!m_canChangeCourse)
			{
				m_logger.debugLog("Not forward set", Logger.severity.ERROR, condition: !isForwardSet, secondaryState: SetName(isForwardSet ? m_forward : m_backward));
				CreatePathNodeLine(ref currentNode);
				return;
			}

			long currentKey = currentNode.Position.GetHash();
			foreach (Vector3I neighbour in Globals.NeighboursOne)
				CreatePathNode(currentKey, ref currentNode, neighbour, 1f,  isForwardSet);
			foreach (Vector3I neighbour in Globals.NeighboursTwo)
				CreatePathNode(currentKey, ref currentNode, neighbour, MathHelper.Sqrt2,  isForwardSet);
			foreach (Vector3I neighbour in Globals.NeighboursThree)
				CreatePathNode(currentKey, ref currentNode, neighbour, MathHelper.Sqrt3, isForwardSet);
		}

		private void CreatePathNode(long parentKey, ref PathNode parent, Vector3I neighbour, float distMulti, bool isForwardSet)
		{
			Profiler.StartProfileBlock();
			PathNodeSet pnSet = isForwardSet ? m_forward : m_backward;

			Vector3D position = parent.Position + neighbour * m_nodeDistance;
			// round position so that is a discrete number of steps from destination
			Vector3D finishToPosition; Vector3D.Subtract(ref position, ref m_backward.m_startPosition, out finishToPosition);
			VectorExtensions.RoundTo(ref finishToPosition, m_nodeDistance);
			Vector3D.Add(ref m_backward.m_startPosition, ref finishToPosition, out position);

			//position = new Vector3D(Math.Round(position.X), Math.Round(position.Y), Math.Round(position.Z));
			long positionHash = position.GetHash();

			if (pnSet.m_reachedNodes.ContainsKey(positionHash))
			{
				Profiler.EndProfileBlock();
				return;
			}

			//Vector3D disp; Vector3D.Subtract(ref position, ref parent.Position, out disp);
			//Vector3 neighbourF = neighbour;
			//Vector3 direction; Vector3.Divide(ref neighbourF, distMulti, out direction);

			PathNode result = new PathNode(ref parent, ref position);

			float turn; Vector3.Dot(ref parent.DirectionFromParent, ref result.DirectionFromParent, out turn);

			if (turn < 0f)
			{
				Profiler.EndProfileBlock();
				return;
			}

			Vector3D obstructionPosition = m_obstructingEntity.GetPosition();
			Vector3D currentWorldPosition; Vector3D.Add(ref position, ref obstructionPosition, out currentWorldPosition);
			Vector3D dest = isForwardSet ? m_destWorld : m_currentPosition;
			Vector3D dispToDest; Vector3D.Subtract(ref dest, ref currentWorldPosition, out dispToDest);
			//double distToDest = Math.Abs(dispToDest.X) + Math.Abs(dispToDest.Y) + Math.Abs(dispToDest.Z);
			//float resultKey = distToCur + (float)distToDest;

			float resultKey = result.DistToCur + (float)MinPathDistance(ref dispToDest);

			if (turn > 0.99f && parent.ParentKey != 0f)
			{
				//m_logger.debugLog("Skipping parent node. Turn: " + turn);
				parentKey = parent.ParentKey;
			}
			else
			{
				if (turn < 0f)
				{
					Profiler.EndProfileBlock();
					return;
				}
				//m_logger.debugLog("Pathfinder node backtracks to parent. Position: " + position + ", parent position: " + parent.Position +
				//	"\ndirection: " + direction + ", parent direction: " + parent.DirectionFromParent, Logger.severity.FATAL, condition: turn < -0.99f, secondaryState: SetName(pnSet));
				resultKey += 300f * (1f - turn);
			}

			//PathNode result = new PathNode()
			//{
			//	ParentKey = parentKey,
			//	DistToCur = distToCur,
			//	Position = position,
			//	DirectionFromParent = direction,
			//};

			m_logger.debugLog("DirectionFromParent is incorrect. DirectionFromParent: " + result.DirectionFromParent + ", parent: " + parent.Position + ", current: " + result.Position + ", direction: " +
				Vector3.Normalize(result.Position - parent.Position), Logger.severity.ERROR, condition: !Vector3.Normalize(result.Position - parent.Position).Equals(result.DirectionFromParent, 0.01f));
			m_logger.debugLog("Length is incorrect. Length: " + (result.DistToCur - parent.DistToCur) + ", distance: " + Vector3D.Distance(result.Position, parent.Position), Logger.severity.ERROR,
				condition: Math.Abs((result.DistToCur - parent.DistToCur) - (Vector3D.Distance(result.Position, parent.Position))) > 0.01f);

			m_logger.debugLog("resultKey <= 0", Logger.severity.ERROR, condition: resultKey <= 0f, secondaryState: SetName(pnSet));
			//m_logger.debugLog("path node: " + resultKey + ", " + result.ParentKey + ", " + result.DistToCur + ", " + result.Position + " => " + (m_obstructingEntity.GetPosition() + result.Position) + ", " + result.DirectionFromParent, secondaryState: SetName(pnSet));
			//m_logger.debugLog("Path node positon: " + ReportRelativePosition(result.Position) + " from " + m_backward.m_startPosition + " + " + finishToPosition + ". original position: " + (parent.Position + neighbour * m_nodeDistance),
			//	secondaryState: SetName(pnSet));
			pnSet.m_openNodes.Insert(result, resultKey);
			//ShowPosition(result, resultKey.ToString());
			Profiler.EndProfileBlock();
		}

		/// <summary>
		/// Create a PathNode between parent and destination. Only for forward search.
		/// </summary>
		private void CreatePathNodeLine(ref PathNode parent)
		{
			Vector3D direction = parent.DirectionFromParent;
			Vector3D disp; Vector3D.Multiply(ref direction, m_nodeDistance, out disp);
			Vector3D position; Vector3D.Add(ref parent.Position, ref disp, out position);

			PathNode result = new PathNode()
			{
				DirectionFromParent = parent.DirectionFromParent,
				DistToCur = parent.DistToCur + m_nodeDistance,
				ParentKey = parent.Key,
				Position = position
			};
			// do not bother with key as there will only be one open node
			m_forward.m_openNodes.Insert(result, 0f);

			m_logger.debugLog("Next position: " + ReportRelativePosition(result.Position));
		}

		private double MinPathDistance(ref Vector3D displacement)
		{
			//return Math.Abs(displacement.X) + Math.Abs(displacement.Y) + Math.Abs(displacement.Z); // going for more of a current-best approach

			double X = Math.Abs(displacement.X), Y = Math.Abs(displacement.Y), Z = Math.Abs(displacement.Z), temp;

			// sort so that X is min and Z is max

			if (Y < X)
			{
				temp = X;
				X = Y;
				Y = temp;
			}
			if (Z < Y)
			{
				temp = Y;
				Y = Z;
				Z = temp;
				if (Y < X)
				{
					temp = X;
					X = Y;
					Y = temp;
				}
			}

			m_logger.debugLog("Sorting failed: " + X + ", " + Y + ", " + Z, Logger.severity.ERROR, condition: X > Y || X > Z || Y > Z);

			return X * MathHelper.Sqrt3 + (Y - X) * MathHelper.Sqrt2 + Z - Y;
		}

		private void OutOfNodes()
		{
			if (m_nodeDistance >= 2f)
			{
				m_logger.debugLog("No path found, halving node distance to " + (m_nodeDistance * 0.5f), Logger.severity.INFO);
				//Logger.DebugNotify("Halving node distance", level: Logger.severity.INFO);
				m_nodeDistance *= 0.5f;

				if (m_forward.m_reachedNodes.Count > 1)
				{
					PathNode node;
					IEnumerator<PathNode> enumerator = m_forward.m_reachedNodes.Values.GetEnumerator();
					while (enumerator.MoveNext())
					{
						node = enumerator.Current;
						CreatePathNodes(ref node, true);
					}
					enumerator.Dispose();
				}
				else
					m_forward.Clear();

				if (m_backward.m_reachedNodes.Count > 1)
				{
					PathNode node;
					IEnumerator<PathNode> enumerator = m_backward.m_reachedNodes.Values.GetEnumerator();
					while (enumerator.MoveNext())
					{
						node = enumerator.Current;
						CreatePathNodes(ref node, false);
					}
					enumerator.Dispose();
				}
				else
					m_backward.Clear();

				FindAPath();
				return;
			}

			m_logger.debugLog("Pathfinding failed", Logger.severity.WARNING);
			Logger.DebugNotify("Pathfinding failed", 10000, Logger.severity.WARNING);

#if PROFILE
			LogStats();
#endif
#if LOG_ENABLED
			LogStats();
#endif

			PathfindingFailed();
			return;
		}

		private void PathfindingFailed()
		{
			m_pathfinding = false;
			m_waitUntil = Globals.UpdateCount + 600uL;
			CurrentState = State.FailedToFindPath;
		}

		private void BuildPath(long forwardHash, long backwardHash = 0L)
		{
			m_path.Clear();
			PurgeTempGPS();

			PathNode node;
			if (!m_forward.m_reachedNodes.TryGetValue(forwardHash, out node))
			{
				m_logger.alwaysLog("Parent hash " + forwardHash + " not found in forward set, failed to build path", Logger.severity.ERROR);
				if (m_backward.m_reachedNodes.ContainsKey(forwardHash))
					m_logger.alwaysLog("Backward set does contains hash", Logger.severity.DEBUG);
				PathfindingFailed();
				return;
			}
			while (node.DistToCur != 0f)
			{
				//ShowPosition(node, "Path");
				m_path.AddFront(ref node.Position);
				if (!m_forward.m_reachedNodes.TryGetValue(node.ParentKey, out node))
				{
					m_logger.alwaysLog("Child hash " + forwardHash + " not found in forward set, failed to build path", Logger.severity.ERROR);
					if (m_backward.m_reachedNodes.ContainsKey(forwardHash))
						m_logger.alwaysLog("Backward set does contains hash", Logger.severity.DEBUG);
					PathfindingFailed();
					return;
				}
			}
			m_path.AddFront(ref node.Position);

			if (backwardHash != 0L)
			{
				if (!m_backward.m_reachedNodes.TryGetValue(backwardHash, out node))
				{
					m_logger.alwaysLog("Parent hash " + backwardHash + " not found in backward set, failed to build path", Logger.severity.ERROR);
					if (m_forward.m_reachedNodes.ContainsKey(forwardHash))
						m_logger.alwaysLog("Forward set does contains hash", Logger.severity.DEBUG);
					PathfindingFailed();
					return;
				}

				if (forwardHash == backwardHash && node.ParentKey != 0L)
				{
					if (!m_backward.m_reachedNodes.TryGetValue(node.ParentKey, out node))
					{
						m_logger.alwaysLog("First child hash " + backwardHash + " not found in backward set, failed to build path", Logger.severity.ERROR);
						if (m_forward.m_reachedNodes.ContainsKey(forwardHash))
							m_logger.alwaysLog("Forward set does contains hash", Logger.severity.DEBUG);
						PathfindingFailed();
					}
				}
				while (node.DistToCur != 0f)
				{
					//ShowPosition(node, "Path");
					m_path.AddBack(ref node.Position);
					if (!m_backward.m_reachedNodes.TryGetValue(node.ParentKey, out node))
					{
						m_logger.alwaysLog("Child hash " + backwardHash + " not found in backward set, failed to build path", Logger.severity.ERROR);
						if (m_forward.m_reachedNodes.ContainsKey(forwardHash))
							m_logger.alwaysLog("Forward set does contains hash", Logger.severity.DEBUG);
						PathfindingFailed();
					}
				}
			}

#if PROFILE
			LogStats();
#endif
#if LOG_ENABLED
			LogStats();
#endif

			m_pathfinding = false;
			m_logger.debugLog("Built path", Logger.severity.INFO);
			//Logger.DebugNotify("Finished Pathfinding", level: Logger.severity.INFO);
			SetNextPathTarget();
		}

		private bool SetNextPathTarget()
		{
			m_logger.debugLog("Path is empty", Logger.severity.ERROR, condition: m_path.Count == 0);
			m_logger.debugLog("Path is complete", Logger.severity.ERROR, condition: m_path.Count == 1);

			Vector3D obstructionPosition = m_obstructingEntity.GetPosition();
			Vector3D currentPosition; Vector3D.Subtract(ref m_currentPosition, ref obstructionPosition, out currentPosition);

			int target;
			for (target = m_path.m_target == 0 ? m_path.Count - 1 : m_path.m_target - 1; target > 0; target--)
			{
				m_logger.debugLog("Trying potential target #" + target + ": " + m_path.m_postions[target]);
				Line line = new Line(currentPosition, m_path.m_postions[target], false);
				if (CanTravelSegment(ref Vector3D.Zero, ref line))
				{
					m_logger.debugLog("Next target is position #" + target);
					m_path.m_target = target;
					return true;
				}
			}

			m_logger.debugLog("Failed to set next target", Logger.severity.INFO);
			m_path.Clear();
			return false;
		}

		private string ReportRelativePosition(Vector3D position)
		{
			return position + " => " + (position + m_obstructingEntity.GetPosition());
		}

		private void LogStats()
		{
			foreach (Vector3D position in m_path.m_postions)
				m_logger.alwaysLog("Waypoint: " + ReportRelativePosition(position));			
#if PROFILE
			m_logger.alwaysLog("Nodes reached: " + (m_forward.m_reachedNodes.Count + m_backward.m_reachedNodes.Count) + ", unreachable: " + (m_forward.m_unreachableNodes + m_backward.m_unreachableNodes) +
				", open: " + (m_forward.m_openNodes.Count + m_backward.m_openNodes.Count) + ", path length: " + m_path.Count);
#else
			m_logger.alwaysLog("Nodes reached: " + (m_forward.m_reachedNodes.Count + m_backward.m_reachedNodes.Count) +
				", open: " + (m_forward.m_openNodes.Count + m_backward.m_openNodes.Count) + ", path length: " + m_path.Count);
#endif
		}

#if LOG_ENABLED
		private Queue<IMyGps> m_shownPositions = new Queue<IMyGps>(100);
		private List<IMyGps> m_allGpsList = new List<IMyGps>();
#endif

		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void ShowPosition(PathNode currentNode, string name = null)
		{
			IMyGps gps = MyAPIGateway.Session.GPS.Create(name ?? currentNode.Position.ToString(), string.Empty, currentNode.Position + m_obstructingEntity.GetPosition(), true, true);
			gps.DiscardAt = MyAPIGateway.Session.ElapsedPlayTime;
			//m_logger.debugLog("Showing " + gps.Coords);
			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => MyAPIGateway.Session.GPS.AddLocalGps(gps));
#if LOG_ENABLED
			m_shownPositions.Enqueue(gps);
			if (m_shownPositions.Count == 100)
			{
				IMyGps remove = m_shownPositions.Dequeue();
				//m_logger.debugLog("Removing " + remove.Coords);
				MyAPIGateway.Utilities.TryInvokeOnGameThread(() => MyAPIGateway.Session.GPS.RemoveLocalGps(remove));
			}
#endif
		}

		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void PurgeTempGPS()
		{
#if LOG_ENABLED
			m_shownPositions.Clear();
			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				foreach (IMyGps gps in MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId))
					if (gps.DiscardAt.HasValue && gps.DiscardAt.Value < MyAPIGateway.Session.ElapsedPlayTime)
						MyAPIGateway.Session.GPS.RemoveLocalGps(gps);
			});
#endif
		}

		private string SetName(PathNodeSet pnSet)
		{
			return pnSet.m_openNodes == m_forward.m_openNodes ? "Forward" : "Backward";
		}

		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void DebugRectClose(ref PathNodeSet pnSet, Vector3D position)
		{
			foreach (PathNode otherNode in pnSet.m_reachedNodes.Values)
				if (otherNode.Position != m_forward.m_startPosition)
				{
					double distRect = Vector3D.RectangularDistance(position, otherNode.Position);
					if (distRect < m_nodeDistance)
					{
						Vector3D positionSteps; StepCount(ref position, out positionSteps);
						Vector3D otherNodePosition = otherNode.Position;
						Vector3D otherSteps; StepCount(ref otherNodePosition, out otherSteps);
						m_logger.debugLog(ReportRelativePosition(position) + " @ " + positionSteps + " steps is less than " + m_nodeDistance + " m from: " + ReportRelativePosition(otherNode.Position) + " @ " + otherSteps + " steps", Logger.severity.WARNING);
					}
				}
		}

		private void StepCount(ref Vector3D position, out Vector3D steps)
		{
			Vector3D finishToPosition; Vector3D.Subtract(ref position, ref m_backward.m_startPosition, out finishToPosition);
			Vector3D.Divide(ref finishToPosition, m_nodeDistance, out steps);
		}

	}
}
