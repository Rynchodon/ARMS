﻿using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Definitions;
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
		/// <summary>Destroy every terminal block on grids</summary>
		Destroy = 1 << 8,

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
		public bool CanTargetType(TargetType type)
		{ return (CanTarget & type) != 0; }

		public List<string> blocksToTarget = new List<string>();

		public TargetingFlags Flags = TargetingFlags.None;
		public bool FlagSet(TargetingFlags flag)
		{ return (Flags & flag) != 0; }

		public TargetingOptions() { }

		public TargetingOptions Clone()
		{
			return new TargetingOptions()
			{
				blocksToTarget = this.blocksToTarget,
				CanTarget = this.CanTarget,
				Flags = this.Flags,
				TargetingRange = this.TargetingRange
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

			return "CanTarget = " + CanTarget.ToString() + ", Flags = " + Flags.ToString() + ", Range = " + TargetingRange + ", Blocks = (" + blocks + ")";
		}

		private static readonly float GlobalMaxRange = Settings.GetSetting<float>(Settings.SettingName.fMaxWeaponRange);
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
		public readonly IMyEntity Entity;
		public readonly TargetType TType;
		public readonly Vector3? FiringDirection;
		public readonly Vector3? InterceptionPoint;

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

		public Target AddDirectionPoint(Vector3? firingDirection, Vector3? interceptionPoint)
		{ return new Target(Entity, TType, firingDirection, interceptionPoint); }
	}

	public class Ammo
	{
		public readonly MyAmmoDefinition Definition;
		public readonly float TimeToMaxSpeed;
		public readonly float DistanceToMaxSpeed;

		public Ammo(MyAmmoDefinition Definiton)
		{
			this.Definition = Definiton;

			MyMissileAmmoDefinition asMissile = Definition as MyMissileAmmoDefinition;
			if (asMissile != null && !asMissile.MissileSkipAcceleration)
			{
				this.TimeToMaxSpeed = (asMissile.DesiredSpeed - asMissile.MissileInitialSpeed) / asMissile.MissileAcceleration;
				this.DistanceToMaxSpeed = (asMissile.DesiredSpeed + asMissile.MissileInitialSpeed) / 2 * TimeToMaxSpeed;
			}
			else
			{
				this.TimeToMaxSpeed = 0;
				this.DistanceToMaxSpeed = 0;
			}
		}
	}
}
