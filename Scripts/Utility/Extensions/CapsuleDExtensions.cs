using System.Collections.Generic;
using Rynchodon.Utility;
using Sandbox.Game.Entities;
using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon
{
	public static class CapsuleDExtensions
	{

		/// <summary>
		/// Performs a binary search for intersection using spheres.
		/// </summary>
		/// <param name="hitPosition">a point on the capsule's line close to obstruction</param>
		public static bool Intersects(ref CapsuleD capsule, MyVoxelBase voxel, out Vector3D hitPosition, double capsuleLength = -1d)
		{
			if (capsuleLength < 0)
				Vector3D.Distance(ref capsule.P0, ref capsule.P1, out capsuleLength);
			double halfLength = capsuleLength * 0.5d;
			Vector3D temp; Vector3D.Add(ref capsule.P0, ref capsule.P1, out temp);
			Vector3D middle; Vector3D.Multiply(ref temp, 0.5d, out middle);

			double radius = halfLength + capsule.Radius;
			BoundingSphereD worldSphere = new BoundingSphereD() { Center = middle, Radius = radius };
			if (!voxel.PositionComp.WorldVolume.Intersects(worldSphere))
			{
				hitPosition = Vector3.Invalid;
				return false;
			}

			Vector3D leftBottom = voxel.PositionLeftBottomCorner;
			Vector3D localMiddle; Vector3D.Subtract(ref middle, ref leftBottom, out localMiddle);
			BoundingSphereD localSphere = new BoundingSphereD() { Center = localMiddle, Radius = radius };

			if (!voxel.Storage.Geometry.Intersects(ref localSphere))
			{
				hitPosition = Vector3.Invalid;
				return false;
			}

			if (capsuleLength < 1f)
			{
				hitPosition = middle;
				return true;
			}

			CapsuleD halfCapsule;
			halfCapsule.P0 = capsule.P0;
			halfCapsule.P1 = middle;
			halfCapsule.Radius = capsule.Radius;

			if (Intersects(ref halfCapsule, voxel, out hitPosition, halfLength))
				return true;

			halfCapsule.P0 = middle;
			halfCapsule.P1 = capsule.P1;

			return Intersects(ref halfCapsule, voxel, out hitPosition, halfLength);
		}

		public static bool IntersectsVoxel(ref CapsuleD capsule, out MyVoxelBase hitVoxel, out Vector3D hitPosition, double capsuleLength = -1d)
		{
			Profiler.StartProfileBlock();
			if (capsuleLength < 0)
				Vector3D.Distance(ref capsule.P0, ref capsule.P1, out capsuleLength);
			double halfLength = capsuleLength * 0.5d;
			Vector3D temp; Vector3D.Add(ref capsule.P0, ref capsule.P1, out temp);
			Vector3D middle; Vector3D.Multiply(ref temp, 0.5d, out middle);

			double radius = halfLength + capsule.Radius;
			BoundingSphereD worldSphere = new BoundingSphereD() { Center = middle, Radius = radius };

			List<MyVoxelBase> voxels = ResourcePool<List<MyVoxelBase>>.Get();
			MyGamePruningStructure.GetAllVoxelMapsInSphere(ref worldSphere, voxels);

			foreach (MyVoxelBase voxel in voxels)
				if ((voxel is MyVoxelMap || voxel is MyPlanet) && Intersects(ref capsule, voxel, out hitPosition, capsuleLength))
				{
					hitVoxel = voxel;
					Profiler.EndProfileBlock();
					return true;
				}

			voxels.Clear();
			ResourcePool.Return(voxels);

			hitVoxel = null;
			hitPosition = Vector3.Invalid;
			Profiler.EndProfileBlock();
			return false;
		}

	}
}
