using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
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
		private readonly IMyCubeGrid myGrid;
		private readonly Logger myLogger;

		private MyObjectBuilderType[] WeaponTypes = new MyObjectBuilderType[] { typeof(MyObjectBuilder_SmallGatlingGun), typeof(MyObjectBuilder_SmallMissileLauncher), typeof(MyObjectBuilder_SmallMissileLauncherReload) };
		private List<FixedWeapon> myWeapons;
		private FixedWeapon value_primary;
		private float value_MinWeaponRange;

		public Engager(IMyCubeGrid grid)
		{
			this.myGrid = grid;
			this.myLogger = new Logger("Engager", () => myGrid.DisplayName);
		}

		~Engager()
		{ Disarm(); }

		/// <summary>
		/// The weapon that should be aimed.
		/// </summary>
		public FixedWeapon PrimaryWeapon
		{
			get
			{
				if (value_primary == null || value_primary.Closed)
				{
					value_primary = null;
					if (myWeapons != null)
						foreach (FixedWeapon weapon in myWeapons)
							if (!weapon.Closed)
							{
								value_primary = weapon;
								break;
							}
				}

				return value_primary;
			}
		}

		public float MinWeaponRange
		{
			get
			{
				if (value_MinWeaponRange < 1 || value_MinWeaponRange == float.MaxValue)
				{
					value_MinWeaponRange = float.MaxValue;
					foreach (FixedWeapon weapon in myWeapons)
						if (weapon.Options.TargetingRange < value_MinWeaponRange)
							value_MinWeaponRange = weapon.Options.TargetingRange;
				}
				return value_MinWeaponRange;
			}
		}
		public bool IsArmed { get { return PrimaryWeapon != null; } }


		/// <summary>
		/// Sets all the weapons to fire as they bear. Does nothing if armed.
		/// </summary>
		public void Arm()
		{
			if (myWeapons != null)
				return;

			myLogger.debugLog("Arming all weapons", "Arm()");

			myWeapons = new List<FixedWeapon>();
			value_MinWeaponRange = 0;
			CubeGridCache cache = CubeGridCache.GetFor(myGrid);

			foreach (MyObjectBuilderType weaponType in WeaponTypes)
			{
				ReadOnlyList<IMyCubeBlock> weaponBlocks = cache.GetBlocksOfType(weaponType);
				if (weaponBlocks != null)
					foreach (IMyCubeBlock block in weaponBlocks)
					{
						FixedWeapon weapon = FixedWeapon.GetFor(block);
						if (weapon.EngagerTakeControl(this))
						{
							//if (weapon.Options.TargetingRange < MinWeaponRange)
							//	MinWeaponRange = weapon.Options.TargetingRange;
							myLogger.debugLog("Took control of " + weapon.weapon.DisplayNameText, "Arm()");
							myWeapons.Add(weapon);
						}
					}
			}

			//myLogger.debugLog("MinWeaponRange = " + MinWeaponRange, "Arm()");
		}

		/// <summary>
		/// Stops tracking targets for all the weapons. Does nothing if not armed.
		/// </summary>
		public void Disarm()
		{
			if (myWeapons == null)
				return;

			myLogger.debugLog("Disarming all weapons", "Arm()");

			foreach (FixedWeapon weapon in myWeapons)
				weapon.EngagerReleaseControl(this);

			myWeapons = null;
		}

		public Vector3D GetWaypoint(Vector3D target)
		{
			Vector3D perpendicular;
			(target - myGrid.GetPosition()).CalculatePerpendicularVector(out perpendicular);
			perpendicular.Normalize();

			return perpendicular * MinWeaponRange * 0.75f + target;
		}
	}
}
