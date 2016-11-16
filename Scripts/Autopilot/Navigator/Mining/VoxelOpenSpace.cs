using System;
using System.Collections.Generic;
using Rynchodon.Threading;
using Rynchodon.Utility;
using Sandbox.Engine.Physics;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator.Mining
{
	class VoxelOpenSpace
	{
		public const double MinSpace = 8d;
		public delegate void EmptySpaceCallback(bool success, ref Vector3D position);

		private static ThreadManager Thread = new ThreadManager(1, true, typeof(VoxelOpenSpace).Name);
		private static ulong nextCheckExpire;

		public static void GetEmptySpaceInside(MyVoxelBase voxel, ref Vector3D near, EmptySpaceCallback callback)
		{
			VoxelOpenSpace obj;
			if (!Registrar.TryGetValue(voxel, out obj))
			{
				obj = new VoxelOpenSpace(voxel.EntityId);
				Registrar.Add(voxel, obj);
			}
			obj.ExpireAt = Globals.UpdateCount + 36000;



			if (Globals.UpdateCount < nextCheckExpire)
				return;
			foreach (VoxelOpenSpace script in Registrar.Scripts<VoxelOpenSpace>())
				if (Globals.UpdateCount < script.ExpireAt)
				{
					Registrar.Remove<VoxelOpenSpace>(script.EntityId);
					return;
				}
			nextCheckExpire = Globals.UpdateCount + 3600;
		}

		public readonly long EntityId;
		private readonly List<BoundingSphereD> m_spheres = new List<BoundingSphereD>();
		private ulong ExpireAt;
		private List<Vector3D>[] m_voxelHits;
		private event EmptySpaceCallback m_onComplete;

		public VoxelOpenSpace(long entityId)
		{
			this.EntityId = entityId;
		}

		private void CastRay()
		{
			Profiler.StartProfileBlock();
			List<MyPhysics.HitInfo> hits = new List<MyPhysics.HitInfo>();

			IMyEntity entity;
			if (!MyAPIGateway.Entities.TryGetEntityById(EntityId, out entity) && !(entity is MyVoxelBase))
			{
				Logger.DebugLog("Failed entity lookup: " + entity.nameWithId(), Logger.severity.ERROR);
				return;
			}
			MyVoxelBase voxel = (MyVoxelBase)entity;
			Vector3D voxelCentre = voxel.GetCentre();

			m_voxelHits = new List<Vector3D>[4];
			int index = 0;

			Vector3D outside;
			for (int i = 0; i < 4; i++)
			{
				voxel.PositionComp.WorldVolume.GetRandomPointOnSphere(out outside);
				MyPhysics.CastRay(voxelCentre, outside, hits, MyPhysics.CollisionLayers.VoxelCollisionLayer);

				List<Vector3D> voxelHits = new List<Vector3D>(hits.Count + 2);
				m_voxelHits[index++] = voxelHits;

				voxelHits.Add(voxelCentre);
				foreach (IHitInfo hit in hits)
					if (hit.HitEntity == voxel)
						voxelHits.Add(hit.Position);
				voxelHits.Add(outside);
			}

			Profiler.EndProfileBlock();
			Thread.EnqueueAction(GetSpheres);
		}

		private void GetSpheres()
		{
			const double minSpaceSq = MinSpace * MinSpace;

			IMyEntity entity;
			if (!MyAPIGateway.Entities.TryGetEntityById(EntityId, out entity) && !(entity is MyVoxelBase))
			{
				Logger.DebugLog("Failed entity lookup: " + entity.nameWithId(), Logger.severity.ERROR);
				return;
			}
			MyVoxelBase voxel = (MyVoxelBase)entity;

			foreach (List<Vector3D> hitGroup in m_voxelHits)
			{
				bool even = hitGroup.Count % 2 == 0;
				Vector3D previous = hitGroup[0];

				for (int index = 1; index < hitGroup.Count; index++)
				{
					Vector3D current = hitGroup[index];

					if (even)
					{
						Logger.DebugLog("Gap from " + previous + " to " + current);
						double distSq; Vector3D.DistanceSquared(ref current, ref previous, out distSq);
						if (distSq >= minSpaceSq)
						{
							Logger.DebugLog("Gap satisfies minimum space requirement");
							Vector3D add; Vector3D.Add(ref current, ref previous, out add);
							Vector3D centre; Vector3D.Multiply(ref add, 0.5d, out centre);

							if (!InsideSphere(ref centre))
								FindLargestSphere(voxel, ref centre);
						}
					}

					even = !even;
					previous = current;
				}
			}

			Logger.DebugLog("Sphere count: " + m_spheres.Count);
			m_voxelHits = null;
		}

		private bool InsideSphere(ref Vector3D position)
		{
			for (int index = m_spheres.Count - 1; index >= 0; index--)
			{
				BoundingSphereD sphere = m_spheres[index];
				double distSq; Vector3D.DistanceSquared(ref sphere.Center, ref position, out distSq);
				if (distSq < sphere.Radius * sphere.Radius)
					return true;
			}

			return false;
		}

		private void FindLargestSphere(MyVoxelBase voxel, ref Vector3D centre)
		{
			BoundingSphereD sphere;
			sphere.Center = centre;
			sphere.Radius = MinSpace;
			double maxRadius = 0d;

			while (maxRadius < 1024d)
			{
				if (voxel.Intersects(ref sphere))
				{
					if (maxRadius != 0d)
					{
						sphere.Radius = maxRadius;
						m_spheres.Add(sphere);
						Logger.DebugLog("Largest sphere: " + sphere);
					}
					return;
				}
				maxRadius = sphere.Radius;
				sphere.Radius = sphere.Radius * 2d;
			}

			throw new Exception("Never intersected voxel");
		}

		private void GetClosest()
		{

		}

	}
}
