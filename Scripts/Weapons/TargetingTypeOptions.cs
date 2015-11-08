using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.AntennaRelay;
using Rynchodon.Settings;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Weapons
{
	/// <summary>
	/// <para>These are all types of targets that the weapon can shoot. A target may be in more than one category.</para>
	/// <para>Defined in the order of precedence</para>
	/// </summary>
	[Flags]
	public enum TargetType : ushort
	{
		None = 0,
		Missile = 1 << 0,
		Meteor = 1 << 1,
		Character = 1 << 2,
		/// <summary>Will track floating object and large and small grids</summary>
		Moving = 1 << 3,
		LargeGrid = 1 << 4,
		SmallGrid = 1 << 5,
		Station = 1 << 6,
		/// <summary>Destroy every terminal block on all grids</summary>
		Destroy = 1 << 8,

		Projectile = Missile + Meteor + Moving,
		AllGrid = LargeGrid + SmallGrid + Station
	}

	[Flags]
	public enum TargetingFlags : byte
	{
		None = 0,
		/// <summary>Check for blocks are functional, rather than working.</summary>
		Functional = 1 << 0,
		/// <summary>Reduce the number of rays to check for obstructions.</summary>
		Interior = 1 << 1,
		/// <summary>Causes a fixed weapon to be treated as a rotor-turret.</summary>
		Turret = 1 << 2
	}

	public class TargetingOptions
	{
		public TargetType CanTarget = TargetType.None;

		/// <summary>Returns true if any of the specified types can be targeted.</summary>
		public bool CanTargetType(TargetType type)
		{ return (CanTarget & type) != 0; }

		public List<string> blocksToTarget = new List<string>();

		public TargetingFlags Flags = TargetingFlags.None;
		public bool FlagSet(TargetingFlags flag)
		{ return (Flags & flag) != 0; }

		/// <summary>If set, only target a top most entity with this id.</summary>
		public long? TargetEntityId;

		public TargetingOptions() { }

		public TargetingOptions Clone()
		{
			return new TargetingOptions()
			{
				blocksToTarget = this.blocksToTarget,
				CanTarget = this.CanTarget,
				Flags = this.Flags,
				TargetingRange = this.TargetingRange,
				TargetEntityId = this.TargetEntityId
			};
		}

		public override string ToString()
		{
			StringBuilder blocks = new StringBuilder();
			foreach (string block in blocksToTarget)
			{
				blocks.Append(block);
				blocks.Append(", ");
			}

			return "CanTarget = " + CanTarget.ToString() + ", Flags = " + Flags.ToString() + ", Range = " + TargetingRange + ", TargetEntityId = " + TargetEntityId + ", Blocks = (" + blocks + ")";
		}

		private static readonly float GlobalMaxRange = ServerSettings.GetSetting<float>(ServerSettings.SettingName.fMaxWeaponRange);
		private float value_TargetingRange;
		/// <summary>
		/// <para>The range for targeting objects</para>
		/// <para>set will be tested against fMaxWeaponRange but does not test against ammunition range</para>
		/// </summary>
		public float TargetingRange
		{
			get { return value_TargetingRange; }
			set
			{
				if (value <= GlobalMaxRange)
					value_TargetingRange = value;
				else
					value_TargetingRange = GlobalMaxRange;
				TargetingRangeSquared = value_TargetingRange * value_TargetingRange;
			}
		}
		public float TargetingRangeSquared { get; private set; }
	}

	public class Target
	{
		static Target()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
		}

		public readonly IMyEntity Entity;
		public readonly TargetType TType;
		public Vector3? FiringDirection;
		public Vector3? InterceptionPoint;

		private readonly LastSeen LastSeen;

		/// <summary>
		/// Creates a target of type None with Entity as null.
		/// </summary>
		public Target()
		{
			this.Entity = null;
			this.TType = TargetType.None;
		}

		public Target(IMyEntity entity, TargetType tType, Vector3? firingDirection = null, Vector3? interceptionPoint = null)
		{
			this.Entity = entity;
			this.TType = tType;
			this.FiringDirection = firingDirection;
			this.InterceptionPoint = interceptionPoint;
		}

		/// <summary>
		/// Creates a target of type AllGrid with last seen values.
		/// </summary>
		public Target(LastSeen target)
		{
			this.Entity = target.Entity;
			this.TType = TargetType.AllGrid;
			this.LastSeen = target;
		}

		public Vector3D GetPosition()
		{
			if (LastSeen != null)
				return LastSeen.predictPosition();

			if (Entity is IMyCharacter)
				// GetPosition() is near feet
				return Entity.WorldMatrix.Up * 1.25 + Entity.GetPosition();

			return Entity.GetCentre();
		}

		public Vector3 GetLinearVelocity()
		{
			if (LastSeen != null)
				return LastSeen.LastKnownVelocity;
			return Entity.GetLinearVelocity();
		}

	}
}
