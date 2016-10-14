using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Collections;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinding
{
	/// <summary>
	/// Groups repulsion spheres together to avoid creating valleys between them.
	/// </summary>
	public class SphereClusters
	{
		private static MyConcurrentPool<List<BoundingSphereD>> m_sphereLists = new MyConcurrentPool<List<BoundingSphereD>>();

		static SphereClusters()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			m_sphereLists = null;
		}

		public readonly List<List<BoundingSphereD>> Clusters = new List<List<BoundingSphereD>>();

		public void Add(ref BoundingSphereD sphere)
		{
			List<BoundingSphereD> joinedCluster = null;
			List<BoundingSphereD> emptyCluster = null;

			for (int indexO = Clusters.Count - 1; indexO >= 0; indexO--)
			{
				List<BoundingSphereD> cluster = Clusters[indexO];
				if (cluster.Count == 0)
				{
					emptyCluster = cluster;
					continue;
				}
				for (int indexI = cluster.Count - 1; indexI >= 0; indexI--)
				{
					BoundingSphereD checkSphere = cluster[indexI];

					double distSq;
					Vector3D.DistanceSquared(ref sphere.Center, ref checkSphere.Center, out distSq);

					double radii = sphere.Radius + checkSphere.Radius;
					radii *= radii;

					if (distSq <= radii)
					{
						if (joinedCluster != null)
						{
							joinedCluster.AddList(cluster);
							cluster.Clear();
						}
						else
						{
							joinedCluster = cluster;
							cluster.Add(sphere);
						}
						break;
					}
				}
			}

			if (joinedCluster == null)
			{
				joinedCluster = emptyCluster != null ? emptyCluster : m_sphereLists.Get();
				joinedCluster.Add(sphere);
				Clusters.Add(joinedCluster);
			}
		}

		/// <summary>
		/// Add the spheres that prevent autopilot from getting stuck between objects.
		/// </summary>
		/// <remarks>
		/// For every cluster, adds a sphere that includes the centre of every sphere in the cluster.
		/// </remarks>
		public void AddMiddleSpheres()
		{
			for (int indexO = Clusters.Count - 1; indexO >= 0; indexO--)
			{
				List<BoundingSphereD> cluster = Clusters[indexO];
				if (cluster.Count < 2)
					continue;
				Vector3D[] points = new Vector3D[cluster.Count];
				for (int indexI = cluster.Count - 1; indexI >= 0; indexI--)
					points[indexI] = cluster[indexI].Center;
				cluster.Add(BoundingSphereD.CreateFromPoints(points));
			}
		}

		public void Clear()
		{
			for (int index = Clusters.Count - 1; index >= 0; index--)
			{
				List<BoundingSphereD> cluster = Clusters[index];
				cluster.Clear();
				m_sphereLists.Return(cluster);
			}
			Clusters.Clear();
		}

	}
}
