#define PROFILE

using System;
using System.Collections.Generic;
using Rynchodon.Autopilot.Data;
using Sandbox.Game.Entities;
using VRage;
using VRage.Collections;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinder
{
	public class NewPathfinder
	{
		private struct PathNode
		{
			public float ParentKey, DistToCur;
			public Vector3D Position;
			public Vector3 DirectionFromParent;
		}

		private const float SpeedFactor = 2f, RepulsionFactor = 4f, VoxelFactor = 10f;

		///// <summary>
		///// Shared will be held while setting interupt or running. Exclusive will be held while starting a run or checking for an interrupt.
		///// </summary>
		//private FastResourceLock m_runningLock;
		//private bool m_runInterrupt;

		private readonly Logger m_logger;

		// Inputs: rarely change

		private PathTester m_tester = new PathTester();
		private MyCubeGrid m_autopilotGrid { get { return m_tester.AutopilotGrid; } set { m_tester.AutopilotGrid = value; } }
		private PseudoBlock m_navBlock;
		private Destination m_destination; // only match speed with entity if there is no obstruction
		private MyEntity m_ignoreEntity;
		private bool m_ignoreVoxel;

		// Inputs: always updated

		private Vector3D m_currentPosition, m_destWorld;
		private Vector3 m_autopilotVelocity;
		private Vector3 m_centreToNavBlock; // changes due to rotation
		private float m_distSqToDest, m_autopilotSpeed, m_autopilotShipBoundingRadius;

		// High Level

		private SphereClusters m_clusters = new SphereClusters();
		/// <summary>First this list is populated by pruning structure, when calculating repulsion it is populated with entities to be avoided.</summary>
		private List<MyEntity> m_entitiesPruneAvoid = new List<MyEntity>();
		/// <summary>List of entities that will repulse the autopilot.</summary>
		private List<MyEntity> m_entitiesRepulse = new List<MyEntity>();

		// Low Level

		private MyBinaryStructHeap<float, PathNode> m_openNodes = new MyBinaryStructHeap<float, PathNode>();
		private Dictionary<float, PathNode> m_reachedNodes = new Dictionary<float, PathNode>();
		private float m_nodeDistance;
		private Stack<Vector3D> m_path = new Stack<Vector3D>();
#if PROFILE
		private int m_nodesRejected = 0;
#endif
		// Results

		public Vector3 m_targetDirection = Vector3.Invalid; // temp public, for testing
		private MyEntity m_obstructingEntity;
		/// <summary>Obstruction to give to players, it may be a block, hence, object.</summary>
		private object m_obstructionReport;
		// TODO: need to give mover a distance, since we may be moving away from the final destination

		public NewPathfinder()
		{
			m_logger = new Logger(() => m_autopilotGrid.DisplayNameText, () => m_navBlock.DisplayName);
		}

		// Methods

		public void Run(PseudoBlock navBlock, ref Destination destination, MyEntity ignoreEntity, bool ignoreVoxel)
		{
			if (m_navBlock == navBlock && m_autopilotGrid == navBlock.Grid && m_destination.Equals(ref destination) && m_ignoreEntity == ignoreEntity && m_ignoreVoxel == ignoreVoxel)
			{
				UpdateInputs();
				TestCurrentPath();
				return;
			}
			m_logger.debugLog("something has changed");

			m_navBlock = navBlock;
			m_autopilotGrid = (MyCubeGrid)m_navBlock.Grid;
			UpdateInputs();
			m_destination = new Destination(destination.Entity, destination.Position - m_centreToNavBlock);
			m_ignoreEntity = ignoreEntity;
			m_ignoreVoxel = ignoreVoxel;
			TestCurrentPath();
		}

		private void UpdateInputs()
		{
			m_currentPosition = m_autopilotGrid.GetCentre();
			m_destWorld = m_destination.WorldPosition();
			m_autopilotVelocity = m_autopilotGrid.Physics.LinearVelocity;
			m_centreToNavBlock = m_currentPosition - m_navBlock.WorldPosition;
			m_distSqToDest = (float)Vector3D.DistanceSquared(m_currentPosition, m_destWorld);
			m_autopilotSpeed = m_autopilotVelocity.Length();
			m_autopilotShipBoundingRadius = m_autopilotGrid.PositionComp.LocalVolume.Radius;
		}

		private void TestCurrentPath()
		{
			m_entitiesPruneAvoid.Clear();
			m_entitiesRepulse.Clear();

			BoundingBoxD box = BoundingBoxD.CreateInvalid();
			box.Include(ref m_currentPosition);
			box.Include(ref m_destWorld);
			box.Inflate(m_autopilotShipBoundingRadius + m_autopilotSpeed * SpeedFactor);

			MyGamePruningStructure.GetTopMostEntitiesInBox(ref box, m_entitiesPruneAvoid);

			FillRepulseList();

			m_entitiesPruneAvoid.Clear();
			Vector3 repulsion;
			CalcRepulsion(out repulsion);

			Vector3D disp; Vector3D.Subtract(ref m_destWorld, ref m_currentPosition, out disp);
			Vector3D direct; Vector3D.Divide(ref disp, Math.Sqrt(m_distSqToDest), out direct);
			Vector3 directF = direct;
			Vector3.Add(ref directF, ref repulsion, out m_targetDirection);
			m_targetDirection.Normalize();

			Vector3D pointOfObstruction;
			if (CurrentObstructed(out m_obstructingEntity, out pointOfObstruction))
			{
				m_entitiesPruneAvoid.Clear();
				m_entitiesRepulse.Clear();
				m_targetDirection = Vector3.Invalid;
				FindAPath(ref pointOfObstruction);
				return;
			}
				
			m_obstructingEntity = null;
			m_entitiesPruneAvoid.Clear();
			m_entitiesRepulse.Clear();
		}

		/// <summary>
		/// Fill m_entitiesRepulse from m_entitiesPruneAvoid, ignoring some entitites.
		/// </summary>
		private void FillRepulseList()
		{
			for (int i = m_entitiesPruneAvoid.Count - 1; i >= 0; i--)
			{
				MyEntity entity = m_entitiesPruneAvoid[i];
				//m_logger.debugLog("checking entity: " + entity.nameWithId());
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

				//m_logger.debugLog("approved entity: " + entity.nameWithId());
				m_entitiesRepulse.Add(entity);
			}
		}

		private void CalcRepulsion(out Vector3 repulsion)
		{
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

			float maxRepulseDist = (float)sphere.Radius * RepulsionFactor; // Might need to be different for planets than other entities.
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
			toDestLenSq += m_distSqToDest; // Adjustable

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
			//m_logger.debugLog("toCurrent: " + toCurrent + ", maxRepulseDist: " + maxRepulseDist + ", toCurrentLen: " + toCurrentLen + ", ratio: " + (maxRepulseDist / toCurrentLen) + ", result: " + result);
			Vector3 result2;
			Vector3.Add(ref repulsion, ref result, out result2);
			//m_logger.debugLog("repulsion: " + result2);
			repulsion = result2;
		}

		/// <summary>
		/// For testing if the current path is obstructed by any entity in m_entitiesPruneAvoid.
		/// </summary>
		private bool CurrentObstructed(out MyEntity obstructingEntity, out Vector3D pointOfObstruction)
		{
			MyCubeBlock ignoreBlock = m_ignoreEntity as MyCubeBlock;

			// if destination is obstructing it needs to be checked first, so we would match speed with destination

			MyEntity destTop;
			if (m_destination.Entity != null)
			{
				destTop = m_destination.Entity.GetTopMostParent();
				if (m_entitiesPruneAvoid.Contains(destTop))
				{
					object partHit;
					if (m_tester.ObstructedBy(destTop, ignoreBlock, ref m_targetDirection, out partHit, out pointOfObstruction))
					{
						m_logger.debugLog("Obstructed by " + destTop.getBestName() + "." + partHit);
						obstructingEntity = destTop;
						return true;
					}
				}
			}
			else
				destTop = null;

			// check voxel next so that the ship will not match an obstruction that is on a collision course

			if (!m_ignoreVoxel)
			{
				Vector3 rayDirection; Vector3.Add(ref m_autopilotVelocity, ref m_targetDirection, out rayDirection);
				Vector3 rayMultied; Vector3.Multiply(ref rayDirection, VoxelFactor, out rayMultied);
				IHitInfo hit;
				if (m_tester.RayCastIntersectsVoxel(ref Vector3D.Zero, ref rayMultied, out hit))
				{
					m_logger.debugLog("Obstructed by voxel at " + hit.Position);
					obstructingEntity = (MyEntity)hit.HitEntity;
					pointOfObstruction = hit.Position;
					return true;
				}
			}

			// check remaining entities

			if (m_entitiesPruneAvoid.Count != 0)
				for (int i = m_entitiesPruneAvoid.Count - 1; i >= 0; i--)
				{
					obstructingEntity = m_entitiesPruneAvoid[i];
					if (obstructingEntity == destTop || obstructingEntity is MyVoxelBase)
						// already checked
						continue;
					object partHit;
					if (m_tester.ObstructedBy(obstructingEntity, ignoreBlock, ref m_targetDirection, out partHit, out pointOfObstruction))
					{
						m_logger.debugLog("Obstructed by " + obstructingEntity.getBestName() + "." + partHit);
						return true;
					}
				}

			m_logger.debugLog("No obstruction");
			obstructingEntity = null;
			pointOfObstruction = Vector3.Invalid;
			return false;
		}

		/// <summary>
		/// Starts the pathfinding.
		/// </summary>
		private void FindAPath(ref Vector3D obstructionPoint)
		{
			m_openNodes.Clear();
			m_reachedNodes.Clear();
#if PROFILE
			m_nodesRejected = 0;
#endif
			double distCurObs, distObsDest;
			Vector3D.Distance(ref m_currentPosition, ref obstructionPoint, out distCurObs);
			Vector3D.Distance(ref obstructionPoint, ref m_destWorld, out distObsDest);
			m_nodeDistance = (float)MathHelper.Max(distCurObs * 0.25d, distObsDest * 0.1d, 10f);

			m_logger.debugLog("m_obstructingEntity == null", Logger.severity.ERROR, condition: m_obstructingEntity == null);

			Vector3D relativePosition;
			Vector3D obstructPosition = m_obstructingEntity.PositionComp.GetPosition();
			Vector3D.Subtract(ref m_currentPosition, ref obstructPosition, out relativePosition);

			//Vector3 relativeVelocity;
			//if (m_obstructingEntity.Physics != null && !m_obstructingEntity.Physics.IsStatic)
			//{
			//	Vector3 obstructVelocity = m_obstructingEntity.Physics.LinearVelocity;
			//	Vector3.Subtract(ref m_autopilotVelocity, ref obstructVelocity, out relativeVelocity);
			//}
			//else
			//	relativeVelocity = m_autopilotVelocity;
			//relativeVelocity.Normalize();

			PathNode rootNode = new PathNode() { Position = relativePosition };
			m_openNodes.Insert(rootNode, 0f);
			FindAPath();
		}

		/// <summary>
		/// Continues pathfinding.
		/// </summary>
		private void FindAPath()
		{
			if (m_openNodes.Count == 0)
			{
				m_logger.alwaysLog("Pathfinding failed", Logger.severity.INFO);
				return;
			}

			float currentKey = m_openNodes.MinKey();
			PathNode currentNode = m_openNodes.RemoveMin();

			// check if current is reachable from parent

			if (!CanReachFromParent(ref currentNode))
			{
				FindAPath();
#if PROFILE
				m_nodesRejected++;
#endif
				return;
			}
			m_logger.debugLog("Reached node: " + currentNode.Position);
			m_reachedNodes.Add(currentKey, currentNode);

			// need to keep checking if the destination can be reached from every node
			// this doesn't indicate a clear path but allows repulsion to resume

			Vector3D obstructPosition = m_obstructingEntity.PositionComp.GetPosition();
			Vector3D worldPosition; Vector3D.Add(ref obstructPosition, ref currentNode.Position, out worldPosition);
			Vector3D offset; Vector3D.Subtract(ref worldPosition, ref m_currentPosition, out offset);
			Vector3D finalDest = m_destination.WorldPosition();
			Vector3D toFinalDest; Vector3D.Subtract(ref finalDest, ref worldPosition, out toFinalDest);
			Vector3 disp = toFinalDest;

			if (CanTravelSegment(ref offset, ref disp))
			{
				m_logger.debugLog("Can reach final destination from a node, finished pathfinding. Final node: " + currentNode.Position, Logger.severity.DEBUG);
				m_path.Clear();
				PathNode node = currentNode;
				while (node.ParentKey != 0f)
				{
					m_path.Push(node.Position);
					node = m_reachedNodes[node.ParentKey];
				}

				LogStats();

				// TODO: follow the path
				return;
			}

			// open nodes

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
			m_entitiesPruneAvoid.Clear();
			m_entitiesRepulse.Clear();

			// only checking entities near autopilot so the ship can get back to repulsion once it is clear

			BoundingSphereD sphere = new BoundingSphereD() { Center = m_currentPosition, Radius = m_autopilotShipBoundingRadius + m_autopilotSpeed * SpeedFactor };
			MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, m_entitiesPruneAvoid);

			FillRepulseList();

			Vector3D obstructPosition = m_obstructingEntity.PositionComp.GetPosition();

			PathNode parent = m_reachedNodes[node.ParentKey];
			Vector3D worldParent;
			Vector3D.Add(ref obstructPosition, ref parent.Position, out worldParent);

			Vector3D offset; Vector3D.Subtract(ref worldParent, ref m_currentPosition, out offset);
			Vector3D parentToNode; Vector3D.Subtract(ref node.Position, ref parent.Position, out parentToNode);
			Vector3 parentToNodeF = parentToNode;

			return CanTravelSegment(ref offset, ref parentToNodeF, node.DirectionFromParent);
		}

		private bool CanTravelSegment(ref Vector3D offset, ref Vector3 disp, Vector3 direction = default(Vector3))
		{
			if (direction == default(Vector3D))
				Vector3.Normalize(ref disp, out direction);

			MyCubeBlock ignoreBlock = m_ignoreEntity as MyCubeBlock;

			if (!m_ignoreVoxel)
			{
				Vector3 adjustment; Vector3.Multiply(ref direction, VoxelFactor, out adjustment);
				Vector3 rayTest; Vector3.Add(ref disp, ref adjustment, out rayTest);
				IHitInfo hit;
				if (m_tester.RayCastIntersectsVoxel(ref offset, ref rayTest, out hit))
				{
					m_logger.debugLog("Obstructed by voxel at " + hit);
					return false;
				}
			}

			if (m_entitiesRepulse.Count != 0)
				for (int i = m_entitiesRepulse.Count - 1; i >= 0; i--)
				{
					MyEntity entity = m_entitiesRepulse[i];
					if (entity is MyVoxelBase)
						// already checked
						continue;
					object partHit;
					Vector3D pointOfObstruction;
					if (m_tester.ObstructedBy(entity, ignoreBlock, ref offset, ref disp, out partHit, out pointOfObstruction))
					{
						m_logger.debugLog("Obstructed by " + entity.getBestName() + "." + partHit);
						return false;
					}
				}

			m_logger.debugLog("No obstruction");
			return true;
		}

		private void CreatePathNode(float parentKey, ref PathNode parent, Vector3I neighbour, float distMulti)
		{
			Vector3D position = parent.Position + neighbour * m_nodeDistance;
			position = new Vector3D(Math.Round(position.X), Math.Round(position.Y), Math.Round(position.Z));

			float distToDest = (float)Vector3D.Distance(position, m_destWorld);
			Vector3 direction = (position - parent.Position) / (float)distToDest;

			PathNode result = new PathNode()
			{
				ParentKey = parentKey,
				DistToCur = parent.DistToCur + m_nodeDistance * distMulti,
				Position = position,
				DirectionFromParent = direction,
			};

			float resultKey = result.DistToCur + distToDest;

			if (m_reachedNodes.ContainsKey(resultKey))
				return;

			float turn; Vector3.Dot(ref parent.DirectionFromParent, ref direction, out turn);
			if (turn > 0.99f && parent.ParentKey != 0f)
				result.ParentKey = parent.ParentKey;
			else
			{
				m_logger.debugLog("Pathfinder node backtracks to parent", Logger.severity.FATAL, condition: turn < -0.9f);
				resultKey += m_nodeDistance * (1f - turn);
			}

			m_logger.debugLog("resultKey < 0", Logger.severity.ERROR, condition: resultKey < 0f);
			m_openNodes.Insert(result, resultKey);
		}

		[System.Diagnostics.Conditional("PROFILE")]
		private void LogStats()
		{
			foreach (Vector3D position in m_path)
				m_logger.profileLog("Waypoint: " + position + " => " + position + m_obstructingEntity.PositionComp.GetPosition());
#if PROFILE
			m_logger.profileLog("Nodes reached: " + m_reachedNodes.Count + ", rejected: " + m_nodesRejected + ", open: " + m_openNodes.Count + ", path length: " + m_path.Count);
#endif
		}

	}
}
