#if DEBUG
//#define TRACE
#endif

using System;
using System.Collections.Generic;
using Rynchodon.Utility;
using Sandbox.Game.Entities;
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
			//Logger.DebugLog("P0: " + capsule.P0 + ", P1: " + capsule.P1 + ", radius: " + capsule.Radius);
			if (capsuleLength < 0)
				Vector3D.Distance(ref capsule.P0, ref capsule.P1, out capsuleLength);

			double halfLength = capsuleLength * 0.5d;
			Vector3D temp; Vector3D.Add(ref capsule.P0, ref capsule.P1, out temp);
			Vector3D middle; Vector3D.Multiply(ref temp, 0.5d, out middle);

			if (capsuleLength < 1f)
			{
				hitPosition = middle;
				return true;
			}

			double radius = halfLength + capsule.Radius;
			BoundingSphereD worldSphere = new BoundingSphereD() { Center = middle, Radius = radius };
			if (capsuleLength < Math.Max(capsule.Radius, 1f) * 8f)
			{
				if (!voxel.ContainsOrIntersects(ref worldSphere))
				{
					hitPosition = Globals.Invalid;
					return false;
				}
			}
			else if (!voxel.PositionComp.WorldAABB.Intersects(ref worldSphere))
			{
				hitPosition = Globals.Invalid;
				return false;
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

		/// <summary>
		/// Estimates the proximity between a given capsule and a voxel.
		/// </summary>
		/// <param name="capsule">The capsule to test for intersection, the points must be at least one metre apart.</param>
		public static double Proximity(ref CapsuleD capsule, MyVoxelBase voxel, ref Vector3D hitPosition, double capsuleLength = -1d)
		{
			Logger.TraceLog(capsule.String());

			if (capsuleLength < 0)
				Vector3D.Distance(ref capsule.P0, ref capsule.P1, out capsuleLength);

			double halfLength = capsuleLength * 0.5d;
			Vector3D temp; Vector3D.Add(ref capsule.P0, ref capsule.P1, out temp);
			Vector3D middle; Vector3D.Multiply(ref temp, 0.5d, out middle);

			if (capsuleLength < 1d)
			{
				hitPosition = middle;
				Logger.TraceLog("capsule length < 1: " + capsuleLength);
				return capsuleLength;
			}

			double radius = halfLength + capsule.Radius;
			BoundingSphereD worldSphere = new BoundingSphereD() { Center = middle, Radius = radius };
			if (capsuleLength < Math.Max(capsule.Radius, 1f) * 8f)
			{
				if (!voxel.ContainsOrIntersects(ref worldSphere))
				{
					Logger.TraceLog("sphere does not intersect voxel: " + worldSphere + ", capsuleLength: " + capsuleLength);
					return capsuleLength;
				}
			}
			else if (!voxel.PositionComp.WorldAABB.Intersects(ref worldSphere))
			{
				Logger.TraceLog("sphere does not intersect voxel AABB: " + worldSphere + ", capsuleLength: " + capsuleLength);
				return capsuleLength;
			}

			CapsuleD halfCapsule;
			halfCapsule.P0 = capsule.P0;
			halfCapsule.P1 = middle;
			halfCapsule.Radius = capsule.Radius;

			double prox = Proximity(ref halfCapsule, voxel, ref hitPosition, halfLength);
			if (prox < 1f)
				return prox;

			halfCapsule.P0 = middle;
			halfCapsule.P1 = capsule.P1;

			return Math.Min(prox, Proximity(ref halfCapsule, voxel, ref hitPosition, halfLength));
		}


		public static bool IntersectsVoxel(ref CapsuleD capsule, out MyVoxelBase hitVoxel, out Vector3D hitPosition, bool checkPlanet, double capsuleLength = -1d)
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
				if ((voxel is MyVoxelMap || voxel is MyPlanet && checkPlanet) && Intersects(ref capsule, voxel, out hitPosition, capsuleLength))
				{
					hitVoxel = voxel;
					voxels.Clear();
					ResourcePool.Return(voxels);
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

		/// <summary>
		/// Estimates the proximity between a given capsule and any voxel.
		/// </summary>
		/// <param name="capsule">The capsule to test for intersection, the points must be at least one metre apart.</param>
		public static double ProximityToVoxel(ref CapsuleD capsule, out MyVoxelBase hitVoxel, out Vector3D hitPosition, bool checkPlanet, double capsuleLength = -1d)
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

			double closestProx = double.MaxValue;
			hitPosition = Globals.Invalid;
			foreach (MyVoxelBase voxel in voxels)
				if (voxel is MyVoxelMap || voxel is MyPlanet && checkPlanet)
				{
					if (voxel is MyPlanet)
					{
						LineSegmentD line;
						ResourcePool.Get(out line);
						line.From = capsule.P0;
						line.To = capsule.P1;

						double minDistSq = line.DistanceSquared(voxel.GetCentre());
						double maxPlanetRadius = ((MyPlanet)voxel).MaximumRadius;

						ResourcePool.Return(line);

						if (minDistSq > maxPlanetRadius * maxPlanetRadius)
						{
							Logger.DebugLog("Skip planet from: " + line.From + ", to: " + line.To + ", planet centre: " + voxel.GetCentre() + ", min dist sq to centre: " + minDistSq + ", max planet radius: " + maxPlanetRadius);
							continue;
						}
						Logger.DebugLog("Check planet from: " + line.From + ", to: " + line.To + ", planet centre: " + voxel.GetCentre() + ", min dist sq to centre: " + minDistSq + ", max planet radius: " + maxPlanetRadius);
					}

					double proximity = Proximity(ref capsule, voxel, ref hitPosition, capsuleLength);
					if (proximity < closestProx)
					{
						Logger.TraceLog("proximity is less than closest. proximity: " + proximity + ", closest: " + closestProx);
						closestProx = proximity;
					}
					if (closestProx < 1d)
					{
						Logger.TraceLog("closest under 1: " + closestProx);
						hitVoxel = voxel;
						voxels.Clear();
						ResourcePool.Return(voxels);
						Profiler.EndProfileBlock();
						return closestProx;
					}
				}

			voxels.Clear();
			ResourcePool.Return(voxels);

			hitVoxel = null;
			Profiler.EndProfileBlock();
			return closestProx;
		}

		public static string String(this CapsuleD capsule)
		{
			return "{P0:" + capsule.P0 + ", P1:" + capsule.P1 + ", Radius:" + capsule.Radius + "}";
		}

	}
}
