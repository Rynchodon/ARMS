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
	public abstract class WeaponTargeting
	{
		#region Targeting Options

		/// <summary>
		/// Defined in the order of precedence
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
			Station = 1 << 6
		}

		public TargetType CanTarget = TargetType.None;
		public bool CanTargetType(TargetType type)
		{ return (CanTarget & type) != 0; }

		#endregion

		private class Target
		{
			public readonly IMyEntity Entity;
			public readonly TargetType TType;
			public Vector3? FiringDirection;

			public Target()
			{
				this.Entity = null;
				this.TType = TargetType.None;
			}

			public Target(IMyEntity entity, TargetType tType)
			{
				this.Entity = entity;
				this.TType = tType;
			}
		}

		private static Dictionary<uint, MyAmmoDefinition> AmmoDefinition = new Dictionary<uint, MyAmmoDefinition>();

		public readonly IMyCubeBlock weapon;
		public readonly Ingame.IMyLargeTurretBase myTurret;

		private Dictionary<TargetType, List<IMyEntity>> Available_Targets;
		//private List<IMyCubeGrid> Available_Targets_Grid;
		private List<IMyEntity> PotentialObstruction;

		private MyAmmoDefinition LoadedAmmo = null;

		private Target CurrentTarget = new Target();
		public Vector3? FiringDirection { get { return CurrentTarget.FiringDirection; } }

		private static readonly float GlobalMaxRange = Settings.GetSetting<float>(Settings.SettingName.fMaxWeaponRange);

		private float value_TargetingRange;
		/// <summary>
		/// <para>The range for targeting objects</para>
		/// <para>set will be tested against fMaxWeaponRange but does not test against ammunition range</para>
		/// </summary>
		protected float TargetingRange
		{
			get { return value_TargetingRange; }
			set
			{
				if (value <= GlobalMaxRange)
					value_TargetingRange = value;
				else
					value_TargetingRange = GlobalMaxRange;
			}
		}

		private byte updateCount = 0;

		private Logger myLogger;

		public WeaponTargeting(IMyCubeBlock weapon)
		{
			if (weapon == null)
				throw new ArgumentNullException("weapon");
			if (!(weapon is IMyTerminalBlock) || !(weapon is IMyFunctionalBlock) || !(weapon is IMyInventoryOwner))
				throw new ArgumentException("weapon(" + weapon.DefinitionDisplayNameText + ") is not of correct type");

			this.weapon = weapon;
			this.myTurret = weapon as Ingame.IMyLargeTurretBase;
			this.myLogger = new Logger("WeaponTargeting", () => weapon.CubeGrid.DisplayName, () => weapon.DefinitionDisplayNameText, () => weapon.getNameOnly());
		}

		/// <summary>
		/// Determines the FiringDirection
		/// </summary>
		protected void Update1()
		{
			if (LoadedAmmo == null)
				return;

			if (CurrentTarget.TType == TargetType.None)
				CurrentTarget.FiringDirection = null;
			else
			{
				CurrentTarget.FiringDirection = GetFiringDirection(CurrentTarget.Entity);
				myLogger.debugLog("Target entity = " + CurrentTarget.Entity + ", position = " + CurrentTarget.Entity.GetPosition() + ", predicted = " + CurrentTarget.FiringDirection, "Update1()");
			}
		}

		/// <summary>
		/// Checks for ammo and chooses a target (if necessary).
		/// </summary>
		protected void Update10()
		{
			UpdateAmmo();
			if (LoadedAmmo == null)
				return;

			CollectTargets();
			PickATarget();
			myLogger.debugLog("Current Target = " + CurrentTarget.Entity.getBestName(), "Update10()");
		}

		/// <summary>
		/// Gets targeting options from name.
		/// </summary>
		protected void Update100()
		{
			TargetOptionsFromName();
		}

		/// <summary>
		/// Get the targeting options from the name.
		/// </summary>
		private void TargetOptionsFromName()
		{
			// TODO: ...
		}

		private void UpdateAmmo()
		{
			List<IMyInventoryItem> loaded = (weapon as IMyInventoryOwner).GetInventory(0).GetItems();
			if (loaded.Count == 0 || loaded[0].Amount < 1)
			{
				myLogger.debugLog("No ammo loaded.", "UpdateAmmo()");
				LoadedAmmo = null;
				return;
			}

			MyAmmoDefinition ammoDef;
			if (!AmmoDefinition.TryGetValue(loaded[0].ItemId, out ammoDef))
			{
				MyDefinitionId magazineId = loaded[0].Content.GetObjectId();
				myLogger.debugLog("magazineId = " + magazineId, "UpdateAmmo()");
				MyDefinitionId ammoDefId = MyDefinitionManager.Static.GetAmmoMagazineDefinition(magazineId).AmmoDefinitionId;
				myLogger.debugLog("ammoDefId = " + ammoDefId, "UpdateAmmo()");
				ammoDef = MyDefinitionManager.Static.GetAmmoDefinition(ammoDefId);
				myLogger.debugLog("ammoDef = " + ammoDef, "UpdateAmmo()");

				AmmoDefinition.Add(loaded[0].ItemId, ammoDef);
			}
			else
				myLogger.debugLog("Got ammo from Dictionary: " + ammoDef, "UpdateAmmo()");

			LoadedAmmo = ammoDef;
		}

		private float LoadedAmmoSpeed(Vector3D target)
		{
			MyMissileAmmoDefinition missileAmmo = LoadedAmmo as MyMissileAmmoDefinition;
			if (missileAmmo == null)
			{
				myLogger.debugLog("speed of " + LoadedAmmo + " is " + LoadedAmmo.DesiredSpeed, "LoadedAmmoSpeed()");
				return LoadedAmmo.DesiredSpeed;
			}

			myLogger.alwaysLog("Missile ammo not implemented, using desired speed: " + LoadedAmmo.DesiredSpeed, "LoadedAmmoSpeed()", Logger.severity.WARNING);
			return LoadedAmmo.DesiredSpeed;
		}

		/// <summary>
		/// Fills Available_Targets and PotentialObstruction
		/// </summary>
		private void CollectTargets()
		{
			myLogger.debugLog("Entered CollectTargets", "CollectTargets()");

			Available_Targets = new Dictionary<TargetType, List<IMyEntity>>();
			//Available_Targets_Grid = new List<IMyCubeGrid>();
			PotentialObstruction = new List<IMyEntity>();

			BoundingSphereD nearbySphere = new BoundingSphereD(myTurret.GetPosition(), TargetingRange);
			HashSet<IMyEntity> nearbyEntities = new HashSet<IMyEntity>();
			MyAPIGateway.Entities.GetEntitiesInSphere_Safe_NoBlock(nearbySphere, nearbyEntities);

			myLogger.debugLog("found " + nearbyEntities.Count + " entities", "CollectTargets()");

			foreach (IMyEntity entity in nearbyEntities)
			{
				myLogger.debugLog("Nearby entity: " + entity.getBestName(), "CollectTargets()");

				if (entity is IMyFloatingObject)
				{
					myLogger.debugLog("floater: " + entity.getBestName(), "CollectTargets()");
					AddTarget(TargetType.Moving, entity);
					continue;
				}

				if (entity is IMyMeteor)
				{
					myLogger.debugLog("meteor: " + entity.getBestName(), "CollectTargets()");
					AddTarget(TargetType.Meteor, entity);
					continue;
				}

				IMyCharacter asChar = entity as IMyCharacter;
				if (asChar != null)
				{
					IMyPlayer asPlayer = asChar.GetPlayer_Safe();

					if (asPlayer == null || weapon.canConsiderHostile(asPlayer.PlayerID))
					{
						myLogger.debugLog("Hostile Character(" + asPlayer + "): " + entity.getBestName(), "CollectTargets()");
						AddTarget(TargetType.Character, entity);
					}
					else
					{
						myLogger.debugLog("Non-Hostile Character: " + entity.getBestName(), "CollectTargets()");
						PotentialObstruction.Add(entity);
					}
					continue;
				}

				IMyCubeGrid asGrid = entity as IMyCubeGrid;
				if (asGrid != null)
				{
					if (!asGrid.Save)
					{
						myLogger.debugLog("No Save Grid: " + entity.getBestName(), "CollectTargets()");
						continue;
					}

					if (weapon.canConsiderHostile(asGrid))
					{
						AddTarget(TargetType.Moving, entity);
						if (asGrid.IsStatic)
						{
							myLogger.debugLog("Hostile Platform: " + entity.getBestName(), "CollectTargets()");
							AddTarget(TargetType.Station, entity);
						}
						else if (asGrid.GridSizeEnum == MyCubeSize.Large)
						{
							myLogger.debugLog("Hostile Large Ship: " + entity.getBestName(), "CollectTargets()");
							AddTarget(TargetType.LargeGrid, entity);
						}
						else
						{
							myLogger.debugLog("Hostile Small Ship: " + entity.getBestName(), "CollectTargets()");
							AddTarget(TargetType.SmallGrid, entity);
						}
					}
					else
					{
						myLogger.debugLog("Friendly Grid: " + entity.getBestName(), "CollectTargets()");
						PotentialObstruction.Add(entity);
					}
					continue;
				}

				if (entity.ToString().StartsWith("MyMissile"))
				{
					AddTarget(TargetType.Missile, entity);
					continue;
				}

				myLogger.debugLog("Some Useless Entity: " + entity.getBestName(), "CollectTargets()");
			}

			myLogger.debugLog("Target Type Count = " + Available_Targets.Count, "CollectTargets()");
			foreach (var Pair in Available_Targets)
				myLogger.debugLog("Targets = " + Pair.Key + ", Count = " + Pair.Value.Count, "CollectTargets()");
		}

		/// <summary>
		/// Adds a target to Available_Targets
		/// </summary>
		private void AddTarget(TargetType tType, IMyEntity target)
		{
			if (!CanTargetType(tType))
			{
				myLogger.debugLog("Cannot add type: " + tType, "AddTarget()");
				return;
			}

			List<IMyEntity> list;
			if (!Available_Targets.TryGetValue(tType, out list))
			{
				list = new List<IMyEntity>();
				Available_Targets.Add(tType, list);
			}
			list.Add(target);
		}

		/// <summary>
		/// <para>If current target is a projectile, check that it is valid</para>
		/// <para>Choose a target from Available_Targets.</para>
		/// </summary>
		private void PickATarget()
		{
			switch (CurrentTarget.TType)
			{
				case TargetType.Missile:
				case TargetType.Meteor:
				case TargetType.Moving:
					{
						if (ProjectileIsThreat(CurrentTarget.Entity))
							return;
						CurrentTarget = new Target();
						return;
					}
			}

			if (PickAProjectile(TargetType.Missile) || PickAProjectile(TargetType.Meteor) || PickAProjectile(TargetType.Moving))
				return;

			double closerThan = double.MaxValue;
			if (GetClosest(TargetType.Character, ref closerThan))
				return;

			// do not short for grid test
			GetClosest(TargetType.LargeGrid, ref closerThan);
			GetClosest(TargetType.SmallGrid, ref closerThan);
			GetClosest(TargetType.Station, ref closerThan);
		}

		/// <summary>
		/// Get the closest target of the specified type from Available_Targets[tType].
		/// </summary>
		private bool GetClosest(TargetType tType, ref double closerThan)
		{
			List<IMyEntity> targetsOfType;
			if (Available_Targets.TryGetValue(tType, out targetsOfType))
			{
				IMyEntity closest = null;
				double closestDistance = double.MaxValue;

				Vector3D weaponPosition = weapon.GetPosition();
				bool isFixed = myTurret == null;

				foreach (IMyEntity target in targetsOfType)
				{
					Vector3D targetPosition = target.GetPosition();
					if (Obstructed(targetPosition, isFixed))
						continue;

					double distance = Vector3D.DistanceSquared(targetPosition, weaponPosition);
					if (distance < closestDistance)
					{
						// TODO: test grids for containing blocks and return those blocks

						closest = target;
						closestDistance = distance;
					}
				}

				if (closest != null)
				{
					CurrentTarget = new Target(closest, tType);
					return true;
				}
				return false;
			}

			return false;
		}

		/// <summary>
		/// Get any projectile which is a threat from Available_Targets[tType].
		/// </summary>
		private bool PickAProjectile(TargetType tType)
		{
			List<IMyEntity> targetsOfType;
			if (Available_Targets.TryGetValue(tType, out targetsOfType))
			{
				bool isFixed = myTurret == null;

				foreach (IMyEntity projectile in targetsOfType)
					if (ProjectileIsThreat(projectile) && Obstructed(projectile.GetPosition(), isFixed))
					{
						CurrentTarget = new Target(projectile, tType);
						return true;
					}
			}

			return false;
		}

		/// <summary>
		/// <para>Approaching, going to intersect protection area.</para>
		/// </summary>
		private bool ProjectileIsThreat(IMyEntity projectile)
		{
			if (projectile.Closed)
				return false;

			Vector3 projectileVelocity = projectile.GetLinearVelocity();
			if (projectileVelocity.LengthSquared() < 100)
				return false;

			Vector3D projectilePosition = projectile.GetPosition();
			BoundingSphereD protectionArea = new BoundingSphereD(weapon.GetPosition(), LoadedAmmoSpeed(projectilePosition) / 10f);
			if (protectionArea.Contains(projectilePosition) == ContainmentType.Disjoint)
				// too late to stop it (also, no test for moving away)
				return false;

			RayD projectileRay = new RayD(projectilePosition, Vector3D.Normalize(projectileVelocity));
			return projectileRay.Intersects(protectionArea) != null;
		}

		#region Target Prediction

		/// <summary>
		/// Determines the direction a projectile must be fired in to hit the target.
		/// </summary>
		/// <param name="target">target</param>
		/// <returns>the required direction</returns>
		/// Do we also need point of intersection? - This test might preceed Unobstructed, then we could test the trajectory not sight-to-target
		private Vector3? GetFiringDirection(IMyEntity target)
		{
			if (weapon == null)
				throw new ArgumentNullException("weapon");
			if (target == null)
				throw new ArgumentNullException("target");

			Vector3D targetPosition = target.GetPosition();
			myLogger.debugLog("targetPosition = " + targetPosition, "GetFiringDirection()");

			Vector3D relativePos = targetPosition - weapon.GetPosition();
			Vector3 relativeVelocity = target.GetLinearVelocity() - weapon.GetLinearVelocity();
			myLogger.debugLog("relativePos = " + relativePos + ", relativeVelocity = " + relativeVelocity, "GetFiringDirection()");

			return GetFiringDirection(relativePos, relativeVelocity, LoadedAmmoSpeed(targetPosition));
		}

		/// <remarks>
		/// Based on http://stackoverflow.com/a/17749335
		/// </remarks>
		private static Vector3? GetFiringDirection(Vector3 relativePosition, Vector3 relativeVelocity, float projectileSpeed)
		{
			float a = projectileSpeed * projectileSpeed - relativeVelocity.LengthSquared();
			float b = -2 * relativePosition.Dot(relativeVelocity);
			float c = -relativePosition.LengthSquared();

			float? time = SmallPositiveRoot(a, b, c);
			if (time == null)
				return null;

			return relativeVelocity + relativePosition / time.Value;
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

		#endregion

		/// <summary>
		/// <para>Test line segment between weapon and target for obstructing entities.</para>
		/// <para>Tests for obstructing voxel map, non-hostile character, or non-hostile grid.</para>
		/// </summary>
		/// <param name="targetPos">entity to shoot</param>
		/// <param name="ignoreSourceGrid">ignore intersections with grid that weapon is part of</param>
		private bool Obstructed(Vector3D targetPos, bool ignoreSourceGrid = false)
		{
			if (weapon == null)
				throw new ArgumentNullException("weapon");

			Vector3D weaponPos = weapon.GetPosition();

			// Voxel Test
			Vector3 boundary;
			if (MyAPIGateway.Entities.RayCastVoxel(weaponPos, targetPos, out boundary))
				return true;

			LineD laser = new LineD(weaponPos, targetPos);
			//Vector3I position = new Vector3I();
			double distance;

			// Test each entity
			foreach (IMyEntity entity in PotentialObstruction)
			{
				IMyCharacter asChar = entity as IMyCharacter;
				if (asChar != null)
				{
					if (entity.WorldAABB.Intersects(laser, out distance))
						return true;
					continue;
				}

				IMyCubeGrid asGrid = entity as IMyCubeGrid;
				if (asGrid != null)
				{
					if (ignoreSourceGrid && asGrid == weapon.CubeGrid)
						continue;

					// use raycast cells so that weapon can be ignored and shot is less likely to hit a moving friendly
					List<Vector3I> cells = new List<Vector3I>();
					asGrid.RayCastCells(weaponPos, targetPos, cells);
					if (cells.Count == 0)
						continue;

					foreach (Vector3I pos in cells)
					{
						if (asGrid.CubeExists(pos))
							if (!ignoreSourceGrid && asGrid == weapon.CubeGrid)
							{
								IMySlimBlock block = asGrid.GetCubeBlock(pos);
								if (block.FatBlock == null || block.FatBlock != weapon)
									return true;
							}
							else
								return true;
					}
				}
			}

			// no obstruction found
			return false;
		}
	}
}
