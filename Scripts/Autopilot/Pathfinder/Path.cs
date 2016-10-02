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
		/// <summary>
		/// Structured to keep it organized, not intended to be copied around.
		/// Vectors are not readonly so they can be passed as ref, do not set!
		/// </summary>
		private struct PathInfo
		{
			public readonly MyCubeGrid AutopilotGrid;
			public Vector3D CurrentPosition, Destination;
			public Vector3 AutopilotVelocity;
			public readonly double DistanceSquared;
			public readonly float AutopilotShipBoundingRadius, AutopilotSpeed;

			public PathInfo(ref Vector3 relativeDestination, MyCubeGrid autopilotGrid)
			{
				this.AutopilotGrid = autopilotGrid;
				this.CurrentPosition = autopilotGrid.GetCentre();
				this.Destination = this.CurrentPosition + relativeDestination;
				this.AutopilotVelocity = autopilotGrid.Physics.LinearVelocity;
				this.AutopilotShipBoundingRadius = autopilotGrid.PositionComp.LocalVolume.Radius;
				this.AutopilotSpeed = this.AutopilotVelocity.Length();

				Vector3D.DistanceSquared(ref this.CurrentPosition, ref this.Destination, out this.DistanceSquared);
			}
		}

		// TODO: need to compare struct vs class
		private struct PathNode
		{
			public float ParentKey, DistToCur;
			public Vector3D Position;
			public Vector3 DirectionFromParent;
		}

		private const float SpeedFactor = 2f, RepulsionFactor = 4f;

		private PathInfo m_path;
		private Vector3 m_targetDirection;

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

		private readonly FastResourceLock m_resultsLock = new FastResourceLock();
		private Vector3 m_resultDirection = Vector3.Invalid;

		public Vector3 ResultDirection
		{
			get
			{
				using (m_resultsLock.AcquireSharedUsing())
					return m_resultDirection;
			}
			private set
			{
				using (m_resultsLock.AcquireExclusiveUsing())
					m_resultDirection = value;
			}
		}

		// TODO: if searching for a path, maintain speed to reference entity
		/// <param name="relativeDestination">Destination - navigation block position</param>
		public void Test(MyCubeGrid myGrid, Vector3 relativeDestination, /*MyCubeGrid referenceEntity,*/ MyEntity ignoreEntity, bool isAtmospheric, bool ignoreVoxel)
		{
			m_path = new PathInfo(ref relativeDestination, myGrid);

			// Collect entities
			m_entitiesPruneAvoid.Clear();
			m_entitiesRepulse.Clear();

			Vector3D min, max;
			Vector3D.Min(ref m_path.CurrentPosition, ref m_path.Destination, out min);
			Vector3D.Max(ref m_path.CurrentPosition, ref m_path.Destination, out max);
			BoundingBoxD box = new BoundingBoxD(min, max);
			box.Inflate(m_path.AutopilotShipBoundingRadius + m_path.AutopilotSpeed * SpeedFactor);

			//Logger.DebugLog("box: " + box);

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
					if (grid == myGrid)
						continue;
					if (!grid.Save)
						continue;
					if (Attached.AttachedGrid.IsGridAttached(myGrid, grid, Attached.AttachedGrid.AttachmentKind.Physics))
						continue;
				}
				else if (entity is MyVoxelMap && ignoreVoxel)
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

			relativeDestination.Normalize();
			Vector3.Add(ref relativeDestination, ref repulsion, out m_targetDirection);
			m_targetDirection.Normalize();

			if (!ignoreVoxel)
			{
				Vector3 rayDirection; Vector3.Add(ref m_path.AutopilotVelocity, ref m_targetDirection, out rayDirection);
				Vector3D contact;
				if (RayCastIntersectsVoxel(ref rayDirection, out contact))
				{
					Logger.DebugLog("Obstructed by voxel at " + contact);
					ResultDirection = Vector3.Invalid;
					m_entitiesPruneAvoid.Clear();
					m_entitiesRepulse.Clear();
					FindAPath(ref contact);
					return;
				}
			}

			if (m_entitiesPruneAvoid.Count != 0)
			{
				//Vector3 referenceVelocity = referenceEntity == null || referenceEntity.Physics == null ? Vector3.Zero : referenceEntity.Physics.LinearVelocity;

				for (int i = m_entitiesPruneAvoid.Count - 1; i >= 0; i--)
				{
					object obstruction;
					if (ObstructedBy(m_entitiesPruneAvoid[i], ignoreEntity as MyCubeBlock, out obstruction))
					{
						Logger.DebugLog("Obstructed by " + obstruction);
						ResultDirection = Vector3.Invalid;
						m_entitiesPruneAvoid.Clear();
						m_entitiesRepulse.Clear();

						Vector3D obstructionPoint;
						MyEntity entity = obstruction as MyEntity;
						if (entity != null)
							obstructionPoint = entity.PositionComp.GetPosition();
						else
						{
							IMySlimBlock slimBlock = obstruction as IMySlimBlock;
							if (slimBlock != null)
								obstructionPoint = slimBlock.CubeGrid.GridIntegerToWorld(slimBlock.Position);
							else
								throw new Exception("Unknown obstruction: " + obstruction);
						}
						FindAPath(ref obstructionPoint);
						return;
					}
				}
				Logger.DebugLog("No obstruction");
			}
			else
				Logger.DebugLog("No obstruction tests performed");

			ResultDirection = m_targetDirection;
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
				Vector3D.Subtract(ref centre, ref m_path.CurrentPosition, out toCentreD);
				toCentreD.Normalize();
				Vector3 toCentre = toCentreD;
				float boundingRadius;
				float linearSpeed;

				MyPlanet planet = entity as MyPlanet;
				if (planet != null)
				{
					boundingRadius = planet.MaximumRadius;
					Vector3.Dot(ref m_path.AutopilotVelocity, ref toCentre, out linearSpeed);
				}
				else
				{
					boundingRadius = entity.PositionComp.LocalVolume.Radius;
					if (entity.Physics == null)
						Vector3.Dot(ref m_path.AutopilotVelocity, ref toCentre, out linearSpeed);
					else
					{
						Vector3 obVel = entity.Physics.LinearVelocity;
						Vector3 relVel;
						Vector3.Subtract(ref m_path.AutopilotVelocity, ref  obVel, out relVel);
						Vector3.Dot(ref relVel, ref toCentre, out linearSpeed);
					}
				}
				//Logger.DebugLog("For entity: " + entity.nameWithId() + ", bounding radius: " + boundingRadius + ", autopilot ship radius: " + m_path.AutopilotShipBoundingRadius + ", linear speed: " + linearSpeed + ", sphere radius: " +
				//	(boundingRadius + m_path.AutopilotShipBoundingRadius + linearSpeed * SpeedFactor));
				if (linearSpeed < 0f)
					linearSpeed = 0f;
				else
					linearSpeed *= SpeedFactor;
				boundingRadius += m_path.AutopilotShipBoundingRadius + linearSpeed;

				Vector3D toCurrent;
				Vector3D.Subtract(ref m_path.CurrentPosition, ref centre, out toCurrent);
				if (toCurrent.LengthSquared() < boundingRadius * boundingRadius)
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
			Vector3D.Subtract(ref m_path.CurrentPosition, ref sphere.Center, out toCurrentD);
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
			Vector3D.DistanceSquared(ref sphere.Center, ref  m_path.Destination, out toDestLenSq);
			toDestLenSq += m_path.DistanceSquared; // Adjustable

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

				Vector3 relativeVelocity = entityTopMost.Physics == null || entityTopMost.Physics.IsStatic ? m_path.AutopilotVelocity : m_path.AutopilotVelocity - entityTopMost.Physics.LinearVelocity;
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

			IEnumerable<CubeGridCache> myCaches = AttachedGrid.AttachedGrids(m_path.AutopilotGrid, AttachedGrid.AttachmentKind.Physics, true).Select(CubeGridCache.GetFor);

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
			if (m_path.AutopilotGrid.GridSizeEnum == oGrid.GridSizeEnum)
			{
				roundTo = m_path.AutopilotGrid.GridSize;
				steps = 1;
			}
			else
			{
				roundTo = Math.Min(m_path.AutopilotGrid.GridSize, oGrid.GridSize);
				steps = (int)Math.Ceiling(Math.Max(m_path.AutopilotGrid.GridSize, oGrid.GridSize) / roundTo) + 1;
			}

			//Logger.DebugLog("building m_rejections");

			m_rejections.Clear();
			MatrixD worldMatrix = m_path.AutopilotGrid.WorldMatrix;
			float gridSize = m_path.AutopilotGrid.GridSize;
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
					Vector3D relative; Vector3D.Subtract(ref world, ref m_path.CurrentPosition, out relative);
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
				Vector3D relative; Vector3D.Subtract(ref world, ref m_path.CurrentPosition, out relative);
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

		private bool RayCastIntersectsVoxel(ref Vector3 rayDirection, out Vector3D contact)
		{
			IEnumerable<CubeGridCache> myCaches = AttachedGrid.AttachedGrids(m_path.AutopilotGrid, AttachedGrid.AttachmentKind.Physics, true).Select(CubeGridCache.GetFor);
			Vector3 testDisp; Vector3.Multiply(ref rayDirection, 10f, out testDisp);

			Vector3 v; rayDirection.CalculatePerpendicularVector(out v);
			Vector3 w; Vector3.Cross(ref rayDirection, ref v, out w);
			Matrix to3D = new Matrix(v.X, v.Y, v.Z, 0f,
				w.X, w.Y, w.Z, 0f,
				rayDirection.X, rayDirection.Y, rayDirection.Z, 0f,
				0f, 0f, 0f, 1f);
			Matrix to2D; Matrix.Invert(ref to3D, out to2D);

			m_rejections.Clear();
			MatrixD worldMatrix = m_path.AutopilotGrid.WorldMatrix;
			float gridSize = m_path.AutopilotGrid.GridSize;
			foreach (CubeGridCache cache in myCaches)
			{
				if (cache == null)
				{
					Logger.DebugLog("Missing a cache", Logger.severity.DEBUG);
					contact = Vector3.Invalid;
					return false;
				}
				foreach (Vector3I cell in cache.OccupiedCells())
				{
					Vector3 local = cell * gridSize;
					Vector3D world; Vector3D.Transform(ref local, ref worldMatrix, out world);
					Vector3D relative; Vector3D.Subtract(ref world, ref m_path.CurrentPosition, out relative);
					Vector3 relativeF = relative;
					Vector3 rejection; Vector3.Reject(ref relativeF, ref rayDirection, out rejection);
					Vector3 planarComponents; Vector3.Transform(ref rejection, ref to2D, out planarComponents);
					Logger.DebugLog("Math fail: " + planarComponents, Logger.severity.FATAL, condition: planarComponents.Z > 0.0001f || planarComponents.Z < -0.0001f);
					Vector2 pc2 = new Vector2(planarComponents.X, planarComponents.Y);
					if (!m_rejections.Add(Round(pc2, gridSize), true))
						continue;

					IHitInfo info;
					if (MyAPIGateway.Physics.CastRay(world, world + testDisp, out info, RayCast.FilterLayerVoxel))
					{
						contact = info.Position;
						return true;
					}
				}
			}

			contact = Vector3.Invalid;
			return false;
		}

		private Vector2I Round(Vector2 vector, float value)
		{
			return new Vector2I((int)Math.Round(vector.X / value), (int)Math.Round(vector.Y / value));
		}

		private void FindAPath(ref Vector3D obstructionPoint)
		{
			m_openNodes.Clear();
			m_reachedNodes.Clear();

			double distCurObs, distObsDest;
			Vector3D.Distance(ref m_path.CurrentPosition, ref obstructionPoint, out distCurObs);
			Vector3D.Distance(ref obstructionPoint, ref m_path.Destination, out distObsDest);
			m_nodeDistance = (float)Math.Max(distCurObs * 0.25d, distObsDest * 0.1d);

			PathNode rootNode = new PathNode() { Position = m_path.CurrentPosition, DirectionFromParent = m_path.AutopilotVelocity / m_path.AutopilotSpeed };

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

			if (false)
			{
				FindAPath();
				return;
			}
			m_reachedNodes.Add(currentKey, currentNode);

			// if current is at dest, we have a path

			// if current is near dest, need to try for destination directly

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

			float distToDest = (float)Vector3D.Distance(position, m_path.Destination);
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

	}
}
