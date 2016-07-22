using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
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
			public double DistanceCurrentToDestination;
			public float AutopilotShipBoundingRadius;

			public Path(ref Vector3D Destination, MyCubeGrid autopilotGrid)
			{
				this.CurrentPosition = autopilotGrid.GetCentre();
				this.Destination = Destination;
				this.AutopilotVelocity = autopilotGrid.Physics.LinearVelocity;
				this.AutopilotShipBoundingRadius = autopilotGrid.PositionComp.LocalVolume.Radius;

				Vector3D.Distance(ref this.CurrentPosition, ref this.Destination, out this.DistanceCurrentToDestination);
			}
		}

		static PathThing()
		{
			Logger.SetFileName("PathThing");
		}

		private Path m_path;
		private SphereClusters m_clusters = new SphereClusters();

		/// <summary>
		/// 
		/// </summary>
		/// <param name="allEntities">All the entities that may need to be avoided or generate repulsion.</param>
		/// <param name="avoidEntities">Output list. Entities that are too close for repulsion and should be avoided.</param>
		private void CalcRepulsion(List<MyEntity> allEntities, List<MyEntity> avoidEntities, out Vector3 repulsion)
		{
			if (m_clusters.Clusters.Count != 0)
				m_clusters.Clear();

			for (int index = allEntities.Count - 1; index >= 0; index--)
			{
				MyEntity entity = allEntities[index];

				Logger.DebugLog("entity is null", Logger.severity.FATAL, condition: entity == null);
				Logger.DebugLog("entity is not top-most", Logger.severity.FATAL, condition: entity.Hierarchy.Parent != null);

				Vector3D centre = entity.GetCentre();
				float boundingRadius;
				float linearSpeed;

				MyPlanet planet = entity as MyPlanet;
				if (planet != null)
				{
					boundingRadius = planet.MaximumRadius;
					linearSpeed = m_path.AutopilotVelocity.Length();
				}
				else
				{
					boundingRadius = entity.PositionComp.LocalVolume.Radius;
					if (entity.Physics == null)
						linearSpeed = m_path.AutopilotVelocity.Length();
					else
						linearSpeed = (entity.Physics.LinearVelocity - m_path.AutopilotVelocity).Length();
				}
				boundingRadius += m_path.AutopilotShipBoundingRadius + linearSpeed * 10f; // Adjustable

				Vector3D toCurrent;
				Vector3D.Subtract(ref m_path.CurrentPosition, ref centre, out toCurrent);
				if (toCurrent.LengthSquared() < boundingRadius * boundingRadius)
				{
					// Entity is too close for repulsion.
					avoidEntities.Add(entity);
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
			toDestLenSq += m_path.DistanceCurrentToDestination * m_path.DistanceCurrentToDestination; // Adjustable

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
	}
}
