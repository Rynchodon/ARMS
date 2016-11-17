using Sandbox.Game.Entities;
using Sandbox.Engine.Voxels;
using VRageMath;
using VRage.Voxels;

namespace Rynchodon
{
	public static	class IMyVoxelBaseExtensions
	{

		public static bool ContainsOrIntersects(this MyVoxelBase voxel, ref BoundingSphereD worldSphere, bool checkContains = false)
		{
			if (!voxel.PositionComp.WorldAABB.Intersects(ref worldSphere))
				return false;

			Vector3D leftBottom = voxel.PositionLeftBottomCorner;
			BoundingSphereD localSphere;
			Vector3D.Subtract(ref worldSphere.Center, ref leftBottom, out localSphere.Center);
			localSphere.Radius = worldSphere.Radius;

			BoundingBox localBox = BoundingBox.CreateFromSphere(localSphere);
			if (voxel.Storage.Intersect(ref localBox) == ContainmentType.Disjoint)
				return false;

			return voxel.Storage.Geometry.Intersects(ref localSphere) || checkContains && HasContentAt(voxel, ref localSphere.Center);
		}

		private static bool HasContentAt(this MyVoxelBase voxel, ref Vector3D localPosition)
		{
			Vector3I voxelCoord;
			MyVoxelCoordSystems.LocalPositionToVoxelCoord(ref localPosition, out voxelCoord);
			MyStorageData cache = new MyStorageData();
			cache.Resize(Vector3I.One);
			voxel.Storage.ReadRange(cache, MyStorageDataTypeFlags.Content, 0, ref voxelCoord, ref voxelCoord);
			return cache.Content(0) > MyVoxelConstants.VOXEL_ISO_LEVEL;
		}

		/// <summary>
		/// Find a sphere free from voxel.
		/// </summary>
		/// <param name="voxel">The voxel to avoid.</param>
		/// <param name="startPosition">The position to start the search.</param>
		/// <param name="minRadius">The minimum radius around freePosition.</param>
		/// <param name="freePosition">A position that is minRadius from voxel</param>
		public static void FindFreeSpace(this MyVoxelBase voxel, Vector3D startPosition, double minRadius, out Vector3D freePosition)
		{
			MyPlanet planet = voxel as MyPlanet;
			if (planet != null)
			{
				Vector3D centre = planet.GetCentre();
				Vector3D direction; Vector3D.Subtract(ref startPosition, ref centre, out direction);
				direction.Normalize();

				FindFreeSpace(voxel, startPosition, direction, minRadius, out freePosition);
				return;
			}

			BoundingSphereD testSphere;
			testSphere.Radius = minRadius;

			for (double testDist = minRadius; ; testDist += minRadius)
				foreach (Vector3I neighbour in Globals.Neighbours)
				{
					Vector3D disp = Vector3D.Multiply(neighbour, testDist);
					Vector3D.Add(ref startPosition, ref disp, out testSphere.Center);
					if (!ContainsOrIntersects(voxel, ref testSphere, true))
					{
						CapsuleD capsule;
						capsule.P0 = testSphere.Center;
						Vector3D disp1 = Vector3D.Multiply(neighbour, testDist * 0.5d);
						Vector3D.Add(ref startPosition, ref disp1, out capsule.P1);
						capsule.Radius = (float)minRadius;

						if (!CapsuleDExtensions.Intersects(ref capsule, voxel, out freePosition))
							freePosition = capsule.P1;
						return;
					}
				}
		}

		/// <summary>
		/// Find a sphere free from voxel.
		/// </summary>
		/// <param name="voxel">The voxel to avoid.</param>
		/// <param name="startPosition">The position to start the search.</param>
		/// <param name="direction">The direction to search in.</param>
		/// <param name="minRadius">The minimum radius around freePosition.</param>
		/// <param name="freePosition">A position that is minRadius from voxel</param>
		public static void FindFreeSpace(this MyVoxelBase voxel, Vector3D startPosition, Vector3D direction, double minRadius, out Vector3D freePosition)
		{
			BoundingSphereD testSphere;
			testSphere.Radius = minRadius;

			for (double testDist = minRadius * 2d; ; testDist *= 2d)
			{
				Vector3D disp; Vector3D.Multiply(ref direction, testDist, out disp);
				Vector3D.Add(ref startPosition, ref disp, out testSphere.Center);
				if (!ContainsOrIntersects(voxel, ref testSphere, true))
				{
					freePosition = testSphere.Center;
					return;
				}
			}
		}

	}
}
