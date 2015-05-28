#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Autopilot.Weapons
{
	/// <summary>
	/// Contains functions that are common to turrets and fixed weapons
	/// </summary>
	public class WeaponTargeting
	{
		#region Targeting Options

		[Flags]
		public enum TargetType : byte
		{
			None = 0,
			Missile = 1 << 0,
			Meteor = 1 << 1,
			Character = 1 << 2,
			Moving = 1 << 3,
			LargeGrid = 1 << 4,
			SmallGrid = 1 << 5,
			Station = 1 << 6
		}

		private static Dictionary<uint, MyAmmoDefinition> AmmoDefinition = new Dictionary<uint, MyAmmoDefinition>();

		/// <summary>The range for targeting objects.</summary>
		public float TargetingRange;

		public TargetType CanTarget = TargetType.None;

		public bool CanTargetType(TargetType type)
		{ return (CanTarget & type) != 0; }

		#endregion

		private readonly IMyCubeBlock weapon;
		private readonly Ingame.IMyLargeTurretBase myTurret;

		private Dictionary<TargetType, List<IMyEntity>> Available_Targets;
		private List<IMyEntity> PotentialObstruction;

		private bool EnemyIsNear;

		private Logger myLogger;

		public WeaponTargeting(IMyCubeBlock weapon)
		{
			if (weapon == null)
				throw new ArgumentNullException("weapon");
			if (!(weapon is IMyTerminalBlock) || !(weapon is IMyFunctionalBlock) || !(weapon is IMyInventoryOwner))
				throw new ArgumentException("weapon(" + weapon.DefinitionDisplayNameText + ") is not of correct type");

			this.weapon = weapon;
			this.myTurret = weapon as Ingame.IMyLargeTurretBase;
			this.myLogger = new Logger("WeaponTargeting", () => weapon.CubeGrid.DisplayName, () => weapon.DisplayNameText);
		}

		/// <summary>
		/// Fills Available_Targets and PotentialObstruction
		/// </summary>
		private void CollectTargets()
		{
			Available_Targets = new Dictionary<TargetType, List<IMyEntity>>();
			PotentialObstruction = new List<IMyEntity>();

			BoundingSphereD nearbySphere = new BoundingSphereD(myTurret.GetPosition(), TargetingRange);
			HashSet<IMyEntity> nearbyEntities = new HashSet<IMyEntity>();
			MyAPIGateway.Entities.GetEntitiesInSphere_Safe_NoBlock(nearbySphere, nearbyEntities);

			foreach (IMyEntity entity in nearbyEntities)
			{
				if (entity is IMyFloatingObject)
				{
					AddTarget(TargetType.Moving, entity);
					continue;
				}

				if (entity is IMyMeteor)
				{
					AddTarget(TargetType.Meteor, entity);
					continue;
				}

				IMyCharacter asChar = entity as IMyCharacter;
				if (asChar != null)
				{
					if (weapon.canConsiderHostile(asChar))
						AddTarget(TargetType.Character, entity);
					else
						PotentialObstruction.Add(entity);
					continue;
				}

				IMyCubeGrid asGrid = entity as IMyCubeGrid;
				if (asGrid != null)
				{
					if (!asGrid.Save)
						continue;

					if (weapon.canConsiderHostile(asGrid))
					{
						AddTarget(TargetType.Moving, entity);
						if (asGrid.IsStatic)
							AddTarget(TargetType.Station, entity);
						else if (asGrid.GridSizeEnum == MyCubeSize.Large)
							AddTarget(TargetType.LargeGrid, entity);
						else
							AddTarget(TargetType.SmallGrid, entity);
					}
					else
						PotentialObstruction.Add(entity);
					continue;
				}

				if (entity.ToString().StartsWith("MyMissile"))
					AddTarget(TargetType.Missile, entity);
			}
		}

		/// <summary>
		/// Adds a target to Available_Targets
		/// </summary>
		private void AddTarget(TargetType tType, IMyEntity target)
		{
			if (!CanTargetType(tType))
				return;

			List<IMyEntity> list;
			if (!Available_Targets.TryGetValue(tType, out list))
			{
				list = new List<IMyEntity>();
				Available_Targets.Add(tType, list);
			}
			list.Add(target);
		}

		/// <summary>
		/// Gets the ammo definition for the first item in inventory.
		/// </summary>
		private MyAmmoDefinition GetCurrentAmmoDef()
		{
			IMyInventoryItem firstAmmo = (weapon as IMyInventoryOwner).GetInventory(0).GetItems()[0];
			MyAmmoDefinition ammoDef;
			if (!AmmoDefinition.TryGetValue(firstAmmo.ItemId, out ammoDef))
			{
				MyDefinitionId magazineId = firstAmmo.Content.GetObjectId();
				MyDefinitionId ammoDefId = MyDefinitionManager.Static.GetAmmoMagazineDefinition(magazineId).AmmoDefinitionId;
				ammoDef = MyDefinitionManager.Static.GetAmmoDefinition(ammoDefId);

				AmmoDefinition.Add(firstAmmo.ItemId, ammoDef);
			}

			return ammoDef;
		}

		#region Target Prediction

		/// <summary>
		/// Determines the direction a projectile must be fired in to hit the target.
		/// </summary>
		/// <param name="target">target</param>
		/// <param name="projectileSpeed">the speed of the projectile</param>
		/// <returns>the required direction</returns>
		/// Do we also need point of intersection? - This test might preceed CanShootAt, then we could test the trajectory not sight-to-target
		private Vector3? FiringDirection(IMyEntity target, float projectileSpeed)
		{
			if (weapon == null)
				throw new ArgumentNullException("weapon");
			if (target == null)
				throw new ArgumentNullException("target");

			Vector3D relativePos = target.GetPosition() - weapon.GetPosition();
			Vector3 relativeVelocity = target.GetLinearVelocity() - weapon.GetLinearVelocity();

			return FiringDirection(relativePos, relativeVelocity, projectileSpeed);
		}

		/// <summary>
		/// Gets the smallest positive root of the quadratic equation.
		/// </summary>
		private static float? SmallPositiveRoot(float a, float b, float c)
		{
			float sqrt = b * b - 4 * a * c;
			if (sqrt < 0)
				return null;
			sqrt = (float)Math.Sqrt(sqrt);

			float root = -b - sqrt / (2 * a);
			if (root > 0)
				return root;
			root = -b + sqrt / (2 * a);
			if (root > 0)
				return root;

			return null;
		}

		/// <remarks>
		/// Based on http://stackoverflow.com/a/17749335
		/// </remarks>
		private static Vector3? FiringDirection(Vector3 relativePosition, Vector3 relativeVelocity, float projectileSpeed)
		{
			float a = projectileSpeed * projectileSpeed - relativeVelocity.LengthSquared();
			float b = -2 * relativePosition.Dot(relativeVelocity);
			float c = -relativePosition.LengthSquared();

			float? time = SmallPositiveRoot(a, b, c);
			if (time == null)
				return null;

			return relativeVelocity + relativePosition / time.Value;
		}

		#endregion

		/// <summary>
		/// <para>Test line segment between weapon and target for obstructing entities.</para>
		/// <para>Tests for obstructing voxel map, non-hostile character, or non-hostile grid.</para>
		/// </summary>
		/// <param name="targetPos">entity to shoot</param>
		/// <param name="ignoreSourceGrid">ignore intersections with grid that weapon is part of</param>
		private bool CanShootAt(Vector3D targetPos, bool ignoreSourceGrid = false)
		{
			if (weapon == null)
				throw new ArgumentNullException("weapon");

			Vector3D weaponPos = weapon.GetPosition();

			// Voxel Test
			Vector3 boundary;
			if (MyAPIGateway.Entities.RayCastVoxel(weaponPos, targetPos, out boundary))
				return false;

			LineD laser = new LineD(weaponPos, targetPos);
			Vector3I position = new Vector3I();
			double distance = 0;

			// Test each entity
			foreach (IMyEntity entity in PotentialObstruction)
			{
				IMyCharacter asChar = entity as IMyCharacter;
				if (asChar != null)
				{
					if (entity.WorldAABB.Intersects(laser, out distance))
						return false;
					continue;
				}

				IMyCubeGrid asGrid = entity as IMyCubeGrid;
				if (asGrid != null)
				{
					if (asGrid.GetLineIntersectionExactGrid(ref laser, ref position, ref distance))
						return false;
					continue;
				}
			}

			// no obstruction found
			return true;
		}
	}
}
