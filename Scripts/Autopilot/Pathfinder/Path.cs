using System;
using System.Collections.Generic;
using System.Linq;
using Rynchodon.Attached;
using Rynchodon.Utility;
using Rynchodon.Utility.Collections;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
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

		private const float SpeedFactor = 2f, RepulsionFactor = 4f;

		// Inputs: rarely change

		private MyCubeGrid m_autopilotGrid;
		private MyCubeBlock m_navBlock;
		private Destination m_destination; // never just match speed with dest, if it is obstructing it will be m_obstructingEntity

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
		/// <summary>Rejections of this grid</summary>
		private Vector2IMatrix<bool> m_rejections = new Vector2IMatrix<bool>();
		/// <summary>Rejections of the other grid.</summary>
		private Vector2IMatrix<bool> m_rejectTests = new Vector2IMatrix<bool>();

		// Low Level

		private MyBinaryStructHeap<float, PathNode> m_openNodes = new MyBinaryStructHeap<float, PathNode>();
		private Dictionary<float, PathNode> m_reachedNodes = new Dictionary<float, PathNode>();
		private float m_nodeDistance;

		// Results

		private Vector3 m_targetDirection = Vector3.Invalid;
		private MyEntity m_obstructingEntity;

		public void Setup(MyCubeBlock navBlock, ref Destination destination)
		{
			if (m_navBlock == navBlock && m_autopilotGrid == navBlock.CubeGrid && m_destination.Equals(ref destination))
				return;

			// TODO: if running, interrupt
			m_navBlock = navBlock;
			m_autopilotGrid = m_navBlock.CubeGrid;
			UpdateInputs();
			m_destination = new Destination(destination.Entity, destination.Position - m_centreToNavBlock);
		}

		private void UpdateInputs()
		{
			m_currentPosition = m_autopilotGrid.GetCentre();
			m_destWorld = m_destination.WorldPosition();
			m_autopilotVelocity = m_autopilotGrid.Physics.LinearVelocity;
			m_centreToNavBlock = m_currentPosition - m_navBlock.PositionComp.GetPosition();
			m_distSqToDest = (float)Vector3D.DistanceSquared(m_currentPosition, m_destWorld);
			m_autopilotSpeed = m_autopilotVelocity.Length();
			m_autopilotShipBoundingRadius = m_autopilotGrid.PositionComp.LocalVolume.Radius;
		}

		public void Test(MyEntity ignoreEntity, bool ignoreVoxel)
		{
			UpdateInputs();

			// Collect entities
			m_entitiesPruneAvoid.Clear();
			m_entitiesRepulse.Clear();

			BoundingBoxD box = BoundingBoxD.CreateInvalid();
			box.Include(ref m_currentPosition);
			box.Include(m_destWorld);
			box.Inflate(m_autopilotShipBoundingRadius + m_autopilotSpeed * SpeedFactor);

			MyGamePruningStructure.GetTopMostEntitiesInBox(ref box, m_entitiesPruneAvoid);
			for (int i = m_entitiesPruneAvoid.Count - 1; i >= 0; i--)
			{
				MyEntity entity = m_entitiesPruneAvoid[i];
				//Logger.DebugLog("checking entity: " + entity.nameWithId());
				if (ignoreEntity != null && ignoreEntity == entity)
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
				else if (entity is MyVoxelBase && ignoreVoxel)
					continue;
				else if (!(entity is MyFloatingObject))
					continue;

				if (entity.Physics != null && entity.Physics.Mass > 0f && entity.Physics.Mass < 1000f)
					continue;

				//Logger.DebugLog("approved entity: " + entity.nameWithId());
				m_entitiesRepulse.Add(entity);
			}

			m_entitiesPruneAvoid.Clear();
			Vector3 repulsion;
			CalcRepulsion(out repulsion);

			Vector3D disp; Vector3D.Subtract(ref m_destWorld, ref m_currentPosition, out disp);
			Vector3D direct; Vector3D.Divide(ref disp, Math.Sqrt(m_distSqToDest), out direct);
			Vector3 directF = direct;
			Vector3.Add(ref directF, ref repulsion, out m_targetDirection);
			m_targetDirection.Normalize();

			// if destination is obstructing it needs to be checked first, so we would match speed with destination

			MyCubeBlock ignoreBlock = ignoreEntity as MyCubeBlock;
			
			if (m_destination.Entity != null && m_entitiesPruneAvoid.Contains(m_destination.Entity))
			{
				object obstruction;
				if (ObstructedBy(m_destination.Entity, ignoreBlock, out obstruction))
				{
					HandleObstruction(obstruction);
					return;
				}
			}

			// check voxel next so that the ship will not match an obstruction that is on a collision course

			if (!ignoreVoxel)
			{
				Vector3 rayDirection; Vector3.Add(ref m_autopilotVelocity, ref m_targetDirection, out rayDirection);
				IHitInfo hit;
				if (RayCastIntersectsVoxel(ref rayDirection, out hit))
				{
					Logger.DebugLog("Obstructed by voxel at " + hit);
					m_targetDirection = Vector3.Invalid;
					m_obstructingEntity = (MyEntity)hit.HitEntity;
					m_entitiesPruneAvoid.Clear();
					m_entitiesRepulse.Clear();
					FindAPath(hit.Position);
					return;
				}
			}

			// check remaining entities

			if (m_entitiesPruneAvoid.Count != 0)
			{
				for (int i = m_entitiesPruneAvoid.Count - 1; i >= 0; i--)
				{
					MyEntity entity = m_entitiesPruneAvoid[i];
					if (entity == m_destination.Entity || entity is MyVoxelBase)
						// already checked
						continue;
					object obstruction;
					if (ObstructedBy(entity, ignoreBlock, out obstruction))
					{
						HandleObstruction(obstruction);
						return;
					}
				}
				Logger.DebugLog("No obstruction");
			}

			m_obstructingEntity = null;
			m_entitiesPruneAvoid.Clear();
			m_entitiesRepulse.Clear();
		}

		private void CalcRepulsion(out Vector3 repulsion)
		{
			if (m_clusters.Clusters.Count != 0)
				m_clusters.Clear();

			for (int index = m_entitiesRepulse.Count - 1; index >= 0; index--)
			{
				MyEntity entity = m_entitiesRepulse[index];

				Logger.DebugLog("entity is null", Logger.severity.FATAL, condition: entity == null);
				Logger.DebugLog("entity is not top-most", Logger.severity.FATAL, condition: entity.Hierarchy.Parent != null);

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
				//Logger.DebugLog("For entity: " + entity.nameWithId() + ", bounding radius: " + boundingRadius + ", autopilot ship radius: " + m_autopilotShipBoundingRadius + ", linear speed: " + linearSpeed + ", sphere radius: " +
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

			//Logger.DebugLog("current position: " + m_path.CurrentPosition + ", sphere centre: " + sphere.Center + ", to current: " + toCurrent + ", sphere radius: " + sphere.Radius);

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
			//Logger.DebugLog("toCurrent: " + toCurrent + ", maxRepulseDist: " + maxRepulseDist + ", toCurrentLen: " + toCurrentLen + ", ratio: " + (maxRepulseDist / toCurrentLen) + ", result: " + result);
			Vector3 result2;
			Vector3.Add(ref repulsion, ref result, out result2);
			//Logger.DebugLog("repulsion: " + result2);
			repulsion = result2;
		}

		private bool ObstructedBy(MyEntity entityTopMost, MyCubeBlock ignoreBlock, out object obstructing)
		{
			// capsule intersection test is done implicitly by the sphere adjustment for speed

			MyCubeGrid grid = entityTopMost as MyCubeGrid;
			if (grid != null)
			{
				Profiler.StartProfileBlock("RejectionIntersects");

				Vector3 relativeVelocity; GetRelativeVelocity(entityTopMost, out relativeVelocity);
				Vector3 rejectionVector;
				Vector3.Add(ref relativeVelocity, ref m_targetDirection, out rejectionVector);
				rejectionVector.Normalize();

				Vector3I hitCell;
				if (RejectionIntersects(grid, ignoreBlock, ref rejectionVector, out hitCell))
				{
					Logger.DebugLog("rejection intersects");
					obstructing = (object)grid.GetCubeBlock(hitCell) ?? (object)grid;
					Profiler.EndProfileBlock();
					return true;
				}
				else
					Logger.DebugLog("rejection does not intersect");
				Profiler.EndProfileBlock();
			}

			obstructing = entityTopMost;
			return true;
		}

		private bool RejectionIntersects(MyCubeGrid oGrid, MyCubeBlock ignoreBlock, ref Vector3 rejectionVector, out Vector3I oGridCell)
		{
			Logger.DebugLog("Rejection vector is not normalized, length squared: " + rejectionVector.LengthSquared(), Logger.severity.FATAL, condition: Math.Abs(rejectionVector.LengthSquared() - 1f) > 0.001f);
			//Logger.DebugLog("Testing for rejection intersection: " + oGrid.nameWithId());

			IEnumerable<CubeGridCache> myCaches = AttachedGrid.AttachedGrids(m_autopilotGrid, AttachedGrid.AttachmentKind.Physics, true).Select(CubeGridCache.GetFor);

			CubeGridCache oCache = CubeGridCache.GetFor(oGrid);
			if (oCache == null)
			{
				Logger.DebugLog("Failed to get cache for other grid", Logger.severity.DEBUG);
				oGridCell = Vector3I.Zero;
				return false;
			}

			bool checkBlock = ignoreBlock != null && oGrid == ignoreBlock.CubeGrid;

			Vector3 v; rejectionVector.CalculatePerpendicularVector(out v);
			Vector3 w; Vector3.Cross(ref rejectionVector, ref v, out w);
			Matrix to3D = new Matrix(v.X, v.Y, v.Z, 0f,
				w.X, w.Y, w.Z, 0f,
				rejectionVector.X, rejectionVector.Y, rejectionVector.Z, 0f,
				0f, 0f, 0f, 1f);
			Matrix to2D; Matrix.Invert(ref to3D, out to2D);

			float roundTo;
			int steps;
			if (m_autopilotGrid.GridSizeEnum == oGrid.GridSizeEnum)
			{
				roundTo = m_autopilotGrid.GridSize;
				steps = 1;
			}
			else
			{
				roundTo = Math.Min(m_autopilotGrid.GridSize, oGrid.GridSize);
				steps = (int)Math.Ceiling(Math.Max(m_autopilotGrid.GridSize, oGrid.GridSize) / roundTo) + 1;
			}

			//Logger.DebugLog("building m_rejections");

			m_rejections.Clear();
			MatrixD worldMatrix = m_autopilotGrid.WorldMatrix;
			float gridSize = m_autopilotGrid.GridSize;
			foreach (CubeGridCache cache in myCaches)
			{
				if (cache == null)
				{
					Logger.DebugLog("Missing a cache", Logger.severity.DEBUG);
					oGridCell = Vector3I.Zero;
					return false;
				}
				foreach (Vector3I cell in cache.OccupiedCells())
				{
					Vector3 local = cell * gridSize;
					Vector3D world; Vector3D.Transform(ref local, ref worldMatrix, out world);
					Vector3D relative; Vector3D.Subtract(ref world, ref m_currentPosition, out relative);
					Vector3 relativeF = relative;
					Vector3 rejection; Vector3.Reject(ref relativeF, ref rejectionVector, out rejection);
					Vector3 planarComponents; Vector3.Transform(ref rejection, ref to2D, out planarComponents);
					Logger.DebugLog("Math fail: " + planarComponents, Logger.severity.FATAL, condition: planarComponents.Z > 0.0001f || planarComponents.Z < -0.0001f);
					Vector2 pc2 = new Vector2(planarComponents.X, planarComponents.Y);
					m_rejections[Round(pc2, roundTo)] = true;
				}
			}

			//Logger.DebugLog("checking other grid cells");

			m_rejectTests.Clear();
			worldMatrix = oGrid.WorldMatrix;
			gridSize = oGrid.GridSize;
			foreach (Vector3I cell in oCache.OccupiedCells())
			{
				Vector3 local = cell * gridSize;
				Vector3D world; Vector3D.Transform(ref local, ref worldMatrix, out world);
				Vector3D relative; Vector3D.Subtract(ref world, ref m_currentPosition, out relative);
				Vector3 relativeF = relative;
				Vector3 rejection; Vector3.Reject(ref relativeF, ref rejectionVector, out rejection);
				Vector3 planarComponents; Vector3.Transform(ref rejection, ref to2D, out planarComponents);
				Logger.DebugLog("Math fail: " + planarComponents, Logger.severity.FATAL, condition: planarComponents.Z > 0.0001f || planarComponents.Z < -0.0001f);
				Vector2 pc2 = new Vector2(planarComponents.X, planarComponents.Y);
				Vector2I rounded = Round(pc2, roundTo);

				if (!m_rejectTests.Add(rounded, true))
					continue;

				//Logger.DebugLog("testing range. x: " + rounded.X + " - " + pc2.X);

				Vector2I test;
				for (test.X = rounded.X - steps; test.X <= pc2.X + steps; test.X++)
					for (test.Y = rounded.Y - steps; test.Y <= pc2.Y + steps; test.Y++)
						if (m_rejections.Contains(test))
						{
							if (checkBlock)
							{
								IMySlimBlock slim = oGrid.GetCubeBlock(cell);
								if (slim.FatBlock == ignoreBlock)
									continue;
							}
							oGridCell = cell;
							return true;
						}
			}
			oGridCell = Vector3I.Zero;
			return false;
		}

		private void HandleObstruction(object obstruction)
		{
			Logger.DebugLog("Obstructed by " + obstruction);

			m_targetDirection = Vector3.Invalid;
			m_obstructingEntity = obstruction as MyEntity;

			Vector3D obstructionPoint;
			if (m_obstructingEntity != null)
				obstructionPoint = m_obstructingEntity.PositionComp.GetPosition();
			else
			{
				IMySlimBlock slimBlock = obstruction as IMySlimBlock;
				if (slimBlock != null)
				{
					m_obstructingEntity = (MyEntity)slimBlock.CubeGrid;
					obstructionPoint = slimBlock.CubeGrid.GridIntegerToWorld(slimBlock.Position);
				}
				else
					throw new Exception("Unknown obstruction: " + obstruction);
			}

			m_entitiesPruneAvoid.Clear();
			m_entitiesRepulse.Clear();
			FindAPath(obstructionPoint);
		}

		private bool RayCastIntersectsVoxel(ref Vector3 rayDirection, out IHitInfo hit)
		{
			IEnumerable<CubeGridCache> myCaches = AttachedGrid.AttachedGrids(m_autopilotGrid, AttachedGrid.AttachmentKind.Physics, true).Select(CubeGridCache.GetFor);
			Vector3 testDisp; Vector3.Multiply(ref rayDirection, 10f, out testDisp);

			Vector3 v; rayDirection.CalculatePerpendicularVector(out v);
			Vector3 w; Vector3.Cross(ref rayDirection, ref v, out w);
			Matrix to3D = new Matrix(v.X, v.Y, v.Z, 0f,
				w.X, w.Y, w.Z, 0f,
				rayDirection.X, rayDirection.Y, rayDirection.Z, 0f,
				0f, 0f, 0f, 1f);
			Matrix to2D; Matrix.Invert(ref to3D, out to2D);

			m_rejections.Clear();
			MatrixD worldMatrix = m_autopilotGrid.WorldMatrix;
			float gridSize = m_autopilotGrid.GridSize;
			foreach (CubeGridCache cache in myCaches)
			{
				if (cache == null)
				{
					Logger.DebugLog("Missing a cache", Logger.severity.DEBUG);
					hit = default(IHitInfo);
					return false;
				}
				foreach (Vector3I cell in cache.OccupiedCells())
				{
					Vector3 local = cell * gridSize;
					Vector3D world; Vector3D.Transform(ref local, ref worldMatrix, out world);
					Vector3D relative; Vector3D.Subtract(ref world, ref m_currentPosition, out relative);
					Vector3 relativeF = relative;
					Vector3 rejection; Vector3.Reject(ref relativeF, ref rayDirection, out rejection);
					Vector3 planarComponents; Vector3.Transform(ref rejection, ref to2D, out planarComponents);
					Logger.DebugLog("Math fail: " + planarComponents, Logger.severity.FATAL, condition: planarComponents.Z > 0.0001f || planarComponents.Z < -0.0001f);
					Vector2 pc2 = new Vector2(planarComponents.X, planarComponents.Y);
					if (!m_rejections.Add(Round(pc2, gridSize), true))
						continue;

					if (MyAPIGateway.Physics.CastRay(world, world + testDisp, out hit, RayCast.FilterLayerVoxel))
						return true;
				}
			}

			hit = default(IHitInfo);
			return false;
		}

		private void FindAPath(Vector3D obstructionPoint)
		{
			m_openNodes.Clear();
			m_reachedNodes.Clear();

			double distCurObs, distObsDest;
			Vector3D.Distance(ref m_currentPosition, ref obstructionPoint, out distCurObs);
			Vector3D.Distance(ref obstructionPoint, ref m_destWorld, out distObsDest);
			m_nodeDistance = (float)MathHelper.Max(distCurObs * 0.25d, distObsDest * 0.1d, 10f);

			Vector3D relativePosition;
			Vector3 relativeVelocity;
			if (m_destination.Entity != null)
			{
				Vector3D destEntityPosition = m_destination.Entity.PositionComp.GetPosition();
				Vector3D.Subtract(ref m_currentPosition, ref destEntityPosition, out relativePosition);
				GetRelativeVelocity(m_destination.Entity, out relativeVelocity);
			}
			else
			{
				relativePosition = m_currentPosition;
				relativeVelocity = m_autopilotVelocity;
			}
			relativeVelocity.Normalize();

			PathNode rootNode = new PathNode() { Position = m_currentPosition, DirectionFromParent = relativeVelocity };
			m_openNodes.Insert(rootNode, 0f);
			FindAPath();
		}

		private void FindAPath()
		{
			if (m_openNodes.Count == 0)
			{
				Logger.AlwaysLog("Pathfinding failed", Logger.severity.INFO);
				return;
			}

			float currentKey = m_openNodes.MinKey();
			PathNode currentNode = m_openNodes.RemoveMin();

			// check if current is reachable from parent
			// remember node position is not real position!

			if (false)
			{
				FindAPath();
				return;
			}
			m_reachedNodes.Add(currentKey, currentNode);

			// if current is at dest, we have a path

			// if current is near dest, need to try for destination directly

			// need to keep checking if the destination can be reached from every node
			// this doesn't indicate a clear path but allows repulsion to resume

			// open nodes

			foreach (Vector3I neighbour in Globals.NeighboursOne)
				CreatePathNode(currentKey, ref currentNode, neighbour, 1f);
			foreach (Vector3I neighbour in Globals.NeighboursTwo)
				CreatePathNode(currentKey, ref currentNode, neighbour, MathHelper.Sqrt2);
			foreach (Vector3I neighbour in Globals.NeighboursThree)
				CreatePathNode(currentKey, ref currentNode, neighbour, MathHelper.Sqrt3);

			FindAPath();
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
				Logger.DebugLog("Pathfinder node backtracks to parent", Logger.severity.FATAL, condition: turn < -0.9f);
				resultKey += m_nodeDistance * (1f - turn);
			}

			Logger.DebugLog("resultKey < 0", Logger.severity.ERROR, condition: resultKey < 0f);
			m_openNodes.Insert(result, resultKey);
		}

		private void GetRelativeVelocity(MyEntity targetEntity, out Vector3 relativeVelocity)
		{
			relativeVelocity = targetEntity.Physics == null || targetEntity.Physics.IsStatic ? m_autopilotVelocity : m_autopilotVelocity - targetEntity.Physics.LinearVelocity;
		}

		private Vector2I Round(Vector2 vector, float value)
		{
			return new Vector2I((int)Math.Round(vector.X / value), (int)Math.Round(vector.Y / value));
		}

		private void ToWorldPosition(ref Vector3D position)
		{
			if (m_destination.Entity != null)
				position += m_destination.Entity.PositionComp.GetPosition();
		}

	}
}
