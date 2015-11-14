using System;
using Rynchodon.AntennaRelay;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Weapons
{
	public abstract class Target
	{
		private Vector3? value_firingDirection;
		private Vector3? value_interceptionPoint;

		public abstract IMyEntity Entity { get; }
		public abstract TargetType TType { get; }
		public abstract Vector3D GetPosition();
		public abstract Vector3 GetLinearVelocity();

		public Vector3? FiringDirection
		{
			get { return value_firingDirection; }
			set { value_firingDirection = value; }
		}

		public Vector3? InterceptionPoint
		{
			get { return value_interceptionPoint; }
			set { value_interceptionPoint = value; }
		}
	}

	public class NoTarget : Target
	{

		public override IMyEntity Entity
		{
			get { return null; }
		}

		public override TargetType TType
		{
			get { return TargetType.None; }
		}

		public override Vector3D GetPosition()
		{
			VRage.Exceptions.ThrowIf<NotImplementedException>(true);
			throw new Exception();
		}

		public override Vector3 GetLinearVelocity()
		{
			{
				VRage.Exceptions.ThrowIf<NotImplementedException>(true);
				throw new Exception();
			}

		}
	}

	public class TurretTarget : Target
	{
		private readonly IMyEntity value_entity;
		private readonly TargetType value_tType;

		public TurretTarget(IMyEntity target, TargetType tType)
		{
			value_entity = target;
			value_tType = tType;
		}

		public override IMyEntity Entity
		{
			get { return value_entity; }
		}

		public override TargetType TType
		{
			get { return value_tType; }
		}

		public override Vector3D GetPosition()
		{
			return value_entity.GetCentre();
		}

		public override Vector3 GetLinearVelocity()
		{
			return value_entity.GetLinearVelocity();
		}
	}

	public class LastSeenTarget : Target
	{
		private LastSeen m_lastSeen;
		private Vector3D m_lastPostion;
		private DateTime m_lastPositionUpdate;
		private bool m_accel;

		public LastSeenTarget(LastSeen seen)
		{
			m_lastSeen = seen;
		}

		public override IMyEntity Entity
		{
			get { return m_lastSeen.Entity; }
		}

		public override TargetType TType
		{
			get { return TargetType.AllGrid; }
		}

		public override Vector3D GetPosition()
		{
			if (!m_accel && !Entity.Closed)
			{
				m_accel = m_lastSeen.Entity.Physics.GetLinearAcceleration().LengthSquared() > 0.01f;
				if (!m_accel)
				{
					m_lastPostion = m_lastSeen.Entity.GetCentre();
					m_lastPositionUpdate = DateTime.UtcNow;
					return m_lastPostion;
				}
			}
			return m_lastPostion + m_lastSeen.GetLinearVelocity() * (float)(DateTime.UtcNow - m_lastPositionUpdate).TotalSeconds;
		}

		public override Vector3 GetLinearVelocity()
		{
			return m_lastSeen.LastKnownVelocity;
		}
	}
}
