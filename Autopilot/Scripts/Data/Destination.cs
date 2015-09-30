// skip file on build

using Sandbox.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Data
{

	public interface IDestination
	{
		Vector3D Point { get; }
		Vector3 Velocity { get; }
	}

	public class DestinationPoint : IDestination
	{
		private readonly Vector3D point;

		public DestinationPoint(Vector3D point)
		{ this.point = point; }

		public Vector3D Point
		{ get { return point; } }

		public Vector3 Velocity
		{ get { return Vector3.Zero; } }
	}

	public class DestinationEntity_WorldOffset : IDestination
	{
		private readonly IMyEntity Entity;
		private readonly Vector3D WorldOffset;

		public DestinationEntity_WorldOffset(IMyEntity entity, Vector3D worldOffset)
		{
			this.Entity = entity;
			this.WorldOffset = worldOffset;
		}

		public Vector3D Point
		{ get { return Entity.GetPosition() + WorldOffset; } }

		public Vector3 Velocity
		{ get { return Entity.GetLinearVelocity(); } }
	}

	public class DestinationEntity_LocalOffset : IDestination
	{
		private readonly IMyEntity Entity;
		private readonly Vector3D LocalOffset;

		public DestinationEntity_LocalOffset(IMyEntity entity, Vector3D localOffset)
		{
			this.Entity = entity;
			this.LocalOffset = localOffset;
		}

		public Vector3D Point
		{ get { return Vector3D.Transform(LocalOffset, Entity.WorldMatrix); } }

		public Vector3 Velocity
		{ get { return Entity.GetLinearVelocity(); } }
	}

}
