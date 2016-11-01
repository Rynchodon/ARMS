using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot
{
	public struct Destination
	{

		// NOTE: Use centre of entity because it works better if the entity rotates.

		public static Destination FromWorld(IMyEntity entity, ref Vector3D worldPostion)
		{
			return new Destination(entity, worldPostion - entity.GetCentre());
		}

		public static Destination FromWorld(IMyEntity entity, Vector3D worldPostion)
		{
			return new Destination(entity, worldPostion - entity.GetCentre());
		}

		public IMyEntity Entity;
		public Vector3D Position;

		public Destination(ref Vector3D worldPosition)
		{
			Entity = null;
			Position = worldPosition;
		}

		public Destination(Vector3D worldPosition)
		{
			Entity = null;
			Position = worldPosition;
		}

		public Destination(IMyEntity entity, ref Vector3D offset)
		{
			Entity = entity;
			Position = offset;
		}

		public Destination(IMyEntity entity, Vector3D offset)
		{
			Entity = entity;
			Position = offset;
		}

		public Destination(ref IHitInfo hitInfo)
		{
			Entity = hitInfo.HitEntity;
			Position = hitInfo.Position - Entity.GetCentre();
		}

		public Vector3D WorldPosition()
		{
			if (Entity == null)
				return Position;
			return Entity.GetCentre() + Position;
		}

		public bool Equals(ref Destination other)
		{
			return this.Entity == other.Entity && Vector3D.DistanceSquared(this.Position, other.Position) < 1d;
		}

		public bool Equals(Destination other)
		{
			return this.Entity == other.Entity && Vector3D.DistanceSquared(this.Position, other.Position) < 1d;
		}

		public override string ToString()
		{
			if (Entity != null)
				return base.ToString() + ": " + Entity.getBestName() + " + " + Position;
			return base.ToString() + ": " + Position;
		}

		public void SetWorld(ref Vector3D worldPosition)
		{
			if (Entity == null)
				Position = worldPosition;
			else
				Position = worldPosition - Entity.GetCentre();
		}

		public void SetWorld(Vector3D worldPosition)
		{
			if (Entity == null)
				Position = worldPosition;
			else
				Position = worldPosition - Entity.GetCentre();
		}

	}
}
