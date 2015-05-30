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
	/// TODO: instructions from text panel
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

		List<Lazy<string>> blocksToTarget = new List<Lazy<string>>();

		#endregion

		public class Target
		{
			public readonly IMyEntity Entity;
			public readonly TargetType TType;
			public Vector3? FiringDirection;
			public Vector3? InterceptionPoint;

			/// <summary>
			/// Creates a target of type None with Entity as null.
			/// </summary>
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

		/// <summary>Required length squared between current direction and target direction to fire.</summary>
		private const float firingThreshold = 0.01f;
		private static Dictionary<uint, MyAmmoDefinition> AmmoDefinition = new Dictionary<uint, MyAmmoDefinition>();

		public readonly IMyCubeBlock weapon;
		public readonly Ingame.IMyLargeTurretBase myTurret;

		private Dictionary<TargetType, List<IMyEntity>> Available_Targets;
		private List<IMyEntity> PotentialObstruction;

		private MyAmmoDefinition LoadedAmmo = null;

		protected Target CurrentTarget { get; private set; }
		private HashSet<long> Blacklist = new HashSet<long>();

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

		protected bool IsControllingWeapon { get; private set; }
		/// <summary>Tests whether or not WeaponTargeting has set the turret to shoot.</summary>
		protected bool IsShooting { get; private set; }

		private Logger myLogger;

		public WeaponTargeting(IMyCubeBlock weapon)
		{
			if (weapon == null)
				throw new ArgumentNullException("weapon");
			if (!(weapon is IMyTerminalBlock) || !(weapon is IMyFunctionalBlock) || !(weapon is IMyInventoryOwner) || !(weapon is Ingame.IMyUserControllableGun))
				throw new ArgumentException("weapon(" + weapon.DefinitionDisplayNameText + ") is not of correct type");

			this.weapon = weapon;
			this.myTurret = weapon as Ingame.IMyLargeTurretBase;
			this.myLogger = new Logger("WeaponTargeting", () => weapon.CubeGrid.DisplayName, () => weapon.DefinitionDisplayNameText, () => weapon.getNameOnly());

			this.CurrentTarget = new Target();
			this.IsControllingWeapon = false;
		}

		/// <summary>
		/// Determines firing direction & intersection point.
		/// </summary>
		public void Update1()
		{
			if (!IsControllingWeapon)
				return;

			if (LoadedAmmo == null)
				return;

			if (CurrentTarget.TType == TargetType.None)
			{
				CurrentTarget.FiringDirection = null;
				CurrentTarget.InterceptionPoint = null;
			}
			else
				SetFiringDirection();
		}

		/// <summary>
		/// Updates can control weapon. Checks for ammo and chooses a target (if necessary).
		/// </summary>
		public void Update10()
		{
			if (!CanControlWeapon())
				return;

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
		public void Update100()
		{
			if (!IsControllingWeapon) 
				return;

			TargetOptionsFromName();
			Blacklist = new HashSet<long>();

			if (!CanTargetType(CurrentTarget.TType))
				CurrentTarget = new Target();
		}

		/// <summary>
		/// Used to apply restrictions on rotation, such as min/max elevation/azimuth.
		/// </summary>
		/// <param name="targetPoint">The point of the target.</param>
		/// <returns>true if the rotation is allowed</returns>
		protected abstract bool CanRotateTo(Vector3D targetPoint);

		private bool CanControlWeapon()
		{
			if (weapon.IsWorking
				&& (myTurret == null || !myTurret.IsUnderControl)
				&& weapon.DisplayNameText.Contains("[") && weapon.DisplayNameText.Contains("]"))
			{
				if (!IsControllingWeapon)
				{
					//myLogger.debugLog("Now controlling weapon", "CanControlWeapon()");
					IsControllingWeapon = true;
					IsShooting = false;

					// stop shooting
					var builder = weapon.GetSlimObjectBuilder_Safe() as MyObjectBuilder_UserControllableGun;
					if (builder.IsShootingFromTerminal)
					{
						myLogger.debugLog("Now controlling weapon, stop shooting", "CanControlWeapon()");
						(weapon as IMyTerminalBlock).GetActionWithName("Shoot").Apply(weapon);
					}
					else
					{
						myLogger.debugLog("Now controlling weapon, not shooting", "CanControlWeapon()");
					}

					// disable default targeting
					if (myTurret != null)
					{
						//	myLogger.debugLog("disabling default targeting", "CanControlWeapon()");
						myTurret.SetTarget(myTurret);
						myTurret.EnableIdleRotation = false;
						myTurret.SyncEnableIdleRotation();
					}
				}

				return true;
			}
			else
			{
				if (IsControllingWeapon)
				{
					//myLogger.debugLog("No longer controlling weapon", "CanControlWeapon()");
					IsControllingWeapon = false;
					IsShooting = false;

					// remove target
					CurrentTarget = new Target();

					// stop shooting
					var builder = weapon.GetSlimObjectBuilder_Safe() as MyObjectBuilder_UserControllableGun;
					if (builder.IsShootingFromTerminal)
					{
						myLogger.debugLog("No longer controlling weapon, stop shooting", "CanControlWeapon()");
						(weapon as IMyTerminalBlock).GetActionWithName("Shoot").Apply(weapon);
					}
					else
					{
						myLogger.debugLog("No longer controlling weapon, not shooting", "CanControlWeapon()");
					}

					// enable default targeting
					if (myTurret != null)
						myTurret.ResetTargetingToDefault();
				}
				return false;
			}
		}

		/// <summary>
		/// Get the targeting options from the name.
		/// </summary>
		private void TargetOptionsFromName()
		{
			blocksToTarget = new List<Lazy<string>>();
			CanTarget = TargetType.None;

			string[] allInstructions = weapon.DisplayNameText.getInstructions().RemoveWhitespace().Split(new char[] { ',',';', ':' });

			foreach (string instruction in allInstructions)
			{
				TargetType tType;
				if (Enum.TryParse(instruction, true, out tType))
				{
					myLogger.debugLog("adding to CanTarget: " + instruction, "TargetOptionsFromName()");
					CanTarget |= tType;
					continue;
				}

				myLogger.debugLog("adding to block search: " + instruction, "TargetOptionsFromName()");
				blocksToTarget.Add(new Lazy<string>(() => CubeGridCache.getKnownDefinition(instruction)));
			}
		}

		private void UpdateAmmo()
		{
			List<IMyInventoryItem> loaded = (weapon as IMyInventoryOwner).GetInventory(0).GetItems();
			if (loaded.Count == 0 || loaded[0].Amount < 1)
			{
				//myLogger.debugLog("No ammo loaded.", "UpdateAmmo()");
				LoadedAmmo = null;
				return;
			}

			MyAmmoDefinition ammoDef;
			if (!AmmoDefinition.TryGetValue(loaded[0].ItemId, out ammoDef))
			{
				MyDefinitionId magazineId = loaded[0].Content.GetObjectId();
				//myLogger.debugLog("magazineId = " + magazineId, "UpdateAmmo()");
				MyDefinitionId ammoDefId = MyDefinitionManager.Static.GetAmmoMagazineDefinition(magazineId).AmmoDefinitionId;
				//myLogger.debugLog("ammoDefId = " + ammoDefId, "UpdateAmmo()");
				ammoDef = MyDefinitionManager.Static.GetAmmoDefinition(ammoDefId);
				//myLogger.debugLog("ammoDef = " + ammoDef, "UpdateAmmo()");

				AmmoDefinition.Add(loaded[0].ItemId, ammoDef);
			}
			//else
			//	myLogger.debugLog("Got ammo from Dictionary: " + ammoDef, "UpdateAmmo()");

			LoadedAmmo = ammoDef;
		}

		private float LoadedAmmoSpeed(Vector3D target)
		{
			MyMissileAmmoDefinition missileAmmo = LoadedAmmo as MyMissileAmmoDefinition;
			if (missileAmmo == null)
			{
				//myLogger.debugLog("speed of " + LoadedAmmo + " is " + LoadedAmmo.DesiredSpeed, "LoadedAmmoSpeed()");
				return LoadedAmmo.DesiredSpeed;
			}

			// TODO: missile ammo

			myLogger.alwaysLog("Missile ammo not implemented, using desired speed: " + LoadedAmmo.DesiredSpeed, "LoadedAmmoSpeed()", Logger.severity.WARNING);
			return LoadedAmmo.DesiredSpeed;
		}

		/// <summary>
		/// <para>If supplied value is small, fire the weapon.</para>
		/// <para>If supplied value is large, stop firing.</para>
		/// </summary>
		protected void CheckFire(float lengthSquared)
		{
			if (lengthSquared < firingThreshold)
			{
				if (!IsShooting)
				{
					// start firing
					myLogger.debugLog("Open fire LS: "+lengthSquared, "CheckFire()");

					(weapon as IMyTerminalBlock).GetActionWithName("Shoot").Apply(weapon);
					IsShooting = true;
				}
				//else
				//	myLogger.debugLog("Keep firing LS: " + lengthSquared, "CheckFire()");
			}
			else
			{
				if (IsShooting)
				{
					// stop firing
					myLogger.debugLog("Hold fire LS: " + lengthSquared, "CheckFire()");

					(weapon as IMyTerminalBlock).GetActionWithName("Shoot").Apply(weapon);
					IsShooting = false;
				}
				//else
				//	myLogger.debugLog("Continue holding LS: " + lengthSquared, "CheckFire()");
			}
		}

		/// <summary>
		/// Fills Available_Targets and PotentialObstruction
		/// </summary>
		private void CollectTargets()
		{
			//myLogger.debugLog("Entered CollectTargets", "CollectTargets()");

			Available_Targets = new Dictionary<TargetType, List<IMyEntity>>();
			//Available_Targets_Grid = new List<IMyCubeGrid>();
			PotentialObstruction = new List<IMyEntity>();

			BoundingSphereD nearbySphere = new BoundingSphereD(myTurret.GetPosition(), TargetingRange);
			HashSet<IMyEntity> nearbyEntities = new HashSet<IMyEntity>();
			MyAPIGateway.Entities.GetEntitiesInSphere_Safe_NoBlock(nearbySphere, nearbyEntities);
			//List<IMyEntity> nearbyEntities = MyAPIGateway.Entities.GetEntitiesInSphere_Safe(ref nearbySphere);

			//myLogger.debugLog("found " + nearbyEntities.Count + " entities", "CollectTargets()");

			foreach (IMyEntity entity in nearbyEntities)
			{
				//if (entity is IMyCubeBlock)
				//	continue;

				//myLogger.debugLog("Nearby entity: " + entity.getBestName(), "CollectTargets()");

				if (Blacklist.Contains(entity.EntityId))
					continue;

				if (entity is IMyFloatingObject)
				{
					//myLogger.debugLog("floater: " + entity.getBestName(), "CollectTargets()");
					AddTarget(TargetType.Moving, entity);
					continue;
				}

				if (entity is IMyMeteor)
				{
					//myLogger.debugLog("meteor: " + entity.getBestName(), "CollectTargets()");
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
						//myLogger.debugLog("No Save Grid: " + entity.getBestName(), "CollectTargets()");
						continue;
					}

					if (weapon.canConsiderHostile(asGrid))
					{
						AddTarget(TargetType.Moving, entity);
						if (asGrid.IsStatic)
						{
							//myLogger.debugLog("Hostile Platform: " + entity.getBestName(), "CollectTargets()");
							AddTarget(TargetType.Station, entity);
						}
						else if (asGrid.GridSizeEnum == MyCubeSize.Large)
						{
							//myLogger.debugLog("Hostile Large Ship: " + entity.getBestName(), "CollectTargets()");
							AddTarget(TargetType.LargeGrid, entity);
						}
						else
						{
							//myLogger.debugLog("Hostile Small Ship: " + entity.getBestName(), "CollectTargets()");
							AddTarget(TargetType.SmallGrid, entity);
						}
					}
					else
					{
						//myLogger.debugLog("Friendly Grid: " + entity.getBestName(), "CollectTargets()");
						PotentialObstruction.Add(entity);
					}
					continue;
				}

				if (entity.ToString().StartsWith("MyMissile"))
				{
					//myLogger.debugLog("Missile: " + entity.getBestName(), "CollectTargets()");
					AddTarget(TargetType.Missile, entity);
					continue;
				}

				//myLogger.debugLog("Some Useless Entity: " + entity + " - " + entity.getBestName() + " - " + entity.GetType(), "CollectTargets()");
			}

			//myLogger.debugLog("Target Type Count = " + Available_Targets.Count, "CollectTargets()");
			//foreach (var Pair in Available_Targets)
			//	myLogger.debugLog("Targets = " + Pair.Key + ", Count = " + Pair.Value.Count, "CollectTargets()");
		}

		/// <summary>
		/// Adds a target to Available_Targets
		/// </summary>
		private void AddTarget(TargetType tType, IMyEntity target)
		{
			if (!CanTargetType(tType))
			//{
			//	myLogger.debugLog("Cannot add type: " + tType, "AddTarget()");
				return;
			//}

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
						if (ProjectileIsThreat(CurrentTarget.Entity, CurrentTarget.TType))
							return;
						CurrentTarget = new Target();
						return;
					}
			}

			if (PickAProjectile(TargetType.Missile) || PickAProjectile(TargetType.Meteor) || PickAProjectile(TargetType.Moving))
				return;

			double closerThan = double.MaxValue;
			if (SetClosest(TargetType.Character, ref closerThan))
				return;

			// do not short for grid test
			SetClosest(TargetType.LargeGrid, ref closerThan);
			SetClosest(TargetType.SmallGrid, ref closerThan);
			SetClosest(TargetType.Station, ref closerThan);
		}

		/// <summary>
		/// Get the closest target of the specified type from Available_Targets[tType].
		/// </summary>
		private bool SetClosest(TargetType tType, ref double closerThan)
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

					int distanceMultiplier = 0;
					IMyCubeGrid asGrid = target as IMyCubeGrid;
					// should target moving even if it does not match any blocks
					if (asGrid != null && tType != TargetType.Moving)
					{
						distanceMultiplier = GridTest(asGrid);
						if (distanceMultiplier < 0) // does not contain any blocksToTarget
						{
							myLogger.debugLog("does not contain any blocksToTarget: " + asGrid.DisplayName, "SetClosest()");
							continue;
						}
						else
							myLogger.debugLog(asGrid.DisplayName + " contains block " + blocksToTarget[distanceMultiplier].Value, "SetClosest()");
					}

					distanceMultiplier++;

					double distance = Vector3D.DistanceSquared(targetPosition, weaponPosition) * distanceMultiplier;
					if (distance < closestDistance)
					{
						closest = target;
						closestDistance = distance;
					}
				}

				IMyCubeGrid closestAsGrid = closest as IMyCubeGrid;
				if (closestAsGrid != null)
					closest = GetTargetBlock(closestAsGrid);

				if (closest != null)
				{
					CurrentTarget = new Target(closest, tType);
					return true;
				}
				return false;
			}

			return false;
		}

		#region Test Grids

		/// <summary>
		/// Test a grid for blocksToTarget
		/// </summary>
		/// <returns>index of found block from blocksToTarget</returns>
		private int GridTest(IMyCubeGrid grid)
		{
			for (int index = 0; index < blocksToTarget.Count; index++)
				if (CubeGridCache.GetFor(grid).ContainsByDefinition(blocksToTarget[index].Value))
					return index;

			return -1;
		}

		/// <summary>
		/// Gets the best block to target from a grid, first by checking blocksToTarget, then just grabs the closest IMyCubeBlock.
		/// </summary>
		private IMyCubeBlock GetTargetBlock(IMyCubeGrid grid)
		{
			Vector3D myPosition = weapon.GetPosition();
			double closestDistance = value_TargetingRange * value_TargetingRange;

			// get best block from blocksToTarget
			CubeGridCache cache = CubeGridCache.GetFor(grid);
			foreach (Lazy<string> blocksSearch in blocksToTarget)
			{
				ReadOnlyList<Ingame.IMyTerminalBlock> blocksWithDef = cache.GetBlocksByDefinition(blocksSearch.Value);
				if (blocksWithDef != null)
				{
					Ingame.IMyTerminalBlock closest = null;
					foreach (Ingame.IMyTerminalBlock block in blocksWithDef)
					{
						if (!block.IsWorking)
							continue;

						double distance = Vector3D.DistanceSquared(myPosition, block.GetPosition());
						if (distance < closestDistance)
						{
							closestDistance = distance;
							closest = block;
						}
					}
					if (closest != null)
						return closest as IMyCubeBlock;
				}
			}

			// get closest IMyCubeBlock
			{
				IMyCubeBlock closest = null;
				List<IMySlimBlock> allSlims = new List<IMySlimBlock>();
				grid.GetBlocks_Safe(allSlims, (slim) => slim.FatBlock != null);
				foreach (IMySlimBlock slim in allSlims)
				{
					if (!slim.FatBlock.IsWorking)
						continue;

					double distance = Vector3D.DistanceSquared(myPosition, slim.FatBlock.GetPosition());
					if (distance < closestDistance)
					{
						closestDistance = distance;
						closest = slim.FatBlock;
					}
				}
				return closest;
			}
		}

		#endregion

		/// <summary>
		/// Get any projectile which is a threat from Available_Targets[tType].
		/// </summary>
		private bool PickAProjectile(TargetType tType)
		{
			List<IMyEntity> targetsOfType;
			if (Available_Targets.TryGetValue(tType, out targetsOfType))
			{
				//myLogger.debugLog("picking projectile of type " + tType + " from " + targetsOfType.Count, "PickAProjectile()");

				bool isFixed = myTurret == null;

				foreach (IMyEntity projectile in targetsOfType)
					if (ProjectileIsThreat(projectile, tType) && !Obstructed(projectile.GetPosition(), isFixed))
					{
						//myLogger.debugLog("Is a threat: " + projectile.getBestName(), "PickAProjectile()");
						CurrentTarget = new Target(projectile, tType);
						return true;
					}
					//else
					//	myLogger.debugLog("Not a threat: " + projectile.getBestName(), "PickAProjectile()");
			}

			return false;
		}

		/// <summary>
		/// <para>Approaching, going to intersect protection area.</para>
		/// </summary>
		private bool ProjectileIsThreat(IMyEntity projectile, TargetType tType)
		{
			if (projectile.Closed)
				return false;

			Vector3 projectileVelocity = projectile.GetLinearVelocity();
			// there are mods that add slow missiles and meteors, so ignore speed for those
			if (tType != TargetType.Missile && tType != TargetType.Meteor && projectileVelocity.LengthSquared() < 100)
			{
				//myLogger.debugLog("too slow to be a threat: " + projectile.getBestName(), "ProjectileIsThreat()");
				return false;
			}

			Vector3D projectilePosition = projectile.GetPosition();
			BoundingSphereD protectionArea = new BoundingSphereD(weapon.GetPosition(), TargetingRange / 10f);
			if (protectionArea.Contains(projectilePosition) == ContainmentType.Contains)
			{
				// too late to stop it (also, no test for moving away)
				//myLogger.debugLog("projectile is inside protection area: " + projectile.getBestName(), "ProjectileIsThreat()");
				return false;
			}

			RayD projectileRay = new RayD(projectilePosition, Vector3D.Normalize(projectileVelocity));
			if (projectileRay.Intersects(protectionArea) != null)
			{
				//myLogger.debugLog("projectile is a threat: " + projectile.getBestName(), "ProjectileIsThreat()");
				return true;
			}

			//myLogger.debugLog("projectile not headed towards protection area: " + projectile.getBestName(), "ProjectileIsThreat()");
			return false;
		}

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

			if (!CanRotateTo(targetPos))
				return true;

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

		/// <summary>
		/// Calculates FiringDirection & InterceptionPoint
		/// </summary>
		/// TODO: if target is accelerating, look ahead (missiles and such)
		private void SetFiringDirection()
		{
			IMyEntity target = CurrentTarget.Entity;
			Vector3D TargetPosition;// = target.GetPosition();

			if (target is IMyCharacter)
				// GetPosition() is near feet
				TargetPosition = target.WorldMatrix.Up * 1.25 + target.GetPosition();
			else
				TargetPosition = target.GetPosition();

			Vector3D RelativeVelocity = target.GetLinearVelocity() - weapon.GetLinearVelocity();

			//myLogger.debugLog("weapon position = " + weapon.GetPosition() + ", ammo speed = " + LoadedAmmoSpeed(TargetPosition) + ", TargetPosition = " + TargetPosition + ", target velocity = " + target.GetLinearVelocity(), "GetFiringDirection()");
			FindInterceptVector(weapon.GetPosition(), LoadedAmmoSpeed(TargetPosition), TargetPosition, RelativeVelocity);
			if (CurrentTarget.FiringDirection == null)
			{
				myLogger.debugLog("Blacklisting " + target.getBestName(), "SetFiringDirection()");
				Blacklist.Add(target.EntityId);
				return;
			}
			if (Obstructed(CurrentTarget.InterceptionPoint.Value, myTurret == null))
			{
				myLogger.debugLog("Shot path is obstructed, blacklisting " + target.getBestName(), "SetFiringDirection()");
				Blacklist.Add(target.EntityId);
				CurrentTarget = new Target();
				return;
			}
		}

		/// <remarks>From http://danikgames.com/blog/moving-target-intercept-in-3d/ </remarks>
		private void FindInterceptVector(Vector3 shotOrigin, float shotSpeed, Vector3 targetOrigin, Vector3 targetVel)
		{
			Vector3 displacementToTarget = targetOrigin - shotOrigin;
			float distanceToTarget = displacementToTarget.Length();
			Vector3 directionToTarget = displacementToTarget / distanceToTarget;

			// Decompose the target's velocity into the part parallel to the
			// direction to the cannon and the part tangential to it.
			// The part towards the cannon is found by projecting the target's
			// velocity on directionToTarget using a dot product.
			float targetSpeedOrth = Vector3.Dot(targetVel, directionToTarget);
			Vector3 targetVelOrth = targetSpeedOrth* directionToTarget;

			// The tangential part is then found by subtracting the
			// result from the target velocity.
			Vector3 targetVelTang = targetVel - targetVelOrth;

			// The tangential component of the velocities should be the same
			// (or there is no chance to hit)
			// THIS IS THE MAIN INSIGHT!
			Vector3 shotVelTang = targetVelTang;

			// Now all we have to find is the orthogonal velocity of the shot

			float shotVelSpeed = shotVelTang.Length();
			if (shotVelSpeed > shotSpeed)
			{
				//// Shot is too slow to intercept target, it will never catch up.
				//// Do our best by aiming in the direction of the targets velocity.
				//return Vector3.Normalize(targetVel) * shotSpeed;
				CurrentTarget.FiringDirection = null;
				CurrentTarget.InterceptionPoint = null;
				return;
			}
			else
			{
				// We know the shot speed, and the tangential velocity.
				// Using pythagoras we can find the orthogonal velocity.
				float shotSpeedOrth = (float)Math.Sqrt(shotSpeed * shotSpeed - shotVelSpeed * shotVelSpeed);
				Vector3 shotVelOrth = directionToTarget * shotSpeedOrth;

				// Finally, add the tangential and orthogonal velocities.
				//return shotVelOrth + shotVelTang;
				CurrentTarget.FiringDirection = Vector3.Normalize(shotVelOrth + shotVelTang);

				// Find the time of collision (distance / relative velocity)
				float timeToCollision = distanceToTarget / (shotSpeedOrth - targetSpeedOrth);

				// Calculate where the shot will be at the time of collision
				Vector3 shotVel = shotVelOrth + shotVelTang;
				CurrentTarget.InterceptionPoint = shotOrigin + shotVel * timeToCollision;
			}
		}
	}
}
