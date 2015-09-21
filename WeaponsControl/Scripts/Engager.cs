using System;
using System.Collections.Generic;
using Rynchodon.Settings;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Rynchodon.Weapons
{
	/// <summary>
	/// <para>Shall be responsible for activating all the fixed weapons on a grid and providing directions to Autopilot.</para>
	/// <para>One weapon shall be the primary and shall be aimed.</para>
	/// <para>All other weapons shall fire as they bear.</para>
	/// </summary>
	public class Engager
	{
		public enum Stage : byte { Disarmed, Armed, Engaging }

		private static readonly MyObjectBuilderType[] FixedWeaponTypes = new MyObjectBuilderType[] { typeof(MyObjectBuilder_SmallGatlingGun), typeof(MyObjectBuilder_SmallMissileLauncher), typeof(MyObjectBuilder_SmallMissileLauncherReload) };
		private static readonly MyObjectBuilderType[] TurretWeaponTypes = new MyObjectBuilderType[] { typeof(MyObjectBuilder_LargeGatlingTurret), typeof(MyObjectBuilder_LargeMissileTurret), typeof(MyObjectBuilder_InteriorTurret) };
		private static readonly Random RandomOffset = new Random();

		private readonly IMyCubeGrid myGrid;
		private readonly Logger myLogger;

		private CachingList<FixedWeapon> myWeapons_Fixed;
		//private CachingList<Turret> myWeapons_Turret;
		private CachingList<WeaponTargeting> myWeapons_All;
		private FixedWeapon value_primary;
		private float value_MinWeaponRange, value_MaxWeaponRange;

		public Engager(IMyCubeGrid grid)
		{
			this.myGrid = grid;
			this.myLogger = new Logger("Engager", () => myGrid.DisplayName);
			this.CurrentStage = Stage.Disarmed;
		}

		~Engager()
		{ Disarm(); }

		public Stage CurrentStage { get; private set; }
		public float MinWeaponRange
		{
			get
			{
				if (value_MinWeaponRange < 1 || value_MinWeaponRange == float.MaxValue)
					GetWeaponRanges();
				return value_MinWeaponRange;
			}
		}
		public float MaxWeaponRange
		{
			get
			{
				if (value_MaxWeaponRange < 1)
					GetWeaponRanges();
				return value_MaxWeaponRange;
			}
		}

		/// <summary>
		/// If not armed, sets all the weapons to fire as they bear
		/// </summary>
		public void Arm()
		{
			if (CurrentStage != Stage.Disarmed)
				return;

			if (!ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bAllowWeaponControl))
			{
				myLogger.debugLog("Cannot arm, weapon control is disabled.", "Arm()", Logger.severity.WARNING);
				return;
			}

			myLogger.debugLog("Arming all weapons", "Arm()");

			myWeapons_Fixed = new CachingList<FixedWeapon>();
			//myWeapons_Turret = new CachingList<Turret>();
			myWeapons_All = new CachingList<WeaponTargeting>();
			value_MinWeaponRange = 0;
			CubeGridCache cache = CubeGridCache.GetFor(myGrid);

			foreach (MyObjectBuilderType weaponType in FixedWeaponTypes)
			{
				ReadOnlyList<IMyCubeBlock> weaponBlocks = cache.GetBlocksOfType(weaponType);
				if (weaponBlocks != null)
					foreach (IMyCubeBlock block in weaponBlocks)
					{
						FixedWeapon weapon = FixedWeapon.GetFor(block);
						if (weapon.EngagerTakeControl(this))
						{
							myLogger.debugLog("Took control of " + weapon.CubeBlock.DisplayNameText, "Arm()");
							myWeapons_Fixed.Add(weapon);
							myWeapons_All.Add(weapon);
						}
					}
			}
			foreach (MyObjectBuilderType weaponType in TurretWeaponTypes)
			{
				ReadOnlyList<IMyCubeBlock> weaponBlocks = cache.GetBlocksOfType(weaponType);
				if (weaponBlocks != null)
					foreach (IMyCubeBlock block in weaponBlocks)
					{
						Turret weapon = Turret.GetFor(block);
						if (weapon.CurrentState_FlagSet(WeaponTargeting.State.Targeting))
						{
							myLogger.debugLog("Active turret: " + weapon.CubeBlock.DisplayNameText, "Arm()");
							//myWeapons_Turret.Add(weapon);
							myWeapons_All.Add(weapon);
						}
					}
			}

			myWeapons_Fixed.ApplyAdditions();
			//myWeapons_Turret.ApplyAdditions();
			myWeapons_All.ApplyAdditions();

			//myLogger.debugLog("MinWeaponRange = " + MinWeaponRange, "Arm()");

			//if (myWeapons_Fixed.Count != 0 || myWeapons_Turret.Count != 0)
			if (myWeapons_All .Count != 0)
			{
				myLogger.debugLog("now armed", "Arm()");
				CurrentStage = Stage.Armed;
			}
		}

		public void Engage()
		{
			if (CurrentStage != Stage.Armed)
				return;

			myLogger.debugLog("now engaging", "Engage()");
			CurrentStage = Stage.Engaging;
		}

		/// <summary>
		/// If armed, stops tracking targets for all the weapons.
		/// </summary>
		public void Disarm()
		{
			if (CurrentStage == Stage.Disarmed)
				return;

			myLogger.debugLog("Disarming all weapons", "Arm()");

			foreach (FixedWeapon weapon in myWeapons_Fixed)
				weapon.EngagerReleaseControl(this);

			myWeapons_Fixed = null;
			//myWeapons_Turret = null;
			myWeapons_All = null;

			myLogger.debugLog("now disarmed", "Disarm()");
			CurrentStage = Stage.Disarmed;
		}

		/// <summary>
		/// The weapon that should be aimed.
		/// </summary>
		public FixedWeapon GetPrimaryWeapon()
		{
			if (CurrentStage == Stage.Disarmed)
			{
				myLogger.debugLog("cannot get primary while disarmed.", "GetPrimaryWeapon()");
				return null;
			}

			if (value_primary != null && !value_primary.CubeBlock.Closed && value_primary.CurrentTarget.FiringDirection.HasValue)
				return value_primary;

			value_primary = null;
			if (myWeapons_Fixed != null)
			{
				foreach (FixedWeapon weapon in myWeapons_Fixed)
					if (!weapon.CubeBlock.Closed)
					{
						if (weapon.CubeBlock.IsWorking)
						{
							value_primary = weapon;
							// it is preferable to choose a PrimaryWeapon that has a target
							if (weapon.CurrentTarget.Entity != null)
								break;
						}
					}
					else
						myWeapons_Fixed.Remove(weapon);

				myWeapons_Fixed.ApplyRemovals();
			}

			return value_primary;
		}

		/// <summary>
		/// Checks if this Engager has any weapon that it can use.
		/// </summary>
		public bool HasWeaponControl()
		{
			foreach (WeaponTargeting weapon in myWeapons_All)
				if (weapon.CurrentState_FlagSet(WeaponTargeting.State.Targeting))
					return true;
			return false;
		}

		public bool CanTarget(IMyEntity entity)
		{
			IMyCubeGrid asGrid = entity as IMyCubeGrid;
			if (asGrid != null)
				return CanTarget(asGrid);

			myLogger.debugLog("cannot target " + entity.getBestName() + ", only grids are allowed", "CanTarget()");
			return false;
		}

		public bool CanTarget(IMyCubeGrid grid)
		{
			if (CurrentStage == Stage.Disarmed)
			{
				myLogger.debugLog("cannot target while disarmed.", "CanTarget()");
				return false;
			}

			foreach (WeaponTargeting weapon in myWeapons_All)
			{
				if (weapon.CubeBlock.Closed)
				{
					myWeapons_All.Remove(weapon);
					FixedWeapon asFixed = weapon as FixedWeapon;
					if (asFixed != null)
						myWeapons_Fixed.Remove(asFixed);
				}

				if (weapon.CurrentState_NotFlag(WeaponTargeting.State.Targeting))
					continue;

				if (CanTarget(weapon, grid))
					return true;
			}

			myWeapons_All.ApplyRemovals();
			myWeapons_Fixed.ApplyRemovals();
			return false;
		}

		/// <remarks>
		/// Equation from https://www.jasondavies.com/maps/random-points/
		/// </remarks>
		public Vector3D GetRandomOffset()
		{
			double angle1 = RandomOffset.NextDouble() * Math.PI * 2 - Math.PI;
			double angle2 = Math.Acos(RandomOffset.NextDouble() * 2 - 1);

			return MinWeaponRange * new Vector3D(
				Math.Sin(angle1) * Math.Cos(angle2),
				Math.Sin(angle1) * Math.Sin(angle2),
				Math.Cos(angle1));
		}

		private void GetWeaponRanges()
		{
			value_MinWeaponRange = float.MaxValue;
			value_MaxWeaponRange = 0;

			foreach (WeaponTargeting weapon in myWeapons_All)
			{
				float TargetingRange = weapon.Options.TargetingRange;
				if (TargetingRange < 1 || !weapon.Options.CanTargetType(TargetType.AllGrid))
					continue;

				if (TargetingRange < value_MinWeaponRange)
					value_MinWeaponRange = TargetingRange;
				if (TargetingRange > value_MaxWeaponRange)
					value_MaxWeaponRange = TargetingRange;
			}
		}

		private bool CanTarget(WeaponTargeting weapon, IMyCubeGrid grid)
		{
			CubeGridCache cache = CubeGridCache.GetFor(grid);

			if (weapon.Options.CanTargetType(TargetType.Destroy) && cache.TotalByDefinition() != 0)
				return true;

			// check can target type
			if (grid.IsStatic)
			{
				if (!weapon.Options.CanTargetType(TargetType.Station))
				{
					//myLogger.debugLog(weapon.CubeBlock.DisplayNameText + " cannot target stations", "CanTarget()");
					return false;
				}
			}
			else if (grid.GridSizeEnum == MyCubeSize.Large)
			{
				if (!weapon.Options.CanTargetType(TargetType.LargeGrid))
				{
					//myLogger.debugLog(weapon.CubeBlock.DisplayNameText + " cannot target large ships", "CanTarget()");
					return false;
				}
			}
			else
				if (!weapon.Options.CanTargetType(TargetType.SmallGrid))
				{
					//myLogger.debugLog(weapon.CubeBlock.DisplayNameText + " cannot target small ships", "CanTarget()");
					return false;
				}

			// check for contains block
			foreach (string search in weapon.Options.blocksToTarget)
			{
				var allBlocks = cache.GetBlocksByDefLooseContains(search);
				foreach (var typeBlocks in allBlocks)
					foreach (var block in typeBlocks)
						if (block.IsWorking || (weapon.Options.FlagSet(TargetingFlags.Functional) && block.IsFunctional))
						{
							//myLogger.debugLog(weapon.CubeBlock.DisplayNameText + " can target " + search, "CanTarget()");
							return true;
						}
			}

			//myLogger.debugLog(weapon.CubeBlock.DisplayNameText + " cannot target, no blocks match", "CanTarget()");
			return false;
		}
	}
}
