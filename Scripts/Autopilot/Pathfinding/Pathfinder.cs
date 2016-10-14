//#define LOG_ENABLED
//#define PROFILE

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
using Sandbox.ModAPI;
using VRage;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinding
{
	public partial class NewPathfinder
	{
		// TODO: repulse from incoming missiles
		// TODO: m_canChangeCourse

		public const float SpeedFactor = 2f, VoxelAdd = 10f;
		/// <summary>of m_nodeDistance</summary>
		private const float DestinationRadius = 0.1f;

		private static ThreadManager ThreadForeground;
		private static ThreadManager ThreadBackground;

		static NewPathfinder()
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

		// Inputs: rarely change

		private MyCubeGrid m_autopilotGrid { get { return m_tester.AutopilotGrid; } set { m_tester.AutopilotGrid = value; } }
		private PseudoBlock m_navBlock;
		private Destination m_destination; // only match speed with entity if there is no obstruction
		private MyEntity m_ignoreEntity;
		private bool m_ignoreVoxel, m_canChangeCourse, m_isLanding;

		// Inputs: always updated

		private Vector3 m_addToVelocity;
		private Vector3D m_currentPosition, m_destWorld;
		private Vector3 m_autopilotVelocity;
		private float m_autopilotShipBoundingRadius;

		// High Level

		private SphereClusters m_clusters = new SphereClusters();
		/// <summary>First this list is populated by pruning structure, when calculating repulsion it is populated with entities to be avoided.</summary>
		private List<MyEntity> m_entitiesPruneAvoid = new List<MyEntity>();
		/// <summary>List of entities that will repulse the autopilot.</summary>
		private List<MyEntity> m_entitiesRepulse = new List<MyEntity>();

		// Low Level

		private Path m_path = new Path(false);
		private float m_nodeDistance;
		/// <summary>Position of the node that was reached. Move to while pathfinding or while path to next node is blocked.</summary>
		private Vector3D m_currentPathPosition;

		private PathNodeSet m_forward = new PathNodeSet(false), m_backward = new PathNodeSet(false);

		// Results

		private Vector3 m_targetDirection = Vector3.Invalid;
		private float m_targetDistance;
		/// <summary>Top most obstructing entity</summary>
		private MyEntity m_obstructingEntity;
		private MyCubeBlock m_obstructingBlock;

		public MyEntity ReportedObstruction { get { return m_obstructingBlock ?? m_obstructingEntity; } }
		public Mover Mover { get; private set; }
		public AllNavigationSettings NavSet { get { return Mover.NavSet; } }
		public RotateChecker RotateCheck { get { return Mover.RotateCheck; } }

		public NewPathfinder(ShipControllerBlock block)
		{
			m_logger = new Logger(() => m_autopilotGrid.getBestName(), () => {
				if (m_navBlock != null)
					return m_navBlock.DisplayName;
				return "N/A";
			});
			Mover = new Mover(block, new RotateChecker(block.CubeBlock, CollectEntities));
		}

		// Methods

		public void Halt()
		{
			m_runHalt = true;
		}

		public void HoldPosition(Vector3 velocity)
		{
			m_runHalt = false;

			m_logger.debugLog("Not yet threaded", Logger.severity.WARNING);

			// maybe interrupt, wait for exclusive and execute on mover?

			Mover.CalcMove(NavSet.Settings_Current.NavigationBlock, ref Vector3.Zero, 0f, ref velocity);
		}

		public void MoveTo(PseudoBlock navBlock, LastSeen targetEntity, Vector3D offset = default(Vector3D), Vector3 addToVelocity = default(Vector3), bool isLanding = false)
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
			MoveTo(navBlock, ref dest, addToVelocity, isLanding);
		}

		public void MoveTo(PseudoBlock navBlock, ref Destination destination, Vector3 addToVelocity = default(Vector3), bool isLanding = false)
		{
			m_runHalt = false;

			AllNavigationSettings.SettingsLevel level = Mover.NavSet.Settings_Current;
			MyEntity ignoreEntity = (MyEntity)level.DestinationEntity;
			bool ignoreVoxel = level.IgnoreAsteroid;
			bool canChangeCourse = level.PathfinderCanChangeCourse;

			m_addToVelocity = addToVelocity;

			if (m_navBlock == navBlock && m_autopilotGrid == navBlock.Grid && m_destination.Equals(ref destination) && m_ignoreEntity == ignoreEntity && m_ignoreVoxel == ignoreVoxel && m_canChangeCourse == canChangeCourse && m_isLanding == isLanding)
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
			m_logger.debugLog("is landing changed from " + m_isLanding + " to " + isLanding, condition: m_isLanding != isLanding);

			m_runInterrupt = true;
			m_navBlock = navBlock;
			m_autopilotGrid = (MyCubeGrid)m_navBlock.Grid;
			m_destination = destination;
			m_ignoreEntity = ignoreEntity;
			m_ignoreVoxel = ignoreVoxel;
			m_canChangeCourse = canChangeCourse;
			m_isLanding = isLanding;

			ThreadForeground.EnqueueAction(Run);
		}

		private void Run()
		{
			if (!m_runningLock.TryAcquireExclusive())
				return;
			try
			{
				if (m_runHalt)
					return;
				if (m_runInterrupt)
				{
					m_path.Clear();
					m_forward.Clear();
					m_backward.Clear();
				}
				else if (m_forward.m_reachedNodes.Count != 0)
					return;

				m_runInterrupt = false;

				UpdateInputs();
				TestCurrentPath();
				OnComplete();
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
			else if (m_forward.m_reachedNodes.Count != 0)
				ThreadBackground.EnqueueAction(ContinuePathfinding);
		}

		private void UpdateInputs()
		{
			m_currentPosition = m_autopilotGrid.GetCentre();
			FillDestWorld();
			m_autopilotVelocity = m_autopilotGrid.Physics.LinearVelocity;
			m_autopilotShipBoundingRadius = m_autopilotGrid.PositionComp.LocalVolume.Radius;
		}

		private void FillDestWorld()
		{
			Vector3D finalDestWorld = m_destination.WorldPosition() + m_currentPosition - m_navBlock.WorldPosition;
			double distance; Vector3D.Distance(ref finalDestWorld, ref m_currentPosition, out distance);
			NavSet.Settings_Current.Distance = (float)distance;

			m_logger.debugLog("Missing obstruction", Logger.severity.ERROR, condition: m_path.Count != 0 && m_obstructingEntity == null);

			m_destWorld = m_path.Count == 0 ? finalDestWorld : m_path.Peek() + m_obstructingEntity.PositionComp.GetPosition();

			//m_logger.debugLog("final dest world: " + finalDestWorld + ", distance: " + distance + ", is final: " + (m_path.Count == 0) + ", dest world: " + m_destWorld);
		}

		/// <summary>
		/// Instructs Mover to calculate movement
		/// </summary>
		private void OnComplete()
		{
			if (m_runInterrupt || m_runHalt)
				return;

			MyEntity relativeEntity = m_obstructingEntity ?? (MyEntity)m_destination.Entity;
			Vector3 targetVelocity = relativeEntity != null && relativeEntity.Physics != null && !relativeEntity.Physics.IsStatic ? relativeEntity.Physics.LinearVelocity : Vector3.Zero;
			if (m_path.Count == 0)
			{
				Vector3 temp; Vector3.Add(ref targetVelocity, ref m_addToVelocity, out temp);
				targetVelocity = temp;
			}

			if (!m_targetDirection.IsValid())
			{
				// while pathfinding, move to start node
				if (m_forward.m_reachedNodes.Count != 0)
				{
					Vector3D obstructPosition = m_obstructingEntity.PositionComp.GetPosition();
					Vector3D firstNodeWorld; Vector3D.Add(ref m_currentPathPosition, ref obstructPosition, out firstNodeWorld);
					Vector3D toFirst; Vector3D.Subtract(ref firstNodeWorld, ref m_currentPosition, out toFirst);
					double distance = toFirst.Normalize();
					Vector3 direction = toFirst;
					//m_logger.debugLog("moving to start node, disp: " + (direction * (float)distance) + ", dist: " + distance + ", obstruct: " + obstructPosition + ", first node: " + m_firstNodePosition + ", first node world: " + firstNodeWorld + ", current: " + m_currentPosition);
					Mover.CalcMove(m_navBlock, ref direction, (float)distance, ref targetVelocity, m_isLanding);
				}
				else
				{
					// maintain postion to obstruction / destination
					//m_logger.debugLog("maintaining position");
					Mover.CalcMove(m_navBlock, ref Vector3.Zero, 0f, ref targetVelocity, m_isLanding);
				}
			}
			else
			{
				//m_logger.debugLog("move to position: " + (m_navBlock.WorldPosition + m_targetDirection * m_targetDistance));
				Mover.CalcMove(m_navBlock, ref m_targetDirection, m_targetDistance, ref targetVelocity, m_isLanding);
			}
		}

		private void TestCurrentPath()
		{
			if (m_path.Count != 0)
			{
				// don't chase an obstructing entity unless it is the destination
				if (m_obstructingEntity != m_destination.Entity && ObstructionMovingAway())
				{
					m_logger.debugLog("Obstruction is moving away, I'll just wait here");
					m_path.Clear();
					m_obstructingEntity = null;
					m_obstructingBlock = null;
					m_targetDirection = Vector3.Invalid;
					return;
				}

				// if near waypoint, pop it
				float destRadius = DestinationRadius * m_nodeDistance;
				double distanceToDest; Vector3D.DistanceSquared(ref m_currentPosition, ref m_destWorld, out distanceToDest);
				if (distanceToDest < destRadius * destRadius)
				{
					m_logger.debugLog("Reached waypoint: " + m_path.Peek() + ", remaining: " + (m_path.Count - 1), Logger.severity.DEBUG);
					m_path.Pop(out m_currentPathPosition);
					FillDestWorld();
					if (m_path.Count == 0)
						Logger.DebugNotify("Completed path", level: Logger.severity.INFO);
				}
			}

			m_logger.debugLog("Current position: " + m_currentPosition + ", destination: " + m_destWorld);

			FillEntitiesLists();

			Vector3 repulsion;
			CalcRepulsion(m_path.Count == 0, out repulsion);

			Vector3D disp; Vector3D.Subtract(ref m_destWorld, ref m_currentPosition, out disp);
			double targetDistance = disp.Length();
			Vector3D direct; Vector3D.Divide(ref disp, targetDistance, out direct);
			Vector3 directF = direct;
			m_targetDistance = (float)targetDistance;
			m_logger.debugLog("directF: " + directF + ", repulsion: " + repulsion);
			Vector3.Add(ref directF, ref repulsion, out m_targetDirection);
			MyEntity obstructing;
			MyCubeBlock block;
			Vector3D pointOfObstruction;
			if (CurrentObstructed(out obstructing, out pointOfObstruction, out block))
			{
				if (m_path.Count != 0)
				{
					// if 1 m < autopilot to m_currentNodePosition < destination radius, retry CurrentObstructed for moving to m_currentNodePosition

					Vector3D obstructPosition = m_obstructingEntity.PositionComp.GetPosition();
					Vector3D currentNodeWorldPos; Vector3D.Add(ref m_currentPathPosition, ref obstructPosition, out currentNodeWorldPos);
					Vector3D.Subtract(ref currentNodeWorldPos, ref m_currentPosition, out disp);
					double distanceSq = disp.LengthSquared();
					float destRadius = Math.Max(DestinationRadius * m_nodeDistance, 10f);
					if (distanceSq > 1d && distanceSq < destRadius * destRadius)
					{
						targetDistance = Math.Sqrt(distanceSq);
						Vector3D.Divide(ref disp, targetDistance, out direct);
						m_logger.debugLog("Trying to move to current node position, m_targetDirection: " + m_targetDirection + ", m_targetDistance: " + m_targetDistance + ", new m_targetDirection: " + direct + ", new m_targetDistance: " + targetDistance);
						m_targetDirection = direct;
						m_targetDistance = (float)targetDistance;
						if (!CurrentObstructed(out obstructing, out pointOfObstruction, out block))
						{
							m_logger.debugLog("Target direction: " + m_targetDirection + ", target distance: " + m_targetDistance);
							m_entitiesPruneAvoid.Clear();
							m_entitiesRepulse.Clear();
							return;
						}
					}
				}

				m_obstructingEntity = obstructing;
				m_obstructingBlock = block;

				m_entitiesPruneAvoid.Clear();
				m_entitiesRepulse.Clear();
				m_targetDirection = Vector3.Invalid;
				if (ObstructionMovingAway())
					return;
				FindAPath(ref pointOfObstruction);
				return;
			}

			m_logger.debugLog("Target direction: " + m_targetDirection + ", target distance: " + m_targetDistance);
			if (m_path.Count == 0)
			{
				m_obstructingEntity = null;
				m_obstructingBlock = null;
			}
			m_entitiesPruneAvoid.Clear();
			m_entitiesRepulse.Clear();
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
				else if (entity is MyVoxelBase && m_ignoreVoxel)
					continue;
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

				MyPlanet planet = entity as MyPlanet;
				if (planet != null)
				{
					boundingRadius = planet.MaximumRadius;
					Vector3.Dot(ref m_autopilotVelocity, ref toCentre, out linearSpeed);
				}
				else
				{
					boundingRadius = entity.PositionComp.LocalVolume.Radius;
					if (entity.Physics == null)
						Vector3.Dot(ref m_autopilotVelocity, ref toCentre, out linearSpeed);
					else
					{
						Vector3 obVel = entity.Physics.LinearVelocity;
						Vector3 relVel;
						Vector3.Subtract(ref m_autopilotVelocity, ref  obVel, out relVel);
						Vector3.Dot(ref relVel, ref toCentre, out linearSpeed);
					}
				}
				//m_logger.debugLog("For entity: " + entity.nameWithId() + ", bounding radius: " + boundingRadius + ", autopilot ship radius: " + m_autopilotShipBoundingRadius + ", linear speed: " + linearSpeed + ", sphere radius: " +
				//	(boundingRadius + m_autopilotShipBoundingRadius + linearSpeed * SpeedFactor));
				if (linearSpeed <= 0f)
					linearSpeed = 0f;
				else
					linearSpeed *= SpeedFactor;
				boundingRadius += m_autopilotShipBoundingRadius + linearSpeed;

				if (distCentreToCurrent < boundingRadius)
				{
					// Entity is too close to autopilot for repulsion.
					m_entitiesPruneAvoid.Add(entity);
					continue;
				}

				boundingRadius *= 4f;

				if (distCentreToCurrent > distAutopilotToFinalDest)
				{
					double distSqCentreToDest; Vector3D.DistanceSquared(ref centre, ref m_destWorld, out distSqCentreToDest);
					if (distSqCentreToDest < boundingRadius * boundingRadius)
					{
						m_logger.debugLog("Entity is too close to dest for repulsion. entity: " + entity.nameWithId() + ", distCentreToCurrent: " + distCentreToCurrent + ", distAutopilotToFinalDest: " +
							distAutopilotToFinalDest + ", distSqCentreToDest: " + distSqCentreToDest + ", boundingRadius: " + boundingRadius);
						// Entity is too close to destination for repulsion.
						m_entitiesPruneAvoid.Add(entity);
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

		// TODO: Atmospheric ship might want to consider space as repulsor???
		/// <param name="sphere">The sphere which is repulsing the autopilot.</param>
		/// <param name="repulsion">A directional vector with length between 0 and 1, indicating the repulsive force.</param>
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

			//// If both entity and autopilot are near the destination, the repulsion radius must be reduced or we would never reach the destination.
			//double toDestLenSq;
			//Vector3D.DistanceSquared(ref sphere.Center, ref  m_destWorld, out toDestLenSq);

			//double distAutopiltoToDest; Vector3D.DistanceSquared(ref m_currentPosition, ref m_destWorld, out distAutopiltoToDest);
			//toDestLenSq += distAutopiltoToDest;

			////toDestLenSq += m_targetDistance * m_targetDistance; // Adjustable

			//if (toCurrentLenSq > toDestLenSq)
			//{
			//	// Autopilot is outside the boundaries of repulsion.
			//	return;
			//}

			//if (toDestLenSq < maxRepulseDistSq)
			//	maxRepulseDist = (float)Math.Sqrt(toDestLenSq);

			float toCurrentLen = (float)Math.Sqrt(toCurrentLenSq);
			// maxRepulseDist / toCurrentLen can be adjusted
			Vector3 result;
			Vector3.Multiply(ref toCurrent, (maxRepulseDist / toCurrentLen - 1f) / toCurrentLen, out result);
			m_logger.debugLog("toCurrent: " + toCurrent + ", maxRepulseDist: " + maxRepulseDist + ", toCurrentLen: " + toCurrentLen + ", ratio: " + (maxRepulseDist / toCurrentLen) + ", result: " + result);
			Vector3 result2;
			Vector3.Add(ref repulsion, ref result, out result2);
			m_logger.debugLog("repulsion: " + result2);
			repulsion = result2;
		}

		/// <summary>
		/// For testing if the current path is obstructed by any entity in m_entitiesPruneAvoid.
		/// </summary>
		private bool CurrentObstructed(out MyEntity obstructingEntity, out Vector3D pointOfObstruction, out MyCubeBlock obstructBlock)
		{
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

			//if (!m_ignoreVoxel)
			//{
			//	//m_logger.debugLog("raycasting voxels");

			//	Vector3 velocityMulti; Vector3.Multiply(ref m_autopilotVelocity, SpeedFactor, out velocityMulti);
			//	Vector3 directionMulti; Vector3.Multiply(ref m_targetDirection, VoxelAdd, out directionMulti);
			//	Vector3 rayDirection; Vector3.Add(ref velocityMulti, ref directionMulti, out rayDirection);
			//	IHitInfo hit;
			//	if (m_tester.RayCastIntersectsVoxel(ref Vector3D.Zero, ref rayDirection, out hit))
			//	{
			//		m_logger.debugLog("Obstructed by voxel " + hit.HitEntity + " at " + hit.Position);
			//		obstructingEntity = (MyEntity)hit.HitEntity;
			//		pointOfObstruction = hit.Position;
			//		obstructBlock = null;
			//		return true;
			//	}
			//}

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

			m_logger.debugLog("No obstruction");
			obstructingEntity = null;
			pointOfObstruction = Vector3.Invalid;
			obstructBlock = null;
			return false;
		}

		private bool ObstructionMovingAway()
		{
			Vector3D velocity = m_obstructingEntity.Physics.LinearVelocity;
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
		private void FindAPath(ref Vector3D obstructionPoint)
		{
			Logger.DebugNotify("Starting pathfinding", level: Logger.severity.INFO);

			m_forward.Clear();
			m_backward.Clear();
			m_path.Clear();
			FillDestWorld();
			double distCurObs, distObsDest;
			Vector3D.Distance(ref m_currentPosition, ref obstructionPoint, out distCurObs);
			Vector3D.Distance(ref obstructionPoint, ref m_destWorld, out distObsDest);
			m_nodeDistance = NavSet.Settings_Current.DestinationRadius;

			m_logger.debugLog("m_obstructingEntity == null", Logger.severity.ERROR, condition: m_obstructingEntity == null);

			Vector3D relativePosition;
			Vector3D obstructPosition = m_obstructingEntity.PositionComp.GetPosition();
			Vector3D.Subtract(ref m_currentPosition, ref obstructPosition, out relativePosition);

			m_logger.debugLog("current position: " + m_currentPosition + ", obs: " + obstructPosition + ", relative: " + relativePosition);

			PathNode startNode = new PathNode() { Position = new Vector3D(Math.Round(relativePosition.X), Math.Round(relativePosition.Y), Math.Round(relativePosition.Z)) };
			m_currentPathPosition = startNode.Position;
			m_forward.m_openNodes.Insert(startNode, 0f);
			m_forward.m_reachedNodes.Add(startNode.Position.GetHash(), startNode);

			Vector3D.Subtract(ref m_destWorld, ref obstructPosition, out relativePosition);
			// backwards start node needs to be a discrete number of steps from forward startNode
			Vector3D startToFinish; Vector3D.Subtract(ref relativePosition, ref startNode.Position, out startToFinish);
			Vector3D steps; Vector3D.Divide(ref startToFinish, m_nodeDistance, out steps);
			Vector3D discreteSteps = new Vector3D() { X = Math.Round(steps.X), Y = Math.Round(steps.Y), Z = Math.Round(steps.Z) };
			Vector3D.Multiply(ref discreteSteps, m_nodeDistance, out startToFinish);
			Vector3D.Add(ref startNode.Position, ref startToFinish, out relativePosition);
				
			startNode = new PathNode() { Position = new Vector3D(Math.Round(relativePosition.X), Math.Round(relativePosition.Y), Math.Round(relativePosition.Z)) };
			m_backward.m_openNodes.Insert(startNode, 0f);
			m_backward.m_reachedNodes.Add(startNode.Position.GetHash(), startNode);
		}

		private void ContinuePathfinding()
		{
			if (!m_runningLock.TryAcquireExclusive())
				return;
			try
			{
				if (m_runHalt || m_runInterrupt)
					return;
				UpdateInputs();
				if (m_forward.m_openNodes.Count <= m_backward.m_openNodes.Count)
					FindAPath(ref m_forward);
				else
					FindAPath(ref m_backward);
				OnComplete();
			}
			finally { m_runningLock.ReleaseExclusive(); }

			PostRun();
		}

		/// <summary>
		/// Continues pathfinding.
		/// </summary>
		private void FindAPath(ref PathNodeSet pnSet)
		{
			m_logger.debugLog("m_obstructingEntity == null", Logger.severity.ERROR, condition: m_obstructingEntity == null, secondaryState: SetName(pnSet));

			if (pnSet.m_openNodes.Count == 0)
			{
				OutOfNodes();
				return;
			}

			PathNode currentNode = pnSet.m_openNodes.RemoveMin();
			if (currentNode.DistToCur == 0f)
			{
				m_logger.debugLog("first node: " + ReportRelativePosition(currentNode.Position), secondaryState: SetName(pnSet));
				CreatePathNodes(ref currentNode, ref pnSet);
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

			Vector3D obstructPosition = m_obstructingEntity.PositionComp.GetPosition();
			PathNode parent = pnSet.m_reachedNodes[currentNode.ParentKey];
			Vector3D worldParent;
			Vector3D.Add(ref obstructPosition, ref parent.Position, out worldParent);
			Vector3D offset; Vector3D.Subtract(ref worldParent, ref m_currentPosition, out offset);
			Line line = new Line() { From = parent.Position, To = currentNode.Position, Direction = currentNode.DirectionFromParent, Length = currentNode.DistToCur - parent.DistToCur };

			if (!CanTravelSegment(ref offset, ref line))
			{
#if PROFILE
				direction.m_unreachableNodes++;
#endif
				m_logger.debugLog("Not reachable: " + ReportRelativePosition(currentNode.Position), secondaryState: SetName(pnSet));
				//ShowPosition(currentNode);
				return;
			}
			m_logger.debugLog("Reached node: " + ReportRelativePosition(currentNode.Position) + " from " + ReportRelativePosition(pnSet.m_reachedNodes[currentNode.ParentKey].Position) + ", reached: " + pnSet.m_reachedNodes.Count + ", open: " + pnSet.m_openNodes.Count, secondaryState: SetName(pnSet));
			long cNodePosHash = currentNode.Position.GetHash();
			pnSet.m_reachedNodes.Add(cNodePosHash, currentNode);
			ShowPosition(currentNode, SetName(pnSet));

			// TODO: blue sky matching

			PathNodeSet otherDirection = pnSet.m_openNodes == m_forward.m_openNodes ? m_backward : m_forward;
			if (otherDirection.m_reachedNodes.ContainsKey(cNodePosHash))
				BuildPath(cNodePosHash);
			else
			{
				DebugRectClose(ref otherDirection, currentNode.Position); // TODO verify that there is no issue for far from zero worlds, then remove
				CreatePathNodes(ref currentNode, ref pnSet);
			}

			//				// need to keep checking if the destination can be reached from every node
			//				// this doesn't indicate a clear path but allows repulsion to resume

			//				Vector3D obstructPosition = m_obstructingEntity.PositionComp.GetPosition();
			//				Vector3D worldPosition; Vector3D.Add(ref obstructPosition, ref currentNode.Position, out worldPosition);
			//				Vector3D offset; Vector3D.Subtract(ref worldPosition, ref m_currentPosition, out offset);

			//				Line line = new Line(worldPosition, m_destWorld, false);
			//				//m_logger.debugLog("line to dest: " + line.From + ", " + line.To + ", " + line.Direction + ", " + line.Length);
			//				if (CanTravelSegment(ref offset, ref line))
			//				{
			//					m_logger.debugLog("Can reach final destination from a node, finished pathfinding. Final node: " + ReportRelativePosition(currentNode.Position), Logger.severity.DEBUG);
			//					Logger.DebugNotify("Finished pathfinding", level: Logger.severity.INFO);
			//					m_path.Clear();
			//					PathNode node = currentNode;
			//					while (node.DistToCur != 0f)
			//					{
			//						ShowPosition(node);
			//						m_path.Push(node.Position);
			//						node = direction.m_reachedNodes[node.ParentKey];
			//					}

			//#if PROFILE
			//					LogStats();
			//#endif
			//#if LOG_ENABLED
			//					LogStats();
			//#endif

			//					m_forward.Clear();
			//					m_backward.Clear();

			//					m_logger.alwaysLog("Following path", Logger.severity.INFO);
			//					return;
			//				}
			//}
			//else
			//	m_logger.debugLog("first node: " + ReportRelativePosition(currentNode.Position));
		}

		//private bool CanReachFromParent(ref PathNode node)
		//{
		//	m_logger.debugLog("m_obstructingEntity == null", Logger.severity.ERROR, condition: m_obstructingEntity == null);

		//	Vector3D obstructPosition = m_obstructingEntity.PositionComp.GetPosition();

		//	PathNode parent = m_reachedNodes[node.ParentKey];
		//	Vector3D worldParent;
		//	Vector3D.Add(ref obstructPosition, ref parent.Position, out worldParent);

		//	Vector3D offset; Vector3D.Subtract(ref worldParent, ref m_currentPosition, out offset);

		//	Line line = new Line() { From = parent.Position, To = node.Position, Direction = node.DirectionFromParent, Length = node.DistToCur - parent.DistToCur };
		//	return CanTravelSegment(ref offset, ref line);
		//}

		private bool CanTravelSegment(ref Vector3D offset, ref Line line)
		{
			Profiler.StartProfileBlock();

			MyCubeBlock ignoreBlock = m_ignoreEntity as MyCubeBlock;

			//if (!m_ignoreVoxel)
			//{
			//	//m_logger.debugLog("raycasting voxels");

			//	Vector3 adjustment; Vector3.Multiply(ref line.Direction, VoxelAdd, out adjustment);
			//	Vector3 disp; Vector3.Subtract(ref line.To, ref line.From, out disp);
			//	Vector3 rayTest; Vector3.Add(ref disp, ref adjustment, out rayTest);
			//	IHitInfo hit;
			//	if (m_tester.RayCastIntersectsVoxel(ref offset, ref rayTest, out hit))
			//	{
			//		m_logger.debugLog("Obstructed by voxel " + hit.HitEntity + " at " + hit.Position, Logger.severity.DEBUG);
			//		Profiler.EndProfileBlock();
			//		return false;
			//	}
			//}

			//m_logger.debugLog("checking " + m_entitiesRepulse.Count + " entites - voxels");

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

			m_logger.debugLog("No obstruction");
			Profiler.EndProfileBlock();
			return true;
		}

		private void CreatePathNodes(ref PathNode currentNode, ref PathNodeSet pnSet)
		{
			long currentKey = currentNode.Position.GetHash();
			foreach (Vector3I neighbour in Globals.NeighboursOne)
				CreatePathNode(currentKey, ref currentNode, neighbour, 1f, ref pnSet);
			foreach (Vector3I neighbour in Globals.NeighboursTwo)
				CreatePathNode(currentKey, ref currentNode, neighbour, MathHelper.Sqrt2, ref pnSet);
			foreach (Vector3I neighbour in Globals.NeighboursThree)
				CreatePathNode(currentKey, ref currentNode, neighbour, MathHelper.Sqrt3, ref pnSet);
		}

		private void CreatePathNode(long parentKey, ref PathNode parent, Vector3I neighbour, float distMulti, ref PathNodeSet pnSet)
		{
			Profiler.StartProfileBlock();

			Vector3D position = parent.Position + neighbour * m_nodeDistance;
			position = new Vector3D(Math.Round(position.X), Math.Round(position.Y), Math.Round(position.Z));
			long positionHash = position.GetHash();

			if (pnSet.m_reachedNodes.ContainsKey(positionHash))
			{
				Profiler.EndProfileBlock();
				return;
			}

			Vector3D disp; Vector3D.Subtract(ref position, ref parent.Position, out disp);
			Vector3 neighbourF = neighbour;
			Vector3 direction; Vector3.Divide(ref neighbourF, distMulti, out direction);

			float turn; Vector3.Dot(ref parent.DirectionFromParent, ref direction, out turn);

			if (turn < 0f)
			{
				Profiler.EndProfileBlock();
				return;
			}

			float distToCur = parent.DistToCur + m_nodeDistance * distMulti;
			Vector3D obstructionPosition = m_obstructingEntity.PositionComp.GetPosition();
			Vector3D currentWorldPosition; Vector3D.Add(ref position, ref obstructionPosition, out currentWorldPosition);
			Vector3D dest = pnSet.m_openNodes == m_forward.m_openNodes ? m_destWorld : m_currentPosition;
			Vector3D dispToDest; Vector3D.Subtract(ref dest, ref currentWorldPosition, out dispToDest);
			double distToDest = MinPathDistance(ref dispToDest);
			//double distToDest = dispToDest.Length();
			float resultKey = distToCur + (float)distToDest;

			if (turn > 0.99f && parent.ParentKey != 0f)
				parentKey = parent.ParentKey;
			else
			{
				if (turn < 0f)
				{
					Profiler.EndProfileBlock();
					return;
				}
				//m_logger.debugLog("Pathfinder node backtracks to parent. Position: " + position + ", parent position: " + parent.Position +
				//	"\ndirection: " + direction + ", parent direction: " + parent.DirectionFromParent, Logger.severity.FATAL, condition: turn < -0.99f);
				resultKey += m_nodeDistance * 10f * (1f - turn);
			}

			PathNode result = new PathNode()
			{
				ParentKey = parentKey,
				DistToCur = distToCur,
				Position = position,
				DirectionFromParent = direction,
			};

			m_logger.debugLog("resultKey <= 0", Logger.severity.ERROR, condition: resultKey <= 0f);
			//m_logger.debugLog("path node: " + resultKey + ", " + result.ParentKey + ", " + result.DistToCur + ", " + result.Position + " => " + (m_obstructingEntity.PositionComp.GetPosition() + result.Position) + ", " + result.DirectionFromParent);
			pnSet.m_openNodes.Insert(result, resultKey);
			//ShowPosition(result, resultKey.ToString());
			Profiler.EndProfileBlock();
		}

		private double MinPathDistance(ref Vector3D displacement)
		{
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
				m_logger.debugLog("No path found, halving node distance", Logger.severity.INFO);
				Logger.DebugNotify("Halving node distance", level: Logger.severity.INFO);
				m_nodeDistance *= 0.5f;

				PathNode node;
				IEnumerator<PathNode> enumerator = m_forward.m_reachedNodes.Values.GetEnumerator();
				while (enumerator.MoveNext())
				{
					node = enumerator.Current;
					CreatePathNodes(ref node, ref m_forward);
				}
				enumerator.Dispose();

				enumerator = m_backward.m_reachedNodes.Values.GetEnumerator();
				while (enumerator.MoveNext())
				{
					node = enumerator.Current;
					CreatePathNodes(ref node, ref m_backward);
				}
				enumerator.Dispose();
				return;
			}

			m_logger.debugLog("Pathfinding failed", Logger.severity.WARNING);
			Logger.DebugNotify("Pathfinding failed", 30000, Logger.severity.WARNING);

#if PROFILE
			LogStats();
#endif
#if LOG_ENABLED
			LogStats();
#endif

			m_forward.Clear();
			m_backward.Clear();
			return;
		}

		private void BuildPath(long meetingHash)
		{
			m_logger.debugLog("m_path.Count != 0", Logger.severity.ERROR, condition: m_path.Count != 0);

			PathNode node = m_forward.m_reachedNodes[meetingHash];
			while (node.DistToCur != 0f)
			{
				ShowPosition(node);
				m_path.AddFront(ref node.Position);
				node = m_forward.m_reachedNodes[node.ParentKey];
			}

			node = m_backward.m_reachedNodes[m_backward.m_reachedNodes[meetingHash].ParentKey];
			while (node.DistToCur != 0f)
			{
				ShowPosition(node);
				m_path.AddBack(ref node.Position);
				node = m_backward.m_reachedNodes[node.ParentKey];
			}

#if PROFILE
			LogStats();
#endif
#if LOG_ENABLED
			LogStats();
#endif

			m_forward.Clear();
			m_backward.Clear();

			m_logger.alwaysLog("Built path", Logger.severity.INFO);
			Logger.DebugNotify("Finished Pathfinding", level: Logger.severity.INFO);
		}

		private string ReportRelativePosition(Vector3D position)
		{
			return position + " => " + (position + m_obstructingEntity.PositionComp.GetPosition());
		}

		private void LogStats()
		{
			foreach (Vector3D position in m_path.m_forward)
				m_logger.alwaysLog("Waypoint: " + ReportRelativePosition(position));
			foreach (Vector3D position in m_path.m_backward)
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
		private Queue<IMyGps> m_shownPositions = new Queue<IMyGps>(10);
#endif

		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void ShowPosition(PathNode currentNode, string name = null)
		{
			IMyGps gps = MyAPIGateway.Session.GPS.Create(name ?? currentNode.Position.ToString(), string.Empty, currentNode.Position + m_obstructingEntity.PositionComp.GetPosition(), true, true);
			gps.DiscardAt = MyAPIGateway.Session.ElapsedPlayTime;
			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => MyAPIGateway.Session.GPS.AddLocalGps(gps));
#if LOG_ENABLED			
			m_shownPositions.Enqueue(gps);
			if (m_shownPositions.Count == 10)
			{
				IMyGps remove = m_shownPositions.Dequeue();
				MyAPIGateway.Utilities.TryInvokeOnGameThread(() => MyAPIGateway.Session.GPS.RemoveLocalGps(remove));
			}
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
			{
				double distRect = Vector3D.RectangularDistance(position, otherNode.Position);
				if (distRect < m_nodeDistance)
					m_logger.debugLog(ReportRelativePosition(position) + " is less than " + m_nodeDistance + " m from: " + ReportRelativePosition(otherNode.Position), Logger.severity.ERROR);
			}
		}

	}
}
