using VRage.Game.Entity;
using VRageMath;

namespace Rynchodon.Autopilot
{
	public struct Destination
	{

		public readonly MyEntity Entity;
		public readonly Vector3D Position;

		public Destination(Vector3D worldPosition)
		{
			Entity = null;
			Position = worldPosition;
		}

		public Destination(MyEntity entity, Vector3D offset)
		{
			Entity = entity;
			Position = offset;
		}

		public Vector3D WorldPosition()
		{
			if (Entity == null)
				return Position;
			return Entity.PositionComp.GetPosition() + Position;
		}

		public bool Equals(Destination other)
		{
			return this.Entity == other.Entity && this.Position == other.Position;
		}

		public bool Equals(ref Destination other)
		{
			return this.Entity == other.Entity && this.Position == other.Position;
		}

	}
}
