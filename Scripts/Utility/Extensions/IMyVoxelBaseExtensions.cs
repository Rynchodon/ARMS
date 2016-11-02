using Sandbox.Game.Entities;
using VRageMath;

namespace Rynchodon
{
	public static	class IMyVoxelBaseExtensions
	{

		public static bool Intersects(this MyVoxelBase voxel, ref BoundingSphereD sphere)
		{
			Vector3D leftBottom = voxel.PositionLeftBottomCorner;
			Vector3D localCentre; Vector3D.Subtract(ref sphere.Center, ref leftBottom, out localCentre);
			BoundingSphereD localSphere = new BoundingSphereD() { Center = localCentre, Radius = sphere.Radius };

			return voxel.Storage.Geometry.Intersects(ref localSphere);
		}

	}
}
