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
	class OreDepositApproachFinder
	{

		private MyVoxelBase m_voxel;
		private Vector3D m_to;
		private double m_minSpace;
		private bool m_getOutside;

		private List<Vector3D> m_voxelHits;

		public bool Complete { get; private set; }
		public bool Success { get; private set; }
		public Vector3D ApproachPoint { get; private set; }
		public Vector3D OreDeposit { get; private set; }

		public OreDepositApproachFinder(MyVoxelBase voxel, ref Vector3D oreDeposit, ref Vector3D direction, double minSpace, bool getOutside)
		{
			this.m_voxel = voxel;
			this.OreDeposit = oreDeposit;
			this.m_minSpace = minSpace;
			this.m_getOutside = getOutside;

			Vector3D disp; Vector3D.Multiply(ref direction, 2d * m_voxel.PositionComp.LocalVolume.Radius, out disp);
			Vector3D.Add(ref oreDeposit, ref disp, out m_to);

			MyAPIGateway.Utilities.InvokeOnGameThread(CastRay);
		}

		private void CastRay()
		{
			Profiler.StartProfileBlock();

			List<MyPhysics.HitInfo> hits;
			ResourcePool.Get(out hits);

			MyPhysics.CastRay(OreDeposit, m_to, hits, MyPhysics.CollisionLayers.VoxelCollisionLayer);

			ResourcePool.Get(out m_voxelHits);
			foreach (IHitInfo hit in hits)
				if (hit.HitEntity == m_voxel)
					m_voxelHits.Add(hit.Position);

			hits.Clear();
			ResourcePool.Return(hits);

			Thread.EnqueueAction(GetInside);

			Profiler.EndProfileBlock();
		}

		private void GetInside()
		{
			Profiler.StartProfileBlock();

			Logger.DebugLog("from " + OreDeposit + " to " + m_to + ", hit " + m_voxelHits.Count + " times");
			bool even = m_voxelHits.Count % 2 == 0;
			double minSpaceSq = m_minSpace * m_minSpace;
			BoundingSphereD testSphere; testSphere.Radius = m_minSpace;

			Vector3D previous = OreDeposit;
			for (int i = 0; i < m_voxelHits.Count; i++)
			{
				Vector3D hit = m_voxelHits[i];
				if (even)
				{
					Logger.DebugLog("Gap from " + previous + " to " + hit);
					double distSq; Vector3D.DistanceSquared(ref hit, ref previous, out distSq);
					if (distSq >= minSpaceSq)
					{
						Logger.DebugLog("Gap satisfies minimum space requirement");
						Vector3D add; Vector3D.Add(ref hit, ref previous, out add);
						Vector3D.Multiply(ref add, 0.5d, out testSphere.Center);
						Logger.DebugLog("Testing sphere: " + testSphere);
						if (m_voxel.Intersects(ref testSphere))
						{
							m_voxelHits.Clear();
							ResourcePool.Return(m_voxelHits);
							Logger.DebugLog("Sphere is empty");
							ApproachPoint = testSphere.Center;
							Success = true;
							Complete = true;
							Profiler.EndProfileBlock();
							return;
						}
					}
				}

				even = !even;
				previous = hit;
			}

			m_voxelHits.Clear();
			ResourcePool.Return(m_voxelHits);

			Profiler.EndProfileBlock();
			if (m_getOutside)
			{
				Logger.DebugLog("No space inside voxel, doing binary search for outside point");
				GetOutside();
			}
			else
			{
				Logger.DebugLog("No space inside voxel");
				Success = false;
				Complete = true;
			}
		}

		private void GetOutside()
		{
			Profiler.StartProfileBlock();

			CapsuleD capsule = new CapsuleD(m_to, OreDeposit, (float)m_minSpace);
			Vector3D emptySpace;
			if (!CapsuleDExtensions.Intersects(ref capsule, m_voxel, out emptySpace))
			{
				Logger.DebugLog("Failed to intersect voxel: " + capsule.String(), Logger.severity.WARNING);
				Success = false;
				Complete = true;
			}
			ApproachPoint = emptySpace;
			Success = true;
			Complete = true;

			Profiler.EndProfileBlock();
		}

	}
}
