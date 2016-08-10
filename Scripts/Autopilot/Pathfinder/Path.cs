using System;
using System.Collections.Generic;
using Rynchodon.Utility.Collections;
using Sandbox.Game.Entities;
using VRage;
using VRage.Game.Entity;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinder
{
	public class PathThing
	{
		private struct Path
		{
			public Vector3D CurrentPosition, Destination;
			public Vector3 AutopilotVelocity;
			public double DistanceSquared;
			public float AutopilotShipBoundingRadius, AutopilotSpeed;

			public Path(ref Vector3 relativeDestination, MyCubeGrid autopilotGrid)
			{
				this.CurrentPosition = autopilotGrid.GetCentre();
				this.Destination = this.CurrentPosition + relativeDestination;
				this.AutopilotVelocity = autopilotGrid.Physics.LinearVelocity;
				this.AutopilotShipBoundingRadius = autopilotGrid.PositionComp.LocalVolume.Radius;
				this.AutopilotSpeed = this.AutopilotVelocity.Length();

				Vector3D.DistanceSquared(ref this.CurrentPosition, ref this.Destination, out this.DistanceSquared);
			}
		}

		static PathThing()
		{
			Logger.SetFileName("PathThing");
		}

		private const float SpeedFactor = 2f, RepulsionFactor = 4f;
		
		private Path m_path;
		private Vector3 m_targetDirection;

		private MyCubeGrid m_grid;
		private SphereClusters m_clusters = new SphereClusters();
		private List<MyEntity> m_entities = new List<MyEntity>(), m_allEntities = new List<MyEntity>();
		private Vector2IMatrix<bool> m_rejections = new Vector2IMatrix<bool>(), m_rejectTests = new Vector2IMatrix<bool>();

		// results
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

		/// <param name="relativeDestination">Destination - navigation block position</param>
		public void Test(MyCubeGrid myGrid, Vector3 relativeDestination, MyEntity ignoreEntity, bool isAtmospheric, bool ignoreVoxel)
		{
			m_path = new Path(ref relativeDestination, myGrid);
			m_grid = myGrid;

			// Collect entities
			m_entities.Clear();
			m_allEntities.Clear();

			Vector3D min, max;
			Vector3D.Min(ref m_path.CurrentPosition, ref m_path.Destination, out min);
			Vector3D.Max(ref m_path.CurrentPosition, ref m_path.Destination, out max);
			BoundingBoxD box = new BoundingBoxD(min, max);
			box.Inflate(m_path.AutopilotShipBoundingRadius + m_path.AutopilotSpeed * SpeedFactor);

			//Logger.DebugLog("box: " + box);

			MyGamePruningStructure.GetTopMostEntitiesInBox(ref box, m_entities);
			for (int i = m_entities.Count - 1; i >= 0; i--)
			{
				MyEntity entity = m_entities[i];
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
				else if (entity is MyVoxelMap)
				{
					if (ignoreVoxel)
						continue;
				}
				else if (!(entity is MyFloatingObject))
					continue;

				if (entity.Physics != null && entity.Physics.Mass > 0f && entity.Physics.Mass < 1000f)
					continue;

				//Logger.DebugLog("approved entity: " + entity.nameWithId());
				m_allEntities.Add(entity);
			}

			m_entities.Clear();
			Vector3 repulsion;
			CalcRepulsion(out repulsion);

			relativeDestination.Normalize();
			Vector3.Add(ref relativeDestination, ref repulsion, out m_targetDirection);
			m_targetDirection.Normalize();

			//Logger.DebugLog("Relative direction: " + relativeDestination + ", repulsion: " + repulsion + ", target direction: " + m_targetDirection);

			if (m_entities.Count != 0)
			{
				for (int i = m_entities.Count - 1; i >= 0; i--)
				{
					object obstruction;
					if (ObstructedBy(m_entities[i], out obstruction))
					{
						Logger.DebugLog("Obstructed by " + obstruction);
						m_resultDirection = Vector3.Invalid;
						m_entities.Clear();
						m_allEntities.Clear();
						return;
					}
				}
				Logger.DebugLog("No obstruction");
			}
			else
				Logger.DebugLog("No obstruction tests performed");

			m_resultDirection = m_targetDirection;
			m_entities.Clear();
			m_allEntities.Clear();
		}

		private void CalcRepulsion(out Vector3 repulsion)
		{
			if (m_clusters.Clusters.Count != 0)
				m_clusters.Clear();

			for (int index = m_allEntities.Count - 1; index >= 0; index--)
			{
				MyEntity entity = m_allEntities[index];

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
					//linearSpeed = m_path.AutopilotSpeed;
					Vector3.Dot(ref m_path.AutopilotVelocity, ref toCentre, out linearSpeed);
				}
				else
				{
					boundingRadius = entity.PositionComp.LocalVolume.Radius;
					if (entity.Physics == null)
						//linearSpeed = m_path.AutopilotSpeed;
						Vector3.Dot(ref m_path.AutopilotVelocity, ref toCentre, out linearSpeed);
					else
					{
						//linearSpeed = Vector3.Distance(entity.Physics.LinearVelocity, m_path.AutopilotVelocity);
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
					m_entities.Add(entity);
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

			float maxRepulseDist =(float) sphere.Radius * RepulsionFactor; // Might need to be different for planets than other entities.
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

		private bool ObstructedBy(MyEntity entityTopMost, out object obstructing)
		{
			Vector3 rejectionVector;
			Vector3 relativeVelocity = entityTopMost.Physics == null ? m_grid.Physics.LinearVelocity : m_grid.Physics.LinearVelocity - entityTopMost.Physics.LinearVelocity;
			Vector3 temp;
			Vector3.Add(ref relativeVelocity, ref m_targetDirection, out temp);
			Vector3.Normalize(ref temp, out rejectionVector);

			// capsule intersection test is done implicitly by the sphere adjustment for speed

			MyCubeGrid grid = entityTopMost as MyCubeGrid;
			if (grid != null)
			{
				Vector3I hitCell;
				if (RejectionIntersects(grid, ref rejectionVector, out hitCell))
				{
					Logger.DebugLog("rejection intersects");
					obstructing = (object)grid.GetCubeBlock(hitCell) ?? (object)grid;
					return true;
				}
				else
					Logger.DebugLog("rejection does not intersect");
			}

			obstructing = entityTopMost;
			return true;
		}

		private bool RejectionIntersects(MyCubeGrid oGrid, ref Vector3 rejectionVector, out Vector3I oGridCell)
		{
			Logger.DebugLog("Rejection vector is not normalized, length squared: " + rejectionVector.LengthSquared(), Logger.severity.FATAL, condition: Math.Abs(rejectionVector.LengthSquared() - 1f) > 0.001f);
			//Logger.DebugLog("Testing for rejection intersection: " + oGrid.nameWithId());

			CubeGridCache myCache = CubeGridCache.GetFor(m_grid);
			if (myCache == null)
			{
				oGridCell = Vector3I.Zero;
				return false;
			}
			CubeGridCache oCache = CubeGridCache.GetFor(oGrid);
			if (oCache == null)
			{
				oGridCell = Vector3I.Zero;
				return false;
			}

			Vector3D origin = m_path.CurrentPosition;
			Vector3 v; rejectionVector.CalculatePerpendicularVector(out v);
			Vector3 w; Vector3.Cross(ref rejectionVector, ref v, out w);
			Matrix to3D = new Matrix(v.X, v.Y, v.Z, 0f,
				w.X, w.Y, w.Z, 0f,
				rejectionVector.X, rejectionVector.Y, rejectionVector.Z, 0f,
				0f, 0f, 0f, 1f);
			Matrix to2D; Matrix.Invert(ref to3D, out to2D);

			float roundTo;
			int steps;
			if (m_grid.GridSizeEnum == oGrid.GridSizeEnum)
			{
				roundTo = m_grid.GridSize;
				steps = 1;
			}
			else
			{
				roundTo = Math.Min(m_grid.GridSize, oGrid.GridSize);
				steps = (int)Math.Ceiling(Math.Max(m_grid.GridSize, oGrid.GridSize) / roundTo) + 1;
			}

			//Logger.DebugLog("building mycache");

			m_rejections.Clear();
			MatrixD worldMatrix = m_grid.WorldMatrix;
			float gridSize = m_grid.GridSize;
			foreach (Vector3I cell in myCache.OccupiedCells())
			{
				Vector3 local = cell * gridSize;
				Vector3D world; Vector3D.Transform(ref local, ref worldMatrix, out world);
				Vector3D relative; Vector3D.Subtract(ref world, ref origin, out relative);
				Vector3 relativeF = relative;
				Vector3 rejection; Vector3.Reject(ref relativeF, ref rejectionVector, out rejection);
				Vector3 planarComponents; Vector3.Transform(ref rejection, ref to2D, out planarComponents);
				Logger.DebugLog("Math fail: " + planarComponents, Logger.severity.FATAL, condition: planarComponents.Z > 0.0001f || planarComponents.Z < -0.0001f);
				Vector2 pc2 = new Vector2(planarComponents.X, planarComponents.Y);
				m_rejections[Round(pc2, roundTo)] = true;
			}

			//Logger.DebugLog("checking other grid cells");

			m_rejectTests.Clear();
			worldMatrix = oGrid.WorldMatrix;
			gridSize = oGrid.GridSize;
			foreach (Vector3I cell in oCache.OccupiedCells())
			{
				Vector3 local = cell * gridSize;
				Vector3D world; Vector3D.Transform(ref local, ref worldMatrix, out world);
				Vector3D relative; Vector3D.Subtract(ref world, ref origin, out relative);
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
							oGridCell = cell;
							return true;
						}
			}
			oGridCell = Vector3I.Zero;
			return false;
		}

		private Vector2I Round(Vector2 vector, float value)
		{
			return new Vector2I((int)Math.Round(vector.X / value), (int)Math.Round(vector.Y / value));
		}

	}
}
