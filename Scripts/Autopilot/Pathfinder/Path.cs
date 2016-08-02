using System;
using System.Collections.Generic;
using Rynchodon.Autopilot.Data;
using Rynchodon.Utility.Vectors;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
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
				this.CurrentPosition = autopilotGrid.PositionComp.LocalVolume.Center;
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

		private const float SpeedFactor = 10f;
		private Path m_path;
		private MyCubeGrid m_grid;
		private SphereClusters m_clusters = new SphereClusters();
		private List<MyEntity> m_entities = new List<MyEntity>(), m_allEntities = new List<MyEntity>();
		private HashSet<Vector3> m_rejections = new HashSet<Vector3>();

		/// <summary>
		/// 
		/// </summary>
		/// <param name="grid"></param>
		/// <param name="relativeDestination">Destination - navigation block position</param>
		/// <param name="ignoreEntity"></param>
		/// <param name="ignoreVoxel"></param>
		public void Test(MyCubeGrid myGrid, Vector3 relativeDestination, MyEntity ignoreEntity, bool ignoreVoxel)
		{
			m_path = new Path(ref relativeDestination, myGrid);
			m_grid = myGrid;

			// Collect entities
			m_entities.Clear();
			m_allEntities.Clear();

			BoundingBoxD box = new BoundingBoxD(m_path.CurrentPosition, m_path.Destination);
			box.Inflate(m_path.AutopilotShipBoundingRadius + m_path.AutopilotSpeed * SpeedFactor);

			MyGamePruningStructure.GetTopMostEntitiesInBox(ref box, m_entities);
			for (int i = m_entities.Count - 1; i >= 0; i--)
			{
				MyEntity entity = m_entities[i];
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

				m_allEntities.Add(entity);
			}

			m_entities.Clear();
			Vector3 repulsion;
			CalcRepulsion(out repulsion);

			if (m_entities.Count != 0)
			{
				// TODO: avoidance
			}

			// TODO: return something useful

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
				float boundingRadius;
				float linearSpeed;

				MyPlanet planet = entity as MyPlanet;
				if (planet != null)
				{
					boundingRadius = planet.MaximumRadius;
					linearSpeed = m_path.AutopilotSpeed;
				}
				else
				{
					boundingRadius = entity.PositionComp.LocalVolume.Radius;
					if (entity.Physics == null)
						linearSpeed = m_path.AutopilotSpeed;
					else
						linearSpeed = Vector3.Distance(entity.Physics.LinearVelocity, m_path.AutopilotVelocity);
				}
				boundingRadius += m_path.AutopilotShipBoundingRadius + linearSpeed * SpeedFactor;

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
		public void CalcRepulsion(ref BoundingSphereD sphere, ref Vector3 repulsion)
		{
			Vector3D toCurrentD;
			Vector3D.Subtract(ref m_path.CurrentPosition, ref sphere.Center, out toCurrentD);
			Vector3 toCurrent = toCurrentD;
			float toCurrentLenSq = toCurrent.LengthSquared();

			float maxRepulseDist = (float)(sphere.Radius + sphere.Radius); // Adjustable, might need to be different for planets than other entities.
			float maxRepulseDistSq = maxRepulseDist * maxRepulseDist;

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

			// maxRepulseDist / toCurrentLenSq can be adjusted
			Vector3 result;
			Vector3.Multiply(ref toCurrent, maxRepulseDist / toCurrentLenSq - 1f, out result);
			Vector3 result2;
			Vector3.Add(ref repulsion, ref result, out result2);
			repulsion = result2;
		}

		private bool RejectionIntersects(MyCubeGrid oGrid)
		{
			GridCellCache myCache = GridCellCache.GetCellCache(m_grid);
			if (myCache == null)
				return false;
			GridCellCache oCache = GridCellCache.GetCellCache(oGrid);
			if (oCache == null)
				return false;

			Vector3 mGridVel = m_grid.Physics != null ? m_grid.Physics.LinearVelocity : Vector3.Zero,
				oGridVel = oGrid.Physics != null ? oGrid.Physics.LinearVelocity : Vector3.Zero;

			Vector3 relVel; Vector3.Subtract(ref mGridVel, ref oGridVel, out relVel);
			float relSpeed = relVel.Normalize();

			Vector3 xVec; relVel.CalculatePerpendicularVector(out xVec);
			Vector3 yVec; Vector3.Cross(ref relVel, ref xVec, out yVec);

			Vector3D origin = m_grid.PositionComp.GetPosition();

			m_rejections.Clear();
			MatrixD worldMatrix = m_grid.WorldMatrix;
			foreach (Vector3I cell in myCache.EachCell())
			{
				Vector3 local = cell * m_grid.GridSize;
				Vector3D world; Vector3D.Transform(ref local, ref worldMatrix, out world);

				float x = Math.Round( world.X - origin.X, m_grid.GridSize);


				Vector3D relPos; Vector3D.Subtract(ref world, ref origin, out relPos);
				relPos.ApplyOperation
				m_rejections.Add(world);
			}
		}

	}
}
