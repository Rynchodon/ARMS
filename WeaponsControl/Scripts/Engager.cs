using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.ObjectBuilders;

namespace Rynchodon.Weapons
{
	/// <summary>
	/// <para>Shall be responsible for activating all the fixed weapons on a grid and providing directions to Autopilot.</para>
	/// <para>One weapon shall be the primary and shall be aimed.</para>
	/// <para>All other weapons shall fire as they bear.</para>
	/// </summary>
	public class Engager
	{
		public bool IsArmed { get { return PrimaryWeapon != null; } }

		private readonly IMyCubeGrid myGrid;
		private readonly Logger myLogger;

		private List<FixedWeapon> myWeapons;
		private FixedWeapon value_primary;

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

		private MyObjectBuilderType[] WeaponTypes = new MyObjectBuilderType[] { typeof(MyObjectBuilder_SmallGatlingGun), typeof(MyObjectBuilder_SmallMissileLauncher), typeof(MyObjectBuilder_SmallMissileLauncherReload) };

		/// <summary>
		/// Sets all the weapons to fire as they bear. Does nothing if armed.
		/// </summary>
		public void Arm()
		{
			if (myWeapons != null)
				return;

			myLogger.debugLog("Arming all weapons", "Arm()");

			myWeapons = new List<FixedWeapon>();
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
							myLogger.debugLog("Took control of " + weapon.weapon.DisplayNameText, "Arm()");
							myWeapons.Add(weapon);
						}
					}
			}
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
	}
}
