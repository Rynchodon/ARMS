using System;
using System.Collections.Generic;
using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Rynchodon.Utility;
using Sandbox.Game.Entities;
using VRage;
using VRage.Collections;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinding
{
	public class NewPathfinder
	{
		// TODO: repulse from incoming missiles

		private struct PathNode
		{
			public long ParentKey;
			public float DistToCur;
			public Vector3D Position;
			public Vector3 DirectionFromParent;
		}

		public const float SpeedFactor = 2f, VoxelAdd = 10f;
		/// <summary>of m_nodeDistance</summary>
		private const float DestinationRadius = 0.1f;

		///// <summary>
		///// Shared will be held while setting interupt or running. Exclusive will be held while starting a run or checking for an interrupt.
		///// </summary>
		//private FastResourceLock m_runningLock;
		//private bool m_runInterrupt;

		private readonly Logger m_logger;
		private readonly PathTester m_tester = new PathTester();

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
		private float m_autopilotSpeed, m_autopilotShipBoundingRadius;

		// High Level

		private SphereClusters m_clusters = new SphereClusters();
		/// <summary>First this list is populated by pruning structure, when calculating repulsion it is populated with entities to be avoided.</summary>
		private List<MyEntity> m_entitiesPruneAvoid = new List<MyEntity>();
		/// <summary>List of entities that will repulse the autopilot.</summary>
		private List<MyEntity> m_entitiesRepulse = new List<MyEntity>();

		// Low Level

		private MyBinaryStructHeap<float, PathNode> m_openNodes = new MyBinaryStructHeap<float, PathNode>();
		private Dictionary<long, PathNode> m_reachedNodes = new Dictionary<long, PathNode>();
		private float m_nodeDistance;
		private Stack<Vector3D> m_path = new Stack<Vector3D>();
#if PROFILE
		private int m_unreachableNodes = 0;
#endif

		// Results

		private Vector3 m_targetDirection = Vector3.Invalid;
		private float m_targetDistance;

		public MyEntity ObstructingEntity { get; private set; }
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

		public void HoldPosition(Vector3 velocity)
		{
			Mover.CalcMove(NavSet.Settings_Current.NavigationBlock, ref Vector3.Zero, 0f, ref velocity);
		}

		public void MoveTo(PseudoBlock navBlock, LastSeen targetEntity, Vector3D offset = default(Vector3D), Vector3 addToVelocity = default(Vector3), bool isLanding = false)
		{
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
			AllNavigationSettings.SettingsLevel level = Mover.NavSet.Settings_Current;
			MyEntity ignoreEntity = (MyEntity)level.DestinationEntity;
			bool ignoreVoxel = level.IgnoreAsteroid;
			bool canChangeCourse = level.PathfinderCanChangeCourse;

			m_addToVelocity = addToVelocity;

			if (m_navBlock == navBlock && m_autopilotGrid == navBlock.Grid && m_destination.Equals(ref destination) && m_ignoreEntity == ignoreEntity && m_ignoreVoxel == ignoreVoxel && m_canChangeCourse == canChangeCourse && m_isLanding == isLanding)
			{
				UpdateInputs();
				TestCurrentPath();
				OnComplete();
				return;
			}

			m_logger.debugLog("nav block changed from " + (m_navBlock == null ? "N/A" : m_navBlock.DisplayName) + " to " + navBlock.DisplayName, condition: m_navBlock != navBlock);
			m_logger.debugLog("grid changed from " + m_autopilotGrid.getBestName() + " to " + navBlock.Grid.getBestName(), condition: m_autopilotGrid != navBlock.Grid);
			m_logger.debugLog("destination changed from " + m_destination + " to " + destination, condition: !m_destination.Equals(ref destination));
			m_logger.debugLog("ignore entity changed from " + m_ignoreEntity.getBestName() + " to " + ignoreEntity.getBestName(), condition: m_ignoreEntity != ignoreEntity);
			m_logger.debugLog("ignore voxel changed from " + m_ignoreVoxel + " to " + ignoreVoxel, condition: m_ignoreVoxel != ignoreVoxel);
			m_logger.debugLog("can change course changed from " + m_canChangeCourse + " to " + canChangeCourse, condition: m_canChangeCourse != canChangeCourse);
			m_logger.debugLog("is landing changed from " + m_isLanding + " to " + isLanding, condition: m_isLanding != isLanding);

			m_navBlock = navBlock;
			m_autopilotGrid = (MyCubeGrid)m_navBlock.Grid;
			m_destination = destination;
			m_ignoreEntity = ignoreEntity;
			m_ignoreVoxel = ignoreVoxel;
			m_canChangeCourse = canChangeCourse;
			m_isLanding = isLanding;
			m_path.Clear();
			UpdateInputs();

			TestCurrentPath();
			OnComplete();
		}

		private void UpdateInputs()
		{
			m_currentPosition = m_autopilotGrid.GetCentre();
			FillDestWorld();
			m_autopilotVelocity = m_autopilotGrid.Physics.LinearVelocity;
			m_autopilotSpeed = m_autopilotVelocity.Length();
			m_autopilotShipBoundingRadius = m_autopilotGrid.PositionComp.LocalVolume.Radius;
		}

		private void FillDestWorld()
		{
			Vector3D finalDestWorld = m_destination.WorldPosition() + m_currentPosition - m_navBlock.WorldPosition;
			double distance; Vector3D.Distance(ref finalDestWorld, ref m_currentPosition, out distance);
			NavSet.Settings_Current.Distance = (float)distance;

			m_logger.debugLog("Missing obstruction", Logger.severity.ERROR, condition: m_path.Count != 0 && ObstructingEntity == null);

			m_destWorld = m_path.Count == 0 ? finalDestWorld : m_path.Peek() + ObstructingEntity.PositionComp.GetPosition();

			//m_logger.debugLog("final dest world: " + finalDestWorld + ", distance: " + distance + ", is final: " + (m_path.Count == 0) + ", dest world: " + m_destWorld);
		}

		private void OnComplete()
		{
			MyEntity relativeEntity = ObstructingEntity ?? (MyEntity)m_destination.Entity;
			Vector3 targetVelocity = relativeEntity != null && relativeEntity.Physics != null && !relativeEntity.Physics.IsStatic ? relativeEntity.Physics.LinearVelocity  : Vector3.Zero;
			if (m_path.Count == 0)
			{
				Vector3 temp; Vector3.Add(ref targetVelocity, ref m_addToVelocity, out temp);
				targetVelocity = temp;
			}

			if (!m_targetDirection.IsValid())
				// maintain postion to obstruction / destination
				Mover.CalcMove(m_navBlock, ref Vector3.Zero, 0f, ref targetVelocity, m_isLanding);
			else
				Mover.CalcMove(m_navBlock, ref m_targetDirection, m_targetDistance, ref targetVelocity, m_isLanding);
		}

		private void TestCurrentPath()
		{
			if (m_path.Count != 0)
			{
				// don't chase an obstructing entity unless it is the destination
				if (ObstructingEntity != m_destination.Entity && ObstructionMovingAway())
				{
					m_logger.debugLog("Obstruction is moving away, I'll just wait here");
					m_path.Clear();
					ObstructingEntity = null;
					m_targetDirection = Vector3.Invalid;
					return;
				}

				// if near waypoint, pop it
				float destRadius = DestinationRadius * m_nodeDistance;
				double distanceToDest; Vector3D.DistanceSquared(ref m_currentPosition, ref m_destWorld, out distanceToDest);
				if (distanceToDest < destRadius * destRadius)
				{
					m_logger.debugLog("Reached waypoint: " + m_path.Peek() + ", remaining: " + (m_path.Count - 1), Logger.severity.DEBUG);
					m_path.Pop();
					FillDestWorld();
				}
			}

			m_logger.debugLog("Current position: " + m_currentPosition + ", destination: " + m_destWorld);

			FillEntitiesLists();

			m_entitiesPruneAvoid.Clear();
			Vector3 repulsion;
			CalcRepulsion(out repulsion);

			Vector3D disp; Vector3D.Subtract(ref m_destWorld, ref m_currentPosition, out disp);
			double  targetDistance = disp.Length();
			Vector3D direct; Vector3D.Divide(ref disp, targetDistance, out direct);
			Vector3 directF = direct;
			Vector3.Add(ref directF, ref repulsion, out m_targetDirection);
			m_targetDirection.Normalize();
			m_targetDistance = (float)targetDistance;

			MyEntity obstructing;
			Vector3D pointOfObstruction;
			object subtObstruct;
			if (CurrentObstructed((float)targetDistance, out obstructing, out pointOfObstruction, out subtObstruct))
			{
				ObstructingEntity = obstructing;
				//ObstructionReport = subtObstruct ?? obstructing;

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
				ObstructingEntity = null;
				//ObstructionReport = null;
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

			// TODO: this might need to be larger to accomodate moving objects, it's possible that a box would be better
			BoundingSphereD sphere = new BoundingSphereD() { Center = m_currentPosition, Radius = m_autopilotShipBoundingRadius + (m_autopilotSpeed + 1000f) * SpeedFactor };
			MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, m_entitiesPruneAvoid);

			foreach (MyEntity entity in CollectEntities(m_entitiesPruneAvoid))
				m_entitiesRepulse.Add(entity);

			Profiler.EndProfileBlock();
		}

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

		private void CalcRepulsion(out Vector3 repulsion)
		{
			Profiler.StartProfileBlock();

			if (m_clusters.Clusters.Count != 0)
				m_clusters.Clear();

			for (int index = m_entitiesRepulse.Count - 1; index >= 0; index--)
			{
				MyEntity entity = m_entitiesRepulse[index];

				m_logger.debugLog("entity is null", Logger.severity.FATAL, condition: entity == null);
				m_logger.debugLog("entity is not top-most", Logger.severity.FATAL, condition: entity.Hierarchy.Parent != null);

				Vector3D centre = entity.GetCentre();
				Vector3D toCentreD;
				Vector3D.Subtract(ref centre, ref m_currentPosition, out toCentreD);
				toCentreD.Normalize();
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
				if (linearSpeed < 0f)
					linearSpeed = 0f;
				else
					linearSpeed *= SpeedFactor;
				boundingRadius += m_autopilotShipBoundingRadius + linearSpeed;

				Vector3D toCurrent;
				Vector3D.Subtract(ref m_currentPosition, ref centre, out toCurrent);
				double toCurrLenSq = toCurrent.LengthSquared();
				if (toCurrLenSq < boundingRadius * boundingRadius)
				{
					// Entity is too close for repulsion.
					m_entitiesPruneAvoid.Add(entity);
					continue;
				}

				BoundingSphereD entitySphere = new BoundingSphereD(centre, boundingRadius);
				m_clusters.Add(ref entitySphere);
			}

			m_clusters.AddMiddleSpheres();

			repulsion = Vector3D.Zero;
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
		/// <summary>
		/// 
		/// </summary>
		/// <param name="sphere">The sphere which is repulsing the autopilot.</param>
		/// <param name="repulsion">A directional vector with length between 0 and 1, indicating the repulsive force.</param>
		private void CalcRepulsion(ref BoundingSphereD sphere, ref Vector3 repulsion)
		{
			Vector3D toCurrentD;
			Vector3D.Subtract(ref m_currentPosition, ref sphere.Center, out toCurrentD);
			Vector3 toCurrent = toCurrentD;
			float toCurrentLenSq = toCurrent.LengthSquared();

			float maxRepulseDist = (float)sphere.Radius * 4f; // Might need to be different for planets than other entities.
			float maxRepulseDistSq = maxRepulseDist * maxRepulseDist;

			//m_logger.debugLog("current position: " + m_path.CurrentPosition + ", sphere centre: " + sphere.Center + ", to current: " + toCurrent + ", sphere radius: " + sphere.Radius);

			if (toCurrentLenSq > maxRepulseDistSq)
			{
				// Autopilot is outside the maximum bounds of repulsion.
				return;
			}

			// If both entity and autopilot are near the destination, the repulsion radius must be reduced or we would never reach the destination.
			double toDestLenSq;
			Vector3D.DistanceSquared(ref sphere.Center, ref  m_destWorld, out toDestLenSq);
			toDestLenSq += m_targetDistance * m_targetDistance; // Adjustable

			if (toCurrentLenSq > toDestLenSq)
			{
				// Autopilot is outside the boundaries of repulsion.
				return;
			}

			if (toDestLenSq < maxRepulseDistSq)
				maxRepulseDist = (float)Math.Sqrt(toDestLenSq);

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
		private bool CurrentObstructed(float targetDistance, out MyEntity obstructingEntity, out Vector3D pointOfObstruction, out object obstructSubEntity)
		{
			MyCubeBlock ignoreBlock = m_ignoreEntity as MyCubeBlock;

			// if destination is obstructing it needs to be checked first, so we would match speed with destination

			MyEntity destTop;
			if (m_destination.Entity != null)
			{
				m_logger.debugLog("checking destination entity");

				destTop = (MyEntity)m_destination.Entity.GetTopMostParent();
				if (m_entitiesPruneAvoid.Contains(destTop))
				{
					if (m_tester.ObstructedBy(destTop, ignoreBlock, ref m_targetDirection, targetDistance, out obstructSubEntity, out pointOfObstruction))
					{
						m_logger.debugLog("Obstructed by " + destTop.getBestName() + "." + obstructSubEntity);
						obstructingEntity = destTop;
						return true;
					}
				}
			}
			else
				destTop = null;

			//// check voxel next so that the ship will not match an obstruction that is on a collision course

			//if (!m_ignoreVoxel)
			//{
			//	m_logger.debugLog("raycasting voxels");

			//	Vector3 velocityMulti; Vector3.Multiply(ref m_autopilotVelocity, SpeedFactor, out velocityMulti);
			//	Vector3 directionMulti; Vector3.Multiply(ref m_targetDirection, VoxelAdd, out directionMulti);
			//	Vector3 rayDirection; Vector3.Add(ref velocityMulti, ref directionMulti, out rayDirection);
			//	IHitInfo hit;
			//	if (m_tester.RayCastIntersectsVoxel(ref Vector3D.Zero, ref rayDirection, out hit))
			//	{
			//		m_logger.debugLog("Obstructed by voxel " + hit.HitEntity + " at " + hit.Position);
			//		obstructingEntity = (MyEntity)hit.HitEntity;
			//		pointOfObstruction = hit.Position;
			//		obstructSubEntity = null;
			//		return true;
			//	}
			//}

			// check remaining entities

			m_logger.debugLog("checking " + m_entitiesPruneAvoid.Count + " entites - destination entity - voxels");

			if (m_entitiesPruneAvoid.Count != 0)
				for (int i = m_entitiesPruneAvoid.Count - 1; i >= 0; i--)
				{
					m_logger.debugLog("entity: " + m_entitiesPruneAvoid[i]);
					obstructingEntity = m_entitiesPruneAvoid[i];
					if (obstructingEntity == destTop || obstructingEntity is MyVoxelBase)
						// already checked
						continue;
					if (m_tester.ObstructedBy(obstructingEntity, ignoreBlock, ref m_targetDirection, targetDistance, out obstructSubEntity, out pointOfObstruction))
					{
						m_logger.debugLog("Obstructed by " + obstructingEntity.getBestName() + "." + obstructSubEntity);
						return true;
					}
				}

			m_logger.debugLog("No obstruction");
			obstructingEntity = null;
			pointOfObstruction = Vector3.Invalid;
			obstructSubEntity = null;
			return false;
		}

		private bool ObstructionMovingAway()
		{
			Vector3D velocity = ObstructingEntity.Physics.LinearVelocity;
			if (velocity.LengthSquared() < 10f)
				return false;
			Vector3D position = ObstructingEntity.GetCentre();
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
			m_openNodes.Clear();
			m_reachedNodes.Clear();
			m_path.Clear();
			FillDestWorld();
#if PROFILE
			m_unreachableNodes = 0;
#endif
			double distCurObs, distObsDest;
			Vector3D.Distance(ref m_currentPosition, ref obstructionPoint, out distCurObs);
			Vector3D.Distance(ref obstructionPoint, ref m_destWorld, out distObsDest);
			m_nodeDistance = (float)MathHelper.Max(distCurObs * 0.25d, distObsDest * 0.1d, 1f);

			m_logger.debugLog("m_obstructingEntity == null", Logger.severity.ERROR, condition: ObstructingEntity == null);

			Vector3D relativePosition;
			Vector3D obstructPosition = ObstructingEntity.PositionComp.GetPosition();
			Vector3D.Subtract(ref m_currentPosition, ref obstructPosition, out relativePosition);

			m_logger.debugLog("current position: " + m_currentPosition + ", obs: " + obstructPosition + ", relative: " + relativePosition);

			PathNode rootNode = new PathNode() { Position = new Vector3D(Math.Round(relativePosition.X), Math.Round(relativePosition.Y), Math.Round(relativePosition.Z)) };
			m_openNodes.Insert(rootNode, 0f);
			AddReachedNode(ref rootNode);
			FindAPath();
		}

		private void AddReachedNode(ref PathNode reached)
		{
			m_reachedNodes.Add(reached.Position.GetHash(), reached);
		}

		/// <summary>
		/// Continues pathfinding.
		/// </summary>
		private void FindAPath()
		{
			if (m_openNodes.Count == 0)
			{
				m_logger.debugLog("Pathfinding failed", Logger.severity.WARNING);

#if PROFILE
				LogStats();
#endif
#if LOG_ENABLED
				LogStats();
#endif

				m_openNodes.Clear();
				m_reachedNodes.Clear();
				return;
			}

			PathNode currentNode = m_openNodes.RemoveMin();

			// check if current is reachable from parent

			if (currentNode.DistToCur != 0f)
			{
				if (m_reachedNodes.ContainsKey(currentNode.Position.GetHash()))
				{
					m_logger.debugLog("Already reached: " + ReportRelativePosition(currentNode.Position));
					FindAPath();
					return;
				}

				FillEntitiesLists();

				// TODO: ignore entities that are far away and slow as in CalcRepulsion

				if (!CanReachFromParent(ref currentNode))
				{
#if PROFILE
					m_unreachableNodes++;
#endif
					FindAPath();
					return;
				}
				m_logger.debugLog("Reached node: " + ReportRelativePosition(currentNode.Position));
				AddReachedNode(ref currentNode);

				// need to keep checking if the destination can be reached from every node
				// this doesn't indicate a clear path but allows repulsion to resume

				Vector3D obstructPosition = ObstructingEntity.PositionComp.GetPosition();
				Vector3D worldPosition; Vector3D.Add(ref obstructPosition, ref currentNode.Position, out worldPosition);
				Vector3D offset; Vector3D.Subtract(ref worldPosition, ref m_currentPosition, out offset);

				Line line = new Line(worldPosition, m_destWorld, false);
				//m_logger.debugLog("line to dest: " + line.From + ", " + line.To + ", " + line.Direction + ", " + line.Length);
				if (CanTravelSegment(ref offset, ref line))
				{
					m_logger.debugLog("Can reach final destination from a node, finished pathfinding. Final node: " + ReportRelativePosition(currentNode.Position), Logger.severity.DEBUG);
					m_path.Clear();
					PathNode node = currentNode;
					while (node.DistToCur != 0f)
					{
						m_path.Push(node.Position);
						node = m_reachedNodes[node.ParentKey];
					}

#if PROFILE
					LogStats();
#endif
#if LOG_ENABLED
					LogStats();
#endif

					m_openNodes.Clear();
					m_reachedNodes.Clear();

					m_logger.alwaysLog("Following path", Logger.severity.INFO);
					return;
				}
			}
			else
				m_logger.debugLog("first node: " + ReportRelativePosition(currentNode.Position));

			// open nodes

			long currentKey = currentNode.Position.GetHash();
			foreach (Vector3I neighbour in Globals.NeighboursOne)
				CreatePathNode(currentKey, ref currentNode, neighbour, 1f);
			foreach (Vector3I neighbour in Globals.NeighboursTwo)
				CreatePathNode(currentKey, ref currentNode, neighbour, MathHelper.Sqrt2);
			foreach (Vector3I neighbour in Globals.NeighboursThree)
				CreatePathNode(currentKey, ref currentNode, neighbour, MathHelper.Sqrt3);

			FindAPath();
		}

		private bool CanReachFromParent(ref PathNode node)
		{
			Profiler.StartProfileBlock();

			Vector3D obstructPosition = ObstructingEntity.PositionComp.GetPosition();

			PathNode parent = m_reachedNodes[node.ParentKey];
			Vector3D worldParent;
			Vector3D.Add(ref obstructPosition, ref parent.Position, out worldParent);

			Vector3D offset; Vector3D.Subtract(ref worldParent, ref m_currentPosition, out offset);

			Profiler.EndProfileBlock();
			Line line = new Line() { From = parent.Position, To = node.Position, Direction = node.DirectionFromParent, Length = node.DistToCur - parent.DistToCur };
			return CanTravelSegment(ref offset, ref line);
		}

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
			//		m_logger.debugLog("Obstructed by voxel " + hit.HitEntity + " at " + hit.Position);
			//		Profiler.EndProfileBlock();
			//		return false;
			//	}
			//}

			//m_logger.debugLog("checking " + m_entitiesRepulse.Count + " entites - voxels");

			if (m_entitiesRepulse.Count != 0)
				for (int i = m_entitiesRepulse.Count - 1; i >= 0; i--)
				{
					MyEntity entity = m_entitiesRepulse[i];
					if (entity is MyVoxelBase)
						// already checked
						continue;
					object partHit;
					Vector3D pointOfObstruction;
					if (m_tester.ObstructedBy(entity, ignoreBlock, ref offset, ref line.Direction, line.Length, out partHit, out pointOfObstruction))
					{
						m_logger.debugLog("Obstructed by " + entity.getBestName() + "." + partHit);
						Profiler.EndProfileBlock();
						return false;
					}
				}

			m_logger.debugLog("No obstruction");
			Profiler.EndProfileBlock();
			return true;
		}

		private void CreatePathNode(long parentKey, ref PathNode parent, Vector3I neighbour, float distMulti)
		{
			Profiler.StartProfileBlock();

			Vector3D position = parent.Position + neighbour * m_nodeDistance;
			position = new Vector3D(Math.Round(position.X), Math.Round(position.Y), Math.Round(position.Z));
			long positionHash = position.GetHash();

			if (m_reachedNodes.ContainsKey(positionHash))
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
			Vector3D dispToDest; Vector3D.Subtract(ref m_destWorld, ref position, out dispToDest);
			double distToDest = MinPathDistance(ref dispToDest);
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
				resultKey += m_nodeDistance * (1f - turn);
			}

			PathNode result = new PathNode()
			{
				ParentKey = parentKey,
				DistToCur = distToCur,
				Position = position,
				DirectionFromParent = direction,
			};

			m_logger.debugLog("resultKey <= 0", Logger.severity.ERROR, condition: resultKey <= 0f);
			//m_logger.debugLog("path node: " + result.ParentKey + ", " + result.DistToCur + ", " + result.Position + ", " + result.DirectionFromParent);
			m_openNodes.Insert(result, resultKey);
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

			return X * MathHelper.Sqrt3 + (Y - X) * MathHelper.Sqrt2 + Z - Y;
		}

		private string ReportRelativePosition(Vector3D position)
		{
			return position + " => " + (position + ObstructingEntity.PositionComp.GetPosition());
		}

		private void LogStats()
		{
			foreach (Vector3D position in m_path)
				m_logger.alwaysLog("Waypoint: " + ReportRelativePosition(position));
#if PROFILE
			m_logger.alwaysLog("Nodes reached: " + m_reachedNodes.Count + ", unreachable: " + m_unreachableNodes + ", open: " + m_openNodes.Count + ", path length: " + m_path.Count);
#else
			m_logger.debugLog("Nodes reached: " + m_reachedNodes.Count + ", open: " + m_openNodes.Count + ", path length: " + m_path.Count);
#endif
		}

	}
}
