#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.Collections;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Autopilot.Weapons
{
	/// <summary>
	/// Contains functions that are common to turrets and fixed weapons
	/// </summary>
	/// TODO: fix range issues
	public abstract class WeaponTargeting
	{
		/// <summary>how close shots need to be to the target, in metres</summary>
		private const float firingThreshold = 10f;
		private static Dictionary<uint, Ammo> KnownAmmo = new Dictionary<uint, Ammo>();

		public readonly IMyCubeBlock weapon;
		public readonly Ingame.IMyLargeTurretBase myTurret;

		private Dictionary<TargetType, List<IMyEntity>> Available_Targets;
		private List<IMyEntity> PotentialObstruction;

		protected TargetingOptions Options = new TargetingOptions();
		private InterpreterWeapon Interpreter;// = new InterpreterWeapon();
		private int InterpreterErrorCount = int.MaxValue;
		private Ammo LoadedAmmo;

		protected Target CurrentTarget { get; private set; }
		private MyUniqueList<IMyEntity> Blacklist = new MyUniqueList<IMyEntity>();
		private int Blacklist_Index = 0;

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
				TargetingRangeSquared = value_TargetingRange * value_TargetingRange;
			}
		}
		protected float TargetingRangeSquared { get; private set; }

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

			this.Interpreter = new InterpreterWeapon(weapon);
			this.CurrentTarget = new Target();
			this.IsControllingWeapon = false;
		}

		/// <summary>
		/// Determines firing direction & intersection point.
		/// </summary>
		public void Update1()
		{
			if (!IsControllingWeapon || LoadedAmmo == null || (CurrentTarget.Entity != null && CurrentTarget.Entity.Closed))
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

			switch (CurrentTarget.TType)
			{
				case TargetType.Missile:
				case TargetType.Meteor:
					if (ProjectileIsThreat(CurrentTarget.Entity, CurrentTarget.TType))
					{
						myLogger.debugLog("Keeping Target = " + CurrentTarget.Entity.getBestName(), "Update10()");
						return;
					}
					goto case TargetType.None;
				case TargetType.None:
				default:
					CurrentTarget = new Target();
					break;
			}

			CollectTargets();
			PickATarget();
			if (CurrentTarget.Entity != null)
				myLogger.debugLog("Current Target = " + CurrentTarget.Entity.getBestName(), "Update10()");
		}

		/// <summary>
		/// Gets targeting options from name.
		/// </summary>
		public void Update100()
		{
			if (!IsControllingWeapon)
				return;

			TryClearBlackList();

			TargetingOptions newOptions;
			List<string> Errors;
			Interpreter.Parse(out newOptions, out Errors);
			if (Errors.Count <= InterpreterErrorCount)
			{
				//myLogger.debugLog("updating Options, Error Count = " + Errors.Count, "Update100()");
				Options = newOptions;
				InterpreterErrorCount = Errors.Count;
			}
			else
				myLogger.debugLog("not updation Options, Error Count = " + Errors.Count, "Update100()");
			WriteErrors(Errors);
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
				&& weapon.OwnerId != 0
				&& weapon.DisplayNameText.Contains("[") && weapon.DisplayNameText.Contains("]"))
			{
				if (!IsControllingWeapon)
				{
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
						//myTurret.SetTarget(myTurret);
						myTurret.SetTarget(weapon.GetPosition() + weapon.WorldMatrix.Forward * 10);
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
					Blacklist = new MyUniqueList<IMyEntity>();

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

		private void UpdateAmmo()
		{
			List<IMyInventoryItem> loaded = (weapon as IMyInventoryOwner).GetInventory(0).GetItems();
			if (loaded.Count == 0 || loaded[0].Amount < 1)
			{
				//myLogger.debugLog("No ammo loaded.", "UpdateAmmo()");
				LoadedAmmo = null;
				return;
			}

			Ammo currentAmmo;
			if (!KnownAmmo.TryGetValue(loaded[0].ItemId, out currentAmmo))
			{
				MyDefinitionId magazineId = loaded[0].Content.GetObjectId();
				//myLogger.debugLog("magazineId = " + magazineId, "UpdateAmmo()");
				MyDefinitionId ammoDefId = MyDefinitionManager.Static.GetAmmoMagazineDefinition(magazineId).AmmoDefinitionId;
				//myLogger.debugLog("ammoDefId = " + ammoDefId, "UpdateAmmo()");
				currentAmmo = new Ammo( MyDefinitionManager.Static.GetAmmoDefinition(ammoDefId));
				//myLogger.debugLog("currentAmmo = " + currentAmmo, "UpdateAmmo()");

				KnownAmmo.Add(loaded[0].ItemId, currentAmmo);
			}
			//else
			//	myLogger.debugLog("Got ammo from Dictionary: " + currentAmmo, "UpdateAmmo()");

			if (LoadedAmmo == null || LoadedAmmo != currentAmmo) // ammo has changed
			{
				myLogger.debugLog("Ammo changed to: " + currentAmmo.Definition.DisplayNameText, "UpdateAmmo()");
				LoadedAmmo = currentAmmo;
			}
		}

		private float LoadedAmmoSpeed(Vector3D target)
		{
			if (LoadedAmmo.DistanceToMaxSpeed < 1)
				return LoadedAmmo.Definition.DesiredSpeed;

			MyMissileAmmoDefinition missileAmmo = LoadedAmmo.Definition as MyMissileAmmoDefinition;
			if (missileAmmo == null)
			{
				myLogger.alwaysLog("Missile Ammo expected: " + LoadedAmmo.Definition.DisplayNameText, "LoadedAmmoSpeed()", Logger.severity.ERROR);
				return LoadedAmmo.Definition.DesiredSpeed;
			}

			float distance = Vector3.Distance(weapon.GetPosition(), target);

			if (distance < LoadedAmmo.DistanceToMaxSpeed)
			{
				float finalSpeed = (float)Math.Sqrt(missileAmmo.MissileInitialSpeed * missileAmmo.MissileInitialSpeed + 2 * missileAmmo.MissileAcceleration * distance);

				//myLogger.debugLog("close missile calc: " + ((missileAmmo.MissileInitialSpeed + finalSpeed) / 2), "LoadedAmmoSpeed()");
				return (missileAmmo.MissileInitialSpeed + finalSpeed) / 2;
			}
			else
			{
				float distanceAfterMaxVel = distance - LoadedAmmo.DistanceToMaxSpeed;
				float timeAfterMaxVel = distanceAfterMaxVel / missileAmmo.DesiredSpeed;

				//myLogger.debugLog("far missile calc: " + (distance / (LoadedAmmo.TimeToMaxSpeed + timeAfterMaxVel)), "LoadedAmmoSpeed()");
				return distance / (LoadedAmmo.TimeToMaxSpeed + timeAfterMaxVel);
			}
		}

		///// <summary>
		///// <para>If supplied value is small, fire the weapon.</para>
		///// <para>If supplied value is large, stop firing.</para>
		///// </summary>
		//protected void CheckFire(float lengthSquared)
		//{
		//	if (lengthSquared < firingThreshold)
		//	{
		//		if (!IsShooting)
		//		{
		//			// start firing
		//			myLogger.debugLog("Open fire LS: " + lengthSquared, "CheckFire()");

		//			(weapon as IMyTerminalBlock).GetActionWithName("Shoot").Apply(weapon);
		//			IsShooting = true;
		//		}
		//		//else
		//		//	myLogger.debugLog("Keep firing LS: " + lengthSquared, "CheckFire()");
		//	}
		//	else
		//	{
		//		if (IsShooting)
		//		{
		//			// stop firing
		//			myLogger.debugLog("Hold fire LS: " + lengthSquared, "CheckFire()");

		//			(weapon as IMyTerminalBlock).GetActionWithName("Shoot").Apply(weapon);
		//			IsShooting = false;
		//		}
		//		//else
		//		//	myLogger.debugLog("Continue holding LS: " + lengthSquared, "CheckFire()");
		//	}
		//}

		/// <summary>
		/// <para>If the direction will put shots on target, fire the weapon.</para>
		/// <para>If the direction will miss the target, stop firing.</para>
		/// </summary>
		/// <param name="direction">The direction the weapon is pointing in.</param>
		protected void CheckFire(Vector3 direction)
		{
			Vector3 weaponPosition = weapon.GetPosition();
			Vector3 finalPosition = weaponPosition + Vector3.Normalize( direction) * TargetingRange;
			Line shot = new Line(weaponPosition, finalPosition, false);

			//myLogger.debugLog("shot is from " + weaponPosition + " to " + finalPosition + ", target is at " + CurrentTarget.InterceptionPoint.Value, "CheckFire()");
			//myLogger.debugLog("100 m out: " + (weaponPosition + Vector3.Normalize(direction) * 100), "CheckFire()");
			//myLogger.debugLog("distance between weapon and target is " + Vector3.Distance(weaponPosition, CurrentTarget.InterceptionPoint.Value) + ", distance between finalPosition and target is " + Vector3.Distance(finalPosition, CurrentTarget.InterceptionPoint.Value), "CheckFire()");
			//myLogger.debugLog("distance between shot and target is " + shot.Distance(CurrentTarget.InterceptionPoint.Value), "CheckFire()");

			if (shot.DistanceLessEqual(CurrentTarget.InterceptionPoint.Value, firingThreshold))
				FireWeapon();
			else
				StopFiring();
		}

		private void FireWeapon()
		{
			if (IsShooting)
				return;

			myLogger.debugLog("Open fire", "CheckFire()");

			(weapon as IMyTerminalBlock).GetActionWithName("Shoot").Apply(weapon);
			IsShooting = true;
		}

		protected void StopFiring()
		{
			if (!IsShooting)
				return;

			myLogger.debugLog("Hold fire", "CheckFire()");

			(weapon as IMyTerminalBlock).GetActionWithName("Shoot").Apply(weapon);
			IsShooting = false;
		}

		/// <summary>
		/// Fills Available_Targets and PotentialObstruction
		/// </summary>
		private void CollectTargets()
		{
			//myLogger.debugLog("Entered CollectTargets", "CollectTargets()");

			Available_Targets = new Dictionary<TargetType, List<IMyEntity>>();
			PotentialObstruction = new List<IMyEntity>();

			BoundingSphereD nearbySphere = new BoundingSphereD(weapon.GetPosition(), TargetingRange);
			HashSet<IMyEntity> nearbyEntities = new HashSet<IMyEntity>();
			MyAPIGateway.Entities.GetEntitiesInSphere_Safe_NoBlock(nearbySphere, nearbyEntities);

			//myLogger.debugLog("found " + nearbyEntities.Count + " entities", "CollectTargets()");

			foreach (IMyEntity entity in nearbyEntities)
			{
				//myLogger.debugLog("Nearby entity: " + entity.getBestName(), "CollectTargets()");

				if (Blacklist.Contains(entity))
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
					IMyIdentity asIdentity = asChar.GetIdentity_Safe();
					if (asIdentity != null)
					{
						if (asIdentity.IsDead)
						{
							myLogger.debugLog("(s)he's dead, jim: " + entity.getBestName(), "CollectTargets()");
							continue;
						}
					}
					else
						myLogger.debugLog("Found a robot! : " + asChar + " . " + entity.getBestName(), "CollectTargets()");

					if (asIdentity == null || weapon.canConsiderHostile(asIdentity.PlayerId))
					{
						//myLogger.debugLog("Hostile Character(" + asIdentity + "): " + entity.getBestName(), "CollectTargets()");
						AddTarget(TargetType.Character, entity);
					}
					else
					{
						//myLogger.debugLog("Non-Hostile Character: " + entity.getBestName(), "CollectTargets()");
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
						AddTarget(TargetType.Destroy, entity);
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
			if (!Options.CanTargetType(tType))
			{
				//if (tType == TargetType.Destroy)
				//myLogger.debugLog("Cannot add type: " + tType, "AddTarget()");
				return;
			}

			//if (tType == TargetType.Moving)
			//	myLogger.debugLog("Adding type: " + tType + ", target = " + target.getBestName(), "AddTarget()");

			List<IMyEntity> list;
			if (!Available_Targets.TryGetValue(tType, out list))
			{
				list = new List<IMyEntity>();
				Available_Targets.Add(tType, list);
			}
			list.Add(target);
		}

		/// <summary>
		/// <para>Choose a target from Available_Targets.</para>
		/// </summary>
		private void PickATarget()
		{
			if (PickAProjectile(TargetType.Missile) || PickAProjectile(TargetType.Meteor) || PickAProjectile(TargetType.Moving))
				return;

			double closerThan = double.MaxValue;
			if (SetClosest(TargetType.Character, ref closerThan))
				return;

			// do not short for grid test
			SetClosest(TargetType.LargeGrid, ref closerThan);
			SetClosest(TargetType.SmallGrid, ref closerThan);
			SetClosest(TargetType.Station, ref closerThan);

			// if weapon does not have a target yet, check for destroy
			if (CurrentTarget.TType == TargetType.None)
			//{
			//	myLogger.debugLog("No targets yet, checking for destroy", "PickATarget()");
				SetClosest(TargetType.Destroy, ref closerThan);
			//}
		}

		/// <summary>
		/// Get the closest target of the specified type from Available_Targets[tType].
		/// </summary>
		private bool SetClosest(TargetType tType, ref double closerThan)
		{
			List<IMyEntity> targetsOfType;
			if (!Available_Targets.TryGetValue(tType, out targetsOfType))
				return false;

			IMyEntity closest = null;

			Vector3D weaponPosition = weapon.GetPosition();
			bool isFixed = myTurret == null;

			foreach (IMyEntity entity in targetsOfType)
			{
				//if (tType == TargetType.Destroy)
				//	myLogger.debugLog("Destroy, Grid = " + target.getBestName(), "SetClosest()");

				if (entity.Closed)
					continue;

				IMyEntity target;
				Vector3D targetPosition;
				double distanceValue;

				// get block from grid before obstruction test
				IMyCubeGrid asGrid = entity as IMyCubeGrid;
				if (asGrid != null)
				{
					IMyCubeBlock targetBlock;
					if (GetTargetBlock(asGrid, tType, out targetBlock, out distanceValue))
						target = targetBlock;
					else
						continue;
					targetPosition = target.GetPosition();
				}
				else
				{
					target = entity;
					targetPosition = target.GetPosition();

					distanceValue = Vector3D.DistanceSquared(targetPosition, weaponPosition);
					if (distanceValue > TargetingRangeSquared)
					{
						myLogger.debugLog("for type: " + tType + ", too far to target: " + target.getBestName(), "SetClosest()");
						continue;
					}

					if (Obstructed(targetPosition))//, isFixed))
						continue;
				}

				if (distanceValue < closerThan)
				{
					closest = target;
					closerThan = distanceValue;
				}
			}

			if (closest != null)
			{
				CurrentTarget = new Target(closest, tType);
				return true;
			}
			return false;
		}

		/// <remarks>
		/// <para>Targeting non-terminal blocks would cause confusion.</para>
		/// <para>Open doors should not be targeted.</para>
		/// </remarks>
		private bool TargetableBlock(IMyCubeBlock block)
		{
			if (!(block is IMyTerminalBlock))
				return false;

			IMyDoor asDoor = block as IMyDoor;
			return asDoor == null || asDoor.OpenRatio < 0.01;
		}

		/// <summary>
		/// Gets the best block to target from a grid.
		/// </summary>
		/// <param name="grid">The grid to search</param>
		/// <param name="tType">Checked for destroy</param>
		/// <param name="target">The best block fromt the grid</param>
		/// <param name="distanceValue">The value assigned based on distance and position in blocksToTarget.</param>
		/// <remarks>
		/// <para>Decoy blocks will be given a distanceValue of the distance squared to weapon.</para>
		/// <para>Blocks from blocksToTarget will be given a distanceValue of the distance squared * (2 + index)^2.</para>
		/// <para>Other blocks will be given a distanceValue of the distance squared * (1e12).</para>
		/// </remarks>
		private bool GetTargetBlock(IMyCubeGrid grid, TargetType tType, out IMyCubeBlock target, out double distanceValue)
		{
			Vector3D myPosition = weapon.GetPosition();
			CubeGridCache cache = CubeGridCache.GetFor(grid);

			target = null;
			distanceValue = TargetingRangeSquared;

			if (cache.TotalByDefinition() == 0)
			{
				myLogger.debugLog("no terminal blocks on grid: " + grid.DisplayName, "GetTargetBlock()");
				return false;
			}

			// get decoy block
			{
				var decoyBlockList = cache.GetBlocksOfType(typeof(MyObjectBuilder_Decoy));
				if (decoyBlockList != null)
					foreach (Ingame.IMyTerminalBlock block in decoyBlockList)
					{
						if (!block.IsWorking)
							continue;

						if (Blacklist.Contains(block))
							continue;

						double distanceSq = Vector3D.DistanceSquared(myPosition, block.GetPosition());
						if (distanceSq > TargetingRangeSquared)
							continue;

						//myLogger.debugLog("decoy search, block = " + block.DisplayNameText + ", distance = " + distance, "GetTargetBlock()");

						if (distanceSq < distanceValue)
						{
							target = block as IMyCubeBlock;
							distanceValue = distanceSq;
						}
					}
				if (target != null)
				{
					myLogger.debugLog("for type = " + tType + " and grid = " + grid.DisplayName + ", found a decoy block: " + target.DisplayNameText + ", distanceValue: " + distanceValue, "GetTargetBlock()");
					return true;
				}
			}

			// get block from blocksToTarget
			int multiplier = 1;
			foreach (string blocksSearch in Options.blocksToTarget)
			{
				multiplier++;
				var master = cache.GetBlocksByDefLooseContains(blocksSearch);
				foreach (var blocksWithDef in master)
					foreach (IMyCubeBlock block in blocksWithDef)
					{
						if (!block.IsWorking || !TargetableBlock(block))
							continue;

						if (Blacklist.Contains(block))
							continue;

						double distanceSq = Vector3D.DistanceSquared(myPosition, block.GetPosition());
						if (distanceSq > TargetingRangeSquared)
							continue;
						distanceSq *= multiplier * multiplier;

						//myLogger.debugLog("blocksSearch = " + blocksSearch + ", block = " + block.DisplayNameText + ", distance = " + distance, "GetTargetBlock()");
						if (distanceSq < distanceValue)
						{
							target = block;
							distanceValue = distanceSq;
						}
					}
				if (target != null) // found a block from blocksToTarget
				{
					myLogger.debugLog("for type = " + tType + " and grid = " + grid.DisplayName + ", blocksSearch = " + blocksSearch + ", target = " + target.DisplayNameText + ", distanceValue = " + distanceValue, "GetTargetBlock()"); 
					return true;
				}
			}

			// get any terminal block
			if (tType == TargetType.Moving || tType == TargetType.Destroy)
			{
				List<IMySlimBlock> allSlims = new List<IMySlimBlock>();
				grid.GetBlocks_Safe(allSlims, (slim) => slim.FatBlock != null);

				foreach (IMySlimBlock slim in allSlims)
					if (TargetableBlock(slim.FatBlock))
					{
						if (Blacklist.Contains(slim.FatBlock))
							continue;

						double distanceSq = Vector3D.DistanceSquared(myPosition, slim.FatBlock.GetPosition());
						if (distanceSq > TargetingRangeSquared)
							continue;
						distanceSq *= 1e12;

						target = slim.FatBlock;
						distanceValue = distanceSq;
						myLogger.debugLog("for type = " + tType + " and grid = " + grid.DisplayName + ", found a block: " + target.DisplayNameText + ", distanceValue = " + distanceValue, "GetTargetBlock()");
						return true;
					}
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
				//myLogger.debugLog("picking projectile of type " + tType + " from " + targetsOfType.Count, "PickAProjectile()");

				bool isFixed = myTurret == null;

				foreach (IMyEntity entity in targetsOfType)
				{
					if (entity.Closed)
						continue;

					// meteors and missiles are dangerous even if they are slow
					if (!(entity is IMyMeteor || entity.ToString().StartsWith("MyMissile") || entity.GetLinearVelocity().LengthSquared() > 100))
					{
						//myLogger.debugLog("type = " + tType + ", entity = " + entity.getBestName() + " is too slow, speed = " + entity.GetLinearVelocity().Length(), "PickAProjectile()");
						continue;
					}

					IMyEntity projectile = entity;

					IMyCubeGrid asGrid = projectile as IMyCubeGrid;
					if (asGrid != null)
					{
						IMyCubeBlock targetBlock;
						double distanceValue;
						if (GetTargetBlock(asGrid, tType, out targetBlock, out distanceValue))
							projectile = targetBlock;
						else
						{
							myLogger.debugLog("failed to get a block from: " + asGrid.DisplayName, "PickAProjectile()");
							continue;
						}
					}

					if (ProjectileIsThreat(projectile, tType) && !Obstructed(projectile.GetPosition()))//, isFixed))
					{
						myLogger.debugLog("Is a threat: " + projectile.getBestName(), "PickAProjectile()");
						CurrentTarget = new Target(projectile, tType);
						return true;
					}
					else
						myLogger.debugLog("Not a threat: " + projectile.getBestName(), "PickAProjectile()");
				}
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

			Vector3D projectilePosition = projectile.GetPosition();
			BoundingSphereD ignoreArea = new BoundingSphereD(weapon.GetPosition(), TargetingRange / 10f);
			if (ignoreArea.Contains(projectilePosition) == ContainmentType.Contains)
			{
				//myLogger.debugLog("projectile is inside ignore area: " + projectile.getBestName(), "ProjectileIsThreat()");
				return false;
			}

			Vector3D weaponPosition = weapon.GetPosition();
			Vector3D nextPosition = projectilePosition + projectile.GetLinearVelocity() / 60f;
			if (Vector3D.DistanceSquared(weaponPosition, nextPosition) < Vector3D.DistanceSquared(weaponPosition, projectilePosition))
			{
				myLogger.debugLog("projectile: " + projectile.getBestName() + ", is moving towards weapon. D0 = " + Vector3D.DistanceSquared(weaponPosition, nextPosition) + ", D1 = " + Vector3D.DistanceSquared(weaponPosition, projectilePosition), "ProjectileIsThreat()");
				return true;
			}
			else
			{
				myLogger.debugLog("projectile: " + projectile.getBestName() + ", is moving away from weapon. D0 = " + Vector3D.DistanceSquared(weaponPosition, nextPosition) + ", D1 = " + Vector3D.DistanceSquared(weaponPosition, projectilePosition), "ProjectileIsThreat()");
				return false;
			}

			//RayD projectileRay = new RayD(projectilePosition, Vector3D.Normalize(projectile.GetLinearVelocity()));
			//double? Intersects = projectileRay.Intersects(ignoreArea);
			//if (Intersects != null && Intersects > 0)
			//{
			//	myLogger.debugLog("projectile is a threat: " + projectile.getBestName(), "ProjectileIsThreat()");
			//	return true;
			//}

			//myLogger.debugLog("projectile not headed towards protection area: " + projectile.getBestName(), "ProjectileIsThreat()");
			//return false;
		}

		/// <summary>
		/// <para>Test line segment between weapon and target for obstructing entities.</para>
		/// <para>Tests for obstructing voxel map, non-hostile character, or non-hostile grid.</para>
		/// </summary>
		/// <param name="targetPos">entity to shoot</param>
		/// <param name="ignoreSourceGrid">ignore intersections with grid that weapon is part of</param>
		private bool Obstructed(Vector3D targetPos)//, bool readyToFire = false)
		{
			if (weapon == null)
				throw new ArgumentNullException("weapon");

			if (!CanRotateTo(targetPos))
				return true;

			bool ignoreSourceGrid = myTurret == null;

			Vector3D weaponPos = weapon.GetPosition();

			// Voxel Test
			Vector3 boundary;
			if (MyAPIGateway.Entities.RayCastVoxel_Safe(weaponPos, targetPos, out boundary))
				return true;

			LineD laser = new LineD(weaponPos, targetPos);
			//Vector3I position = new Vector3I();
			double distance;

			// Test each entity
			foreach (IMyEntity entity in PotentialObstruction)
			{
				if (entity.Closed)
					continue;

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

			Vector3 TargetVelocity = target.GetLinearVelocity();

			Vector3D RelativeVelocity = TargetVelocity - weapon.GetLinearVelocity();

			//myLogger.debugLog("weapon position = " + weapon.GetPosition() + ", ammo speed = " + LoadedAmmoSpeed(TargetPosition) + ", TargetPosition = " + TargetPosition + ", target velocity = " + target.GetLinearVelocity(), "GetFiringDirection()");
			FindInterceptVector(weapon.GetPosition(), LoadedAmmoSpeed(TargetPosition), TargetPosition, RelativeVelocity);
			if (CurrentTarget.FiringDirection == null)
			{
				myLogger.debugLog("Blacklisting " + target.getBestName(), "SetFiringDirection()");
				Blacklist.Add(target);
				return;
			}
			if (Obstructed(CurrentTarget.InterceptionPoint.Value))//, myTurret == null))
			{
				myLogger.debugLog("Shot path is obstructed, blacklisting " + target.getBestName(), "SetFiringDirection()");
				Blacklist.Add(target);
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
			Vector3 targetVelOrth = targetSpeedOrth * directionToTarget;

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

		/// <summary>
		/// Write errors to weapon, using angle brackets.
		/// </summary>
		private void WriteErrors(List<string> Errors)
		{
			string DisplayName = weapon.DisplayNameText;
			//myLogger.debugLog("initial name: " + DisplayName, "WriteErrors()");
			int start = DisplayName.IndexOf('>') + 1;
			if (start > 0)
				DisplayName = DisplayName.Substring(start);

			//myLogger.debugLog("chopped name: " + DisplayName, "WriteErrors()");

			StringBuilder build = new StringBuilder();
			if (Errors.Count > 0)
			{
				build.Append("<ERROR(");
				for (int index = 0; index < Errors.Count; index++)
				{
					//myLogger.debugLog("Error: " + Errors[index], "WriteErrors()");
					build.Append(Errors[index]);
					if (index + 1 < Errors.Count)
						build.Append(',');
				}
				build.Append(")>");
				build.Append(DisplayName);

				//myLogger.debugLog("New name: " + build, "WriteErrors()");
				(weapon as IMyTerminalBlock).SetCustomName(build);
			}
			else
				(weapon as IMyTerminalBlock).SetCustomName(DisplayName);
		}

		/// <summary>
		/// attempts to remove some blacklisted items by checking for obstructions
		/// </summary>
		private void TryClearBlackList()
		{
			bool FixedGun = myTurret ==null;
			int i;
			for (i = 0; i < 10; i++)
			{
				int index = Blacklist_Index + i;
				if (index >= Blacklist.Count)
				{
					Blacklist_Index = 0;
					return;
				}
				IMyEntity entity = Blacklist[index];
				if (entity.Closed || !Obstructed(entity.GetPosition()))//, FixedGun))
				{
					myLogger.debugLog("removing from blacklist: " + entity.getBestName(), "TryClearBlackList()");
					Blacklist.Remove(entity);
				}
				else
					myLogger.debugLog("leaving in blacklist: " + entity.getBestName(), "TryClearBlackList()");
			}
			Blacklist_Index += i;
		}
	}
}
