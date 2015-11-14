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

		public bool CanTargetType(IMyEntity entity)
		{
			IMyCubeGrid grid = entity as IMyCubeGrid;
			if (grid != null)
			{
				if (grid.IsStatic)
					return CanTargetType(TargetType.Station);
				if (grid.GridSizeEnum == Sandbox.Common.ObjectBuilders.MyCubeSize.Large)
					return CanTargetType(TargetType.LargeGrid);
				if (grid.GridSizeEnum == Sandbox.Common.ObjectBuilders.MyCubeSize.Small)
					return CanTargetType(TargetType.SmallGrid);

				throw new Exception("Unknown grid size: " + grid.DisplayName);
			}
			if (entity is IMyCharacter)
				return CanTargetType(TargetType.Character);
			if (entity is IMyMeteor)
				return CanTargetType(TargetType.Meteor);
			if (entity.ToString().StartsWith("MyMissile"))
				return CanTargetType(TargetType.Missile);

			return false;
		}

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

}
