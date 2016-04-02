using System.Collections.Generic;
using System.Text;
using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Rynchodon.Settings;
using Rynchodon.Weapons;
using Sandbox.Common.ObjectBuilders;
using VRage.Collections;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator
{
	/// <summary>
	/// Uses weapons to attack enemy ship
	/// </summary>
	public class Fighter : NavigatorMover, IEnemyResponse
	{

		private static readonly MyObjectBuilderType[] FixedWeaponTypes = new MyObjectBuilderType[] { typeof(MyObjectBuilder_SmallGatlingGun), typeof(MyObjectBuilder_SmallMissileLauncher), typeof(MyObjectBuilder_SmallMissileLauncherReload) };
		private static readonly MyObjectBuilderType[] TurretWeaponTypes = new MyObjectBuilderType[] { typeof(MyObjectBuilder_LargeGatlingTurret), typeof(MyObjectBuilder_LargeMissileTurret), typeof(MyObjectBuilder_InteriorTurret) };
		private static readonly TargetType[] CumulativeTypes = new TargetType[] { TargetType.SmallGrid, TargetType.LargeGrid, TargetType.Station };

		private readonly Logger m_logger;

		private readonly CachingList<FixedWeapon> m_weapons_fixed = new CachingList<FixedWeapon>();
		private readonly CachingList<WeaponTargeting> m_weapons_all = new CachingList<WeaponTargeting>();
		private readonly Dictionary<TargetType, BlockTypeList> m_cumulative_targeting = new Dictionary<TargetType, BlockTypeList>();
		private WeaponTargeting m_weapon_primary;
		private PseudoBlock m_weapon_primary_pseudo;
		private LastSeen m_currentTarget;
		private Orbiter m_orbiter;
		private float m_weaponRange_min;
		private bool m_weaponArmed = false;
		private bool m_destroySet = false;
		private bool m_weaponDataDirty = true;

		public Fighter(Mover mover, AllNavigationSettings navSet)
			: base(mover, navSet)
		{
			this.m_logger = new Logger(GetType().Name, () => m_controlBlock.CubeGrid.DisplayName);
			Arm();
		}

		~Fighter()
		{ Disarm(); }

		public bool CanRespond()
		{
			if (!m_weaponArmed)
				return false;

			GetPrimaryWeapon();
			if (m_weapon_primary == null)
				return false;

			if (m_weaponDataDirty)
				UpdateWeaponData();

			//m_logger.debugLog("weapon count: " + m_weapons_all.Count, "CanRespond()"); 

			return m_weapons_all.Count != 0;
		}

		public void UpdateTarget(LastSeen enemy)
		{
			if (enemy == null)
			{
				m_logger.debugLog(m_currentTarget != null, "lost target", "UpdateTarget()", Logger.severity.DEBUG);
				m_currentTarget = null;
				m_orbiter = null;
				return;
			}

			if (m_currentTarget == null || m_currentTarget.Entity != enemy.Entity)
			{
				m_logger.debugLog("new target: " + enemy.Entity.getBestName(), "UpdateTarget()", Logger.severity.DEBUG);
				m_currentTarget = enemy;
				m_navSet.Settings_Task_NavEngage.DestinationEntity = m_currentTarget.Entity;
			}
		}

		public bool CanTarget(IMyCubeGrid grid)
		{
			CubeGridCache cache = CubeGridCache.GetFor(grid);
			if (m_destroySet && cache.TerminalBlocks > 0)
				return true;

			if (m_currentTarget != null && grid == m_currentTarget.Entity && m_weapon_primary.CurrentTarget.TType != TargetType.None)
				return true;

			TargetType gridType = grid.GridSizeEnum == MyCubeSize.Small ? TargetType.SmallGrid
				: grid.IsStatic ? TargetType.Station
				: TargetType.LargeGrid;

			BlockTypeList targetBlocks;
			if (m_cumulative_targeting.TryGetValue(gridType, out targetBlocks))
				return targetBlocks.HasAny(cache, block => block.IsWorking);
			else
				m_logger.debugLog("no targeting at all for grid type of: " + grid.DisplayName, "CanTarget()");

			return false;
		}

		public override void Move()
		{
			if (!m_weaponArmed)
			{
				m_mover.StopMove();
				return;
			}

			if (m_weapon_primary == null)
			{
				m_logger.debugLog("no primary weapon", "Move()");
				m_mover.StopMove();
				return;
			}

			if (m_currentTarget == null)
			{
				m_mover.StopMove();
				return;
			}

			if (m_orbiter == null)
			{
				if (m_navSet.DistanceLessThan(m_weaponRange_min * 2f))
					m_orbiter = new Orbiter(m_mover, m_navSet, m_weapon_primary_pseudo, m_currentTarget.Entity, m_weaponRange_min - 50f, m_currentTarget.HostileName());
				else
				{
					m_mover.CalcMove(m_weapon_primary_pseudo, m_currentTarget.GetPosition(), m_currentTarget.Entity.Physics.LinearVelocity);
					return;
				}
			}

			m_orbiter.Move();

			////m_logger.debugLog("moving to " + (m_currentTarget.predictPosition() + m_currentOffset), "Move()");
			//m_mover.CalcMove(m_weapon_primary_pseudo, m_currentTarget.GetPosition() + m_currentOffset, m_currentTarget.GetLinearVelocity());
		}

		public void Rotate()
		{
			if (!m_weaponArmed)
			{
				m_mover.StopRotate();
				return;
			}

			if (m_weapon_primary == null)
			{
				m_logger.debugLog("no primary weapon", "Rotate()");
				Disarm();
				m_mover.StopRotate();
				return;
			}
			Vector3? FiringDirection = m_weapon_primary.CurrentTarget.FiringDirection;
			if (!FiringDirection.HasValue)
			{
				//m_logger.debugLog("no target", "Rotate()");
				//m_mover.CalcRotate(m_controlBlock.Pseudo, RelativeDirection3F.FromWorld(m_controlBlock.CubeGrid, m_currentTarget.Entity.GetCentre() - m_controlBlock.CubeBlock.GetPosition()));
				m_mover.CalcRotate();
				return;
			}
			//m_logger.debugLog("facing target at " + firingDirection.Value, "Rotate()");

			m_mover.CalcRotate(m_weapon_primary_pseudo, RelativeDirection3F.FromWorld(m_weapon_primary_pseudo.Grid, FiringDirection.Value), targetEntity: m_weapon_primary.CurrentTarget.Entity);
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			if (m_orbiter != null)
				m_orbiter.AppendCustomInfo(customInfo);
			else
			{
				customInfo.Append("Fighter moving to: ");
				customInfo.AppendLine(m_currentTarget.HostileName());
			}
		}

		private void Arm()
		{
			if (!ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bAllowWeaponControl))
			{
				m_logger.debugLog("Cannot arm, weapon control is disabled.", "Arm()", Logger.severity.WARNING);
				return;
			}

			m_logger.debugLog("Arming", "Arm()", Logger.severity.DEBUG);

			m_logger.debugLog(m_weapons_fixed.Count != 0, "Fixed weapons has not been cleared", "Arm()", Logger.severity.FATAL);
			m_logger.debugLog(m_weapons_all.Count != 0, "All weapons has not been cleared", "Arm()", Logger.severity.FATAL);

			m_weaponRange_min = float.MaxValue;

			CubeGridCache cache = CubeGridCache.GetFor(m_controlBlock.CubeGrid);

			foreach (MyObjectBuilderType weaponType in FixedWeaponTypes)
			{
				ReadOnlyList<IMyCubeBlock> weaponBlocks = cache.GetBlocksOfType(weaponType);
				if (weaponBlocks != null)
					foreach (IMyCubeBlock block in weaponBlocks)
					{
						FixedWeapon weapon;
						Registrar.TryGetValue(block.EntityId, out weapon);
						if (weapon.EngagerTakeControl())
						{
							m_logger.debugLog("Took control of " + weapon.CubeBlock.DisplayNameText, "Arm()");
							m_weapons_fixed.Add(weapon);
							m_weapons_all.Add(weapon);

							weapon.CubeBlock.OnClosing += Weapon_OnClosing;
						}
						else
							m_logger.debugLog("failed to get control of: " + weapon.CubeBlock.DisplayNameText, "Arm()");
					}
			}
			foreach (MyObjectBuilderType weaponType in TurretWeaponTypes)
			{
				ReadOnlyList<IMyCubeBlock> weaponBlocks = cache.GetBlocksOfType(weaponType);
				if (weaponBlocks != null)
					foreach (IMyCubeBlock block in weaponBlocks)
					{
						Turret weapon;
						Registrar.TryGetValue(block.EntityId, out weapon);
						if (weapon.CurrentControl != WeaponTargeting.Control.Off)
						{
							m_logger.debugLog("Active turret: " + weapon.CubeBlock.DisplayNameText, "Arm()");
							m_weapons_all.Add(weapon);

							weapon.CubeBlock.OnClosing += Weapon_OnClosing;
						}
					}
			}

			m_weapons_fixed.ApplyAdditions();
			m_weapons_all.ApplyAdditions();

			m_weaponArmed = m_weapons_all.Count != 0;
			m_weaponDataDirty = m_weaponArmed;
			if (m_weaponArmed)
				m_logger.debugLog("Now armed", "Arm()", Logger.severity.DEBUG);
			else
				m_logger.debugLog("Failed to arm", "Arm()", Logger.severity.DEBUG);
		}

		private void Disarm()
		{
			if (!m_weaponArmed)
				return;

			m_logger.debugLog("Disarming", "Disarm()", Logger.severity.DEBUG);

			foreach (FixedWeapon weapon in m_weapons_fixed)
				weapon.EngagerReleaseControl();

			foreach (WeaponTargeting weapon in m_weapons_all)
				weapon.CubeBlock.OnClosing -= Weapon_OnClosing;

			m_weapons_fixed.ClearImmediate();
			m_weapons_all.ClearImmediate();

			m_weaponArmed = false;
			m_weaponDataDirty = true;
		}

		/// <summary>
		/// Finds a primary weapon for m_weapon_primary and m_weapon_primary_pseudo.
		/// A primary weapon can be any working weapon with ammo.
		/// Preference is given to fixed weapons and weapons with targets.
		/// If no weapons have ammo, m_weapon_primary and m_weapon_primary_pseudo will be null.
		/// </summary>
		private void GetPrimaryWeapon()
		{
			if (m_weapon_primary != null && m_weapon_primary.CubeBlock.IsWorking && m_weapon_primary.CurrentTarget.Entity != null)
				return;

			m_weapon_primary = null;
			m_weapon_primary_pseudo = null;

			bool removed = false;
			foreach (FixedWeapon weapon in m_weapons_fixed)
				if (weapon.CubeBlock.IsWorking)
				{
					if (weapon.HasAmmo)
					{
						m_weapon_primary = weapon;
						if (weapon.CurrentTarget.Entity != null)
						{
							m_logger.debugLog("has target: " + weapon.CubeBlock.DisplayNameText, "GetPrimaryWeapon()");
							break;
						}
					}
					else
						m_logger.debugLog("no ammo: " + weapon.CubeBlock.DisplayNameText, "GetPrimaryWeapon()");
				}
				else
				{
					m_logger.debugLog("not working: " + weapon.CubeBlock.DisplayNameText, "GetPrimaryWeapon()");
					m_weapons_fixed.Remove(weapon);
					weapon.EngagerReleaseControl();
					removed = true;
				}

			if (m_weapon_primary == null)
				foreach (WeaponTargeting weapon in m_weapons_all)
					if (weapon.CubeBlock.IsWorking)
					{
						if (weapon.HasAmmo)
						{
							m_weapon_primary = weapon;
							if (weapon.CurrentTarget.Entity != null)
							{
								m_logger.debugLog("has target: " + weapon.CubeBlock.DisplayNameText, "GetPrimaryWeapon()");
								break;
							}
						}
						else
							m_logger.debugLog("no ammo: " + weapon.CubeBlock.DisplayNameText, "GetPrimaryWeapon()");
					}
					else
					{
						m_logger.debugLog("not working: " + weapon.CubeBlock.DisplayNameText, "GetPrimaryWeapon()");
						m_weapons_all.Remove(weapon);
						removed = true;
					}

			if (removed)
			{
				m_weapons_fixed.ApplyRemovals();
				m_weapons_all.ApplyRemovals();
				m_weaponDataDirty = true;
			}

			if (m_weapon_primary != null)
				m_weapon_primary_pseudo = new PseudoBlock(m_weapon_primary.CubeBlock);
		}

		private void UpdateWeaponData()
		{
			m_weaponRange_min = float.MaxValue;
			m_cumulative_targeting.Clear();
			m_destroySet = false;

			foreach (WeaponTargeting weapon in m_weapons_all)
			{
				if (!weapon.CubeBlock.IsWorking)
				{
					m_weapons_all.Remove(weapon);
					FixedWeapon asFixed = weapon as FixedWeapon;
					if (asFixed != null)
					{
						m_weapons_fixed.Remove(asFixed);
						asFixed.EngagerReleaseControl();
					}
					continue;
				}

				float TargetingRange = weapon.Options.TargetingRange;
				if (TargetingRange < 1 || !weapon.Options.CanTargetType(TargetType.AllGrid))
					continue;

				if (TargetingRange < m_weaponRange_min)
					m_weaponRange_min = TargetingRange;

				if (m_destroySet)
					continue;

				if (weapon.Options.CanTargetType(TargetType.Destroy))
				{
					m_destroySet = true;
					m_cumulative_targeting.Clear();
					continue;
				}

				foreach (TargetType type in CumulativeTypes)
					if (weapon.Options.CanTargetType(type))
						AddToCumulative(type, weapon.Options.listOfBlocks);
			}

			m_weapons_fixed.ApplyRemovals();
			m_weapons_all.ApplyRemovals();

			if (m_weapons_all.Count == 0)
			{
				m_logger.debugLog("No working weapons, " + GetType().Name + " is done here", "UpdateWeaponData()", Logger.severity.INFO);
				m_navSet.OnTaskComplete_NavEngage();
			}

			m_weaponDataDirty = false;
		}

		private void AddToCumulative(TargetType type, BlockTypeList blocks)
		{
			m_logger.debugLog("adding to type: " + type + ", count: " + blocks.BlockNamesContain.Length, "AddToCumulative()");

			if (type == TargetType.AllGrid)
			{
				AddToCumulative(TargetType.SmallGrid, blocks);
				AddToCumulative(TargetType.LargeGrid, blocks);
				AddToCumulative(TargetType.Station, blocks);
				return;
			}

			BlockTypeList targetBlocks;
			if (m_cumulative_targeting.TryGetValue(type, out targetBlocks))
				m_cumulative_targeting[type] = BlockTypeList.Union(targetBlocks, blocks);
			else
				m_cumulative_targeting[type] = blocks;
		}

		private void Weapon_OnClosing(IMyEntity obj)
		{ m_weaponDataDirty = true; }

	}
}
