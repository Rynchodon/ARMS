using System;
using Rynchodon.AntennaRelay;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Weapons
{
	public static class TargetExtensions
	{
		public static bool IsNull(this Target t)
		{
			return t == null || t.Entity == null;
		}
	}

	public abstract class Target
	{

		public abstract IMyEntity Entity { get; }
		public abstract TargetType TType { get; }
		public abstract Vector3D GetPosition();
		public abstract Vector3 GetLinearVelocity();

		/// <summary>The direction the shot shall be fired in.</summary>
		public Vector3? FiringDirection { get; set; }

		/// <summary>The point where contact will be made. For obstruction test, do not point the weapon here!</summary>
		public Vector3D? ContactPoint { get; set; }

	}

	public class NoTarget : Target
	{

		public static NoTarget Instance = new NoTarget();

		private NoTarget() { }

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
			throw new InvalidOperationException();
		}

		public override Vector3 GetLinearVelocity()
		{
			throw new InvalidOperationException();
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
		private IMyCubeBlock m_block;
		private Vector3D m_lastPostion;
		private TimeSpan m_lastPositionUpdate;
		private bool m_accel;
		private TargetType m_targetType;

		public LastSeenTarget(LastSeen seen, IMyCubeBlock block = null)
		{
			m_lastSeen = seen;
			m_block = block;
			m_lastPostion = m_lastSeen.LastKnownPosition;
			m_lastPositionUpdate = m_lastSeen.LastSeenAt;
			m_targetType = TargetingOptions.GetTargetType(seen.Entity);
		}

		public void Update(LastSeen seen, IMyCubeBlock block = null)
		{
			Logger.DebugLog("Different entity", Logger.severity.ERROR, condition: seen.Entity != m_lastSeen.Entity);
			m_lastSeen = seen;
			if (block != null)
				m_block = block;
			m_lastPostion = m_lastSeen.LastKnownPosition;
			m_lastPositionUpdate = m_lastSeen.LastSeenAt;
		}

		public LastSeen LastSeen
		{
			get { return m_lastSeen; }
		}

		public IMyCubeBlock Block
		{
			get { return m_block; }
		}

		public override IMyEntity Entity
		{
			get { return m_lastSeen.Entity; }
		}

		public override TargetType TType
		{
			get { return m_targetType; }
		}

		public override Vector3D GetPosition()
		{
			if (!m_accel && !Entity.Closed && (m_block == null || !m_block.Closed))
			{
				m_accel = Vector3.DistanceSquared(m_lastSeen.Entity.Physics.LinearVelocity, m_lastSeen.LastKnownVelocity) > 1f;
				if (!m_accel)
				{
					m_lastPostion = m_block != null ? m_block.GetPosition() : m_lastSeen.Entity.GetCentre();
					m_lastPositionUpdate = Globals.ElapsedTime;
					return m_lastPostion;
				}
			}
			return m_lastPostion + m_lastSeen.GetLinearVelocity() * (float)(Globals.ElapsedTime - m_lastPositionUpdate).TotalSeconds;
		}

		public override Vector3 GetLinearVelocity()
		{
			return m_lastSeen.LastKnownVelocity;
		}
	}

	public class SemiActiveTarget : Target
	{

		private readonly IMyEntity entity;
		private Vector3D position;
		private Vector3 linearVelocity;

		public SemiActiveTarget(IMyEntity entity)
		{
			this.entity = entity;
		}

		public void Update(IMyEntity missile)
		{
			LineSegment line = new LineSegment(entity.GetPosition(), entity.GetPosition() + entity.WorldMatrix.Forward * 1e6f);
			position = line.ClosestPoint(missile.GetPosition()) + missile.Physics.LinearVelocity;
			linearVelocity = entity.WorldMatrix.Forward * missile.Physics.LinearVelocity.Length();
		}

		public override IMyEntity Entity 
		{ get { return entity; } }

		public override TargetType TType
		{ get { return TargetType.AllGrid; } }

		public override Vector3D GetPosition()
		{
			return position;
		}

		public override Vector3 GetLinearVelocity()
		{
			return linearVelocity;
		}

	}

	public class GolisTarget : Target
	{

		private readonly IMyEntity m_entity;
		private readonly Vector3D m_position;

		public GolisTarget(IMyEntity entity, Vector3D position)
		{
			this.m_entity = entity;
			this.m_position = position;
		}

		public override IMyEntity Entity
		{
			get { return m_entity; }
		}

		public override TargetType TType
		{
			get { return TargetType.AllGrid; }
		}

		public override Vector3D GetPosition()
		{
			return m_position;
		}

		public override Vector3 GetLinearVelocity()
		{
			return Vector3.Zero;
		}

	}

}
