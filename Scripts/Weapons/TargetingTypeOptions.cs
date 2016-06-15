using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Settings;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Weapons
{
	/// <summary>
	/// <para>These are all types of targets that the weapon can shoot. A target may be in more than one category.</para>
	/// <para>Defined in the order of precedence</para>
	/// </summary>
	[Flags]
	public enum TargetType : byte
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
		Destroy = 1 << 7,

		/// <summary>Targets which can only have one guided missile targeting them.</summary>
		LimitTargeting = Missile + Meteor,
		Projectile = Missile + Meteor + Moving,
		AllGrid = LargeGrid + SmallGrid + Station,

		LargeShip = LargeGrid,
		SmallShip = SmallGrid,
		AllShip = AllGrid,

		LowestPriority = Destroy
	}

	[Flags]
	public enum TargetingFlags : byte
	{
		None = 0,
		/// <summary>Check for blocks are functional, rather than working.</summary>
		Functional = 1 << 0,
		/// <summary>Reduce the number of rays to check for obstructions.</summary>
		[Obsolete]
		Interior = 1 << 1,
		/// <summary>Causes a fixed weapon to be treated as a rotor-turret.</summary>
		Turret = 1 << 2,
		/// <summary>Turns ARMS targeting on for the turret.</summary>
		ArmsEnabled = 1 << 3,
		/// <summary>ARMS will attempt to leave the target intact instead of shooting through blocks</summary>
		Preserve = 1 << 4,
		/// <summary>Do not target blocks/grids without ownership.</summary>
		IgnoreOwnerless = 1 << 5
	}

	public class TargetingOptions
	{

		public static TargetType GetTargetType(IMyEntity entity)
		{
			IMyCubeGrid grid = entity as IMyCubeGrid;
			if (grid != null)
			{
				if (grid.IsStatic)
					return TargetType.Station;
				if (grid.GridSizeEnum == MyCubeSize.Large)
					return TargetType.LargeGrid;
				if (grid.GridSizeEnum == MyCubeSize.Small)
					return TargetType.SmallGrid;

				throw new Exception("Unknown grid size: " + grid.DisplayName);
			}
			if (entity is IMyCharacter)
				return TargetType.Character;
			if (entity is IMyMeteor)
				return TargetType.Meteor;
			if (entity.IsMissile())
				return TargetType.Missile;

			return TargetType.None;
		}

		public TargetType CanTarget = TargetType.None;

		/// <summary>Returns true if any of the specified types can be targeted.</summary>
		public bool CanTargetType(TargetType type)
		{ return (CanTarget & type) != 0; }

		public bool CanTargetType(IMyEntity entity)
		{
			return CanTargetType(GetTargetType(entity));
		}

		public BlockTypeList listOfBlocks { get; private set; }

		public string[] blocksToTarget
		{
			get { return listOfBlocks == null ? null : listOfBlocks.BlockNamesContain; }
			set
			{
				if (listOfBlocks != null && listOfBlocks.BlockNamesContain == value)
					return;

				if (value.IsNullOrEmpty())
					listOfBlocks = null;
				else
					listOfBlocks = new BlockTypeList(value);
			}
		}

		public TargetingFlags Flags = TargetingFlags.None;
		public bool FlagSet(TargetingFlags flag)
		{ return (Flags & flag) != 0; }

		/// <summary>If set, target coordinates. Overrides TargetEntityId.</summary>
		public Vector3D? TargetGolis;

		/// <summary>If set, only target a top most entity with this id. Defers to TargetGolis.</summary>
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
				TargetGolis = this.TargetGolis,
				TargetEntityId = this.TargetEntityId
			};
		}

		public void Assimilate(TargetingOptions fallback, TargetType typeFlags, TargetingFlags optFlags, float range, Vector3D? targetGolis, long? targetEntityId, string[] blocksToTarget)
		{
			this.CanTarget = typeFlags | fallback.CanTarget;
			this.Flags = optFlags | fallback.Flags;
			this.TargetingRange = Math.Max(range, fallback.TargetingRange);
			this.TargetGolis = targetGolis ?? fallback.TargetGolis;
			this.TargetEntityId = targetEntityId ?? fallback.TargetEntityId;
			this.blocksToTarget = blocksToTarget ?? fallback.blocksToTarget;
		}

		public override string ToString()
		{
			StringBuilder blocks = new StringBuilder();
			if (blocksToTarget != null)
				foreach (string block in blocksToTarget)
				{
					blocks.Append(block);
					blocks.Append(", ");
				}

			return "CanTarget = " + CanTarget.ToString() + ", Flags = " + Flags.ToString() + ", Range = " + TargetingRange + ", TargetGolis: " + TargetGolis + ", TargetEntityId = " + TargetEntityId + ", Blocks = (" + blocks + ")";
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
