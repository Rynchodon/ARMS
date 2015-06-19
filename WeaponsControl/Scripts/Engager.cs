using System;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Collections;
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
		private static readonly MyObjectBuilderType[] WeaponTypes = new MyObjectBuilderType[] { typeof(MyObjectBuilder_SmallGatlingGun), typeof(MyObjectBuilder_SmallMissileLauncher), typeof(MyObjectBuilder_SmallMissileLauncherReload) };

		private readonly IMyCubeGrid myGrid;
		private readonly Logger myLogger;

		private CachingList<FixedWeapon> myFixedWeapons;
		private FixedWeapon value_primary;
		private float value_MinWeaponRange, value_MaxWeaponRange;

		public Engager(IMyCubeGrid grid)
		{
			this.myGrid = grid;
			this.myLogger = new Logger("Engager", () => myGrid.DisplayName);
		}

		~Engager()
		{ Disarm(); }

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
		public bool IsArmed { get { return myFixedWeapons != null && myFixedWeapons.Count > 0; } }
		//public bool IsApproaching { get; private set; }

		/// <summary>
		/// The weapon that should be aimed.
		/// </summary>
		public FixedWeapon GetPrimaryWeapon()
		{
			if (value_primary != null && !value_primary.Closed && value_primary.CurrentTarget.FiringDirection.HasValue)
				return value_primary;

			value_primary = null;
			if (myFixedWeapons != null)
			{
				foreach (FixedWeapon weapon in myFixedWeapons)
					if (!weapon.Closed)
					{
						value_primary = weapon;
						// it is preferable to choose a PrimaryWeapon that has a target
						if (weapon.CurrentTarget.Entity != null)
							break;
					}
					else
						myFixedWeapons.Remove(weapon);

				myFixedWeapons.ApplyRemovals();
			}

			return value_primary;
		}

		public bool GetHasTarget()
		{
			FixedWeapon Primary = GetPrimaryWeapon();
			if (Primary == null)
				return false;
			return Primary.CurrentTarget.Entity != null;
		}

		/// <summary>
		/// If not armed, sets all the weapons to fire as they bear
		/// </summary>
		private void Arm()
		{
			if (myFixedWeapons != null)
				return;

			myLogger.debugLog("Arming all weapons", "Arm()");

			myFixedWeapons = new CachingList<FixedWeapon>();
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
							myFixedWeapons.Add(weapon);
						}
					}
			}

			myFixedWeapons.ApplyAdditions();
			//myLogger.debugLog("MinWeaponRange = " + MinWeaponRange, "Arm()");
		}

		/// <summary>
		/// If armed, stops tracking targets for all the weapons.
		/// </summary>
		public void Disarm()
		{
			if (myFixedWeapons == null)
				return;

			myLogger.debugLog("Disarming all weapons", "Arm()");

			foreach (FixedWeapon weapon in myFixedWeapons)
				weapon.EngagerReleaseControl(this);

			myFixedWeapons = null;
			targetGrid = null;
		}

		///// <remarks>
		///// <para>Sets IsAproaching = false</para>
		///// </remarks>
		//public Vector3D GetWaypoint(Vector3D target)
		//{
		//	Vector3D perpendicular;
		//	(target - myGrid.GetPosition()).CalculatePerpendicularVector(out perpendicular);
		//	perpendicular.Normalize();

		//	return perpendicular * MinWeaponRange * 0.75f + target;
		//}

		private static readonly TimeSpan updateWaypoint = new TimeSpan(0, 0, 10);
		private IMyCubeGrid targetGrid;
		private Vector3D? PreviousWaypoint;
		//private DateTime nextWaypointUpdate = DateTime.MinValue;

		/// <summary>
		/// If any weapons can target, set the target and return true.
		/// </summary>
		public bool SetTarget(IMyCubeGrid targetGrid)
		{
			Arm();
			if (!IsArmed)
				return false;

			this.targetGrid = targetGrid;
			PreviousWaypoint = null;
			return true;
		}

		///// <summary>
		///// <para>Gets the point that Navigator should fly to.</para>
		///// </summary>
		//public Vector3D GetWaypoint()//(double distanceToWaypoint, out Vector3D waypoint)
		//{
		//	if (targetGrid == null)
		//		throw new InvalidOperationException("No target grid.");

		//	//if (!PreviousWaypoint.HasValue)
		//	//{
		//	//	PreviousWaypoint = waypoint = targetGrid.GetPosition();
		//	//	return true;
		//	//}

		//	//if (DateTime.UtcNow < nextWaypointUpdate)
		//	//if (PreviousWaypoint.HasValue && distanceToWaypoint > 25)
		//	//{
		//	//	waypoint = PreviousWaypoint.Value;
		//	//	return false;
		//	//}
		//	//nextWaypointUpdate = DateTime.UtcNow + updateWaypoint;

		//	Vector3D targetPos = targetGrid.GetPosition();
		//	Vector3D perpendicular;
		//	(targetPos - myGrid.GetPosition()).CalculatePerpendicularVector(out perpendicular);
		//	perpendicular.Normalize();

		//	PreviousWaypoint = perpendicular * MinWeaponRange * 0.75f + targetPos;
		//	myLogger.debugLog("setting new waypoint to: " + PreviousWaypoint + ", targetPos = " + targetPos + ", myGridPos = " + myGrid.GetPosition() + ", perpendicular = " + perpendicular + ", MinWeaponRange = " + MinWeaponRange, "GetWaypoint()");
		//	//return true;
		//	return PreviousWaypoint.Value;
		//}

		private static readonly Random RandomOffset = new Random();

		public Vector3D GetRandomOffset()
		{
			double half = MinWeaponRange / 2;
			return new Vector3D(RandomOffset.NextDouble() * half + half,
				RandomOffset.NextDouble() * half + half,
				RandomOffset.NextDouble() * half + half);
		}

		private void GetWeaponRanges()
		{
			value_MinWeaponRange = float.MaxValue;
			value_MaxWeaponRange = 0;
			foreach (FixedWeapon weapon in myFixedWeapons)
			{
				float TargetingRange = weapon.Options.TargetingRange;
				if (TargetingRange < 1)
					continue;

				if (TargetingRange < value_MinWeaponRange)
					value_MinWeaponRange = TargetingRange;
				if (TargetingRange > value_MaxWeaponRange)
					value_MaxWeaponRange = TargetingRange;
			}
		}
	}
}
