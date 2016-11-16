using Sandbox.Game.Entities;
using VRageMath;

namespace Rynchodon
{
	public static	class IMyVoxelBaseExtensions
	{

		public static bool Intersects(this MyVoxelBase voxel, ref BoundingSphereD sphere)
		{
			if (!voxel.PositionComp.WorldVolume.Intersects(sphere))
				return false;

			Vector3D leftBottom = voxel.PositionLeftBottomCorner;
			Vector3D localCentre; Vector3D.Subtract(ref sphere.Center, ref leftBottom, out localCentre);
			BoundingSphereD localSphere = new BoundingSphereD() { Center = localCentre, Radius = sphere.Radius };

			return voxel.Storage.Geometry.Intersects(ref localSphere);
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

			for (double testDist = minRadius * 2d; ; testDist *= 2d)
				foreach (Vector3I neighbour in Globals.Neighbours)
				{
					Vector3D disp = Vector3D.Multiply(neighbour, testDist);
					Vector3D.Add(ref startPosition, ref disp, out testSphere.Center);
					if (!Intersects(voxel, ref testSphere))
					{
						freePosition = testSphere.Center;
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
				if (!Intersects(voxel, ref testSphere))
				{
					freePosition = testSphere.Center;
					return;
				}
			}
		}

	}
}
