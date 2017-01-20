using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinding
{
	/// <summary>
	/// Groups repulsion spheres together to avoid creating valleys between them.
	/// </summary>
	public class SphereClusters
	{

		/// <summary>
		/// Clustering needs to ignore speed or the middle spheres become inconsistent, so we store the value without speed. For planets distance is also a factor in the variable component.
		/// </summary>
		public struct RepulseSphere
		{
#if DEBUG
			public IMyEntity[] Entities;
#endif

			/// <summary>Position of the sphere</summary>
			public Vector3D Centre;
			/// <summary>Fixed radius of the sphere.</summary>
			public double FixedRadius;
			/// <summary>Variable component of the radius.</summary>
			public double VariableRadius;

			public double RepulseRadius { get { return FixedRadius + VariableRadius; } }

			[Conditional("DEBUG")]
			public void SetEntity(IMyEntity entity)
			{
#if DEBUG
				Entities = new IMyEntity[] { entity };
#endif
			}

			public override string ToString()
			{

#if DEBUG
				return "{Centre: " + Centre + " FixedRadius: " + PrettySI.makePretty(FixedRadius) + " VariableRadius: " + PrettySI.makePretty(VariableRadius) + " Entities: {" + string.Join(",", Entities.Select(MiscExtensions.getBestName)) + "} }";
#else
				return "{Centre: " + Centre + " FixedRadius: " + PrettySI.makePretty(FixedRadius) + " VariableRadius: " + PrettySI.makePretty(VariableRadius) + "}";
#endif
			}
		}

		public readonly List<List<RepulseSphere>> Clusters = new List<List<RepulseSphere>>();
		public readonly List<RepulseSphere> Planets = new List<RepulseSphere>();

		public void Add(ref RepulseSphere sphere)
		{
			List<RepulseSphere> joinedCluster = null;
			List<RepulseSphere> emptyCluster = null;

			for (int indexO = Clusters.Count - 1; indexO >= 0; indexO--)
			{
				List<RepulseSphere> cluster = Clusters[indexO];
				if (cluster.Count == 0)
				{
					emptyCluster = cluster;
					continue;
				}
				for (int indexI = cluster.Count - 1; indexI >= 0; indexI--)
				{
					RepulseSphere checkSphere = cluster[indexI];

					double distSq;
					Vector3D.DistanceSquared(ref sphere.Centre, ref checkSphere.Centre, out distSq);

					double radii = sphere.FixedRadius + checkSphere.FixedRadius;
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
				if (emptyCluster != null)
					emptyCluster.Add(sphere);
				else
				{
					ResourcePool.Get(out joinedCluster);
					joinedCluster.Add(sphere);
					Clusters.Add(joinedCluster);
				}
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
				List<RepulseSphere> cluster = Clusters[indexO];
				if (cluster.Count < 2)
					continue;
				Vector3D[] points = new Vector3D[cluster.Count];
				for (int indexI = cluster.Count - 1; indexI >= 0; indexI--)
					points[indexI] = cluster[indexI].Centre;

				BoundingSphereD sphere = BoundingSphereD.CreateFromPoints(points);
				RepulseSphere repulse = new RepulseSphere() { Centre = sphere.Center, FixedRadius = sphere.Radius };

#if DEBUG
				List<IMyEntity> entities; ResourcePool.Get(out entities);
				foreach (RepulseSphere rs in cluster)
					entities.AddArray(rs.Entities);
				repulse.Entities = entities.ToArray();
				entities.Clear();
				ResourcePool.Return(entities);
#endif

				cluster.Add(repulse);
			}
		}

		public void Clear()
		{
			for (int index = Clusters.Count - 1; index >= 0; index--)
			{
				List<RepulseSphere> cluster = Clusters[index];
				cluster.Clear();
				ResourcePool.Return(cluster);
			}
			Clusters.Clear();
		}

	}
}
