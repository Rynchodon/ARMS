#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.Collections;
using VRage.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Weapons
{
	/// <summary>
	/// Contains functions that are common to turrets and fixed weapons
	/// </summary>
	public abstract class WeaponTargeting
	{
		[Flags]
		public enum State : byte
		{
			Off = 0,
			/// <summary>Fetch options from name and Update_Options()</summary>
			GetOptions = 1 << 0,
			/// <summary>Indicates targeting is enabled, targeting may not be possible</summary>
			Targeting = GetOptions | 1 << 1
		}

		private static readonly ThreadManager Thread = new ThreadManager();
		private static readonly List<Vector3> obstructionOffsets_turret = new List<Vector3>();
		private static readonly List<Vector3> obstructionOffsets_fixed = new List<Vector3>();

		/// <remarks>Not locked because there is only one thread allowed.</remarks>
		private static Dictionary<string, Ammo> KnownAmmo = new Dictionary<string, Ammo>();

		public readonly IMyCubeBlock CubeBlock;
		public readonly Ingame.IMyLargeTurretBase myTurret;

		public Target CurrentTarget { get; private set; }
		public TargetingOptions Options { get; private set; }
		public bool CanControl { get; private set; }

		/// <remarks>Simple turrets can potentially shoot their own grids so they must be treated differently</remarks>
		private readonly bool IsNormalTurret;
		private readonly FastResourceLock lock_Queued = new FastResourceLock();

		private Logger myLogger;
		private State value_AllowedState = State.Off;
		private State value_CurrentState = State.Off;
		private Dictionary<TargetType, List<IMyEntity>> Available_Targets;
		private List<IMyEntity> PotentialObstruction;
		private Ammo LoadedAmmo;
		private long UpdateNumber = 0;

		private InterpreterWeapon Interpreter;
		private int InterpreterErrorCount = int.MaxValue;

		private MyUniqueList<IMyEntity> Blacklist = new MyUniqueList<IMyEntity>();
		//private readonly FastResourceLock lock_Blacklist = new FastResourceLock(); // probably do not need this

		/// <summary>Tests whether or not WeaponTargeting has set the turret to shoot.</summary>
		/// <remarks>need to lock IsShooting because StopFiring() can be called at any time</remarks>
		private bool IsShooting;
		/// <remarks>need to lock IsShooting because StopFiring() can be called at any time</remarks>
		private readonly FastResourceLock lock_IsShooting = new FastResourceLock();

		private List<IMyEntity> value_ObstructIgnore;
		private readonly FastResourceLock lock_ObstructIgnore = new FastResourceLock();

		private LockedQueue<Action> GameThreadActions = new LockedQueue<Action>(1);

		static WeaponTargeting()
		{
			obstructionOffsets_turret.Add(new Vector3(0, -1.25f, 0));
			obstructionOffsets_turret.Add(new Vector3(2.5f, 5f, 2.5f));
			obstructionOffsets_turret.Add(new Vector3(2.5f, 5f, -2.5f));
			obstructionOffsets_turret.Add(new Vector3(-2.5f, 5f, 2.5f));
			obstructionOffsets_turret.Add(new Vector3(-2.5f, 5f, -2.5f));

			obstructionOffsets_fixed.Add(new Vector3(0, 0, 0));
			obstructionOffsets_fixed.Add(new Vector3(-2.5f, -2.5f, 0));
			obstructionOffsets_fixed.Add(new Vector3(-2.5f, 2.5f, 0));
			obstructionOffsets_fixed.Add(new Vector3(2.5f, -2.5f, 0));
			obstructionOffsets_fixed.Add(new Vector3(2.5f, 2.5f, 0));
		}

		public WeaponTargeting(IMyCubeBlock weapon)
		{
			if (weapon == null)
				throw new ArgumentNullException("weapon");
			if (!(weapon is IMyTerminalBlock) || !(weapon is IMyFunctionalBlock) || !(weapon is IMyInventoryOwner) || !(weapon is Ingame.IMyUserControllableGun))
				throw new ArgumentException("weapon(" + weapon.DefinitionDisplayNameText + ") is not of correct type");

			this.CubeBlock = weapon;
			this.myTurret = weapon as Ingame.IMyLargeTurretBase;
			this.myLogger = new Logger("WeaponTargeting", weapon);

			this.Interpreter = new InterpreterWeapon(weapon);
			this.CurrentTarget = new Target();
			this.Options = new TargetingOptions();
			this.IsNormalTurret = myTurret != null;
			this.CubeBlock.OnClose += weapon_OnClose;
		}

		private void weapon_OnClose(IMyEntity obj)
		{
			myLogger.debugLog("entered weapon_OnClose()", "weapon_OnClose()");

			CubeBlock.OnClose -= weapon_OnClose;
			value_AllowedState = State.Off;
			value_CurrentState = State.Off;

			myLogger.debugLog("leaving weapon_OnClose()", "weapon_OnClose()");
		}

		/// <summary>
		/// UpdateManger invokes this every update.
		/// </summary>
		public void Update_Targeting()
		{
			try
			{
				GameThreadActions.DequeueAll(action => action.Invoke());
				Update();
			}
			catch (Exception ex)
			{
				myLogger.alwaysLog("Exception: " + ex, "Update_Targeting()", Logger.severity.ERROR);
				AllowedState = State.Off;

				IMyFunctionalBlock func = CubeBlock as IMyFunctionalBlock;
				func.SetCustomName("<Broken>" + func.DisplayNameText);
				func.RequestEnable(false);
			}

			if (AllowedState != State.Off && lock_Queued.TryAcquireExclusive())
				Thread.EnqueueAction(Update_Thread);
		}

		protected State AllowedState
		{
			get { return value_AllowedState; }
			set
			{
				value_AllowedState = value;

				CurrentState &= value;
				StopFiring("AllowedState changed");

				//if (IsNormalTurret && (value & State.Targeting) == 0)
				//	GameThreadActions.Enqueue(() => myTurret.ResetTargetingToDefault());
			}
		}

		protected State CurrentState
		{
			get { return value_CurrentState; }
			private set
			{
				if (value_CurrentState == value)
					return;

				// need to get builder because we have no idea what the player may have been up to
				var builder = CubeBlock.GetSlimObjectBuilder_Safe() as MyObjectBuilder_UserControllableGun;
				using (lock_IsShooting.AcquireExclusiveUsing())
					if (IsShooting != builder.IsShootingFromTerminal)
					{
						myLogger.debugLog("switching IsShooting: player was up to something fishy", "set_CurrentState()", Logger.severity.INFO);
						IsShooting = builder.IsShootingFromTerminal;
					}

				myLogger.debugLog("CurrentState changed to " + value, "set_CurrentState()", Logger.severity.DEBUG);
				StopFiring("CurrentState changed to " + value);

				if (IsNormalTurret)
				{
					if ((value & State.Targeting) == State.Targeting) // now targeting
						GameThreadActions.Enqueue(() =>	myTurret.SetTarget(BarrelPositionWorld() + CubeBlock.WorldMatrix.Forward * 10));	// disable default targeting
					else // not targeting
						GameThreadActions.Enqueue(() =>	myTurret.ResetTargetingToDefault());
				}

				value_CurrentState = value;
			}
		}

		protected List<IMyEntity> ObstructionIgnore
		{
			private get
			{
				using (lock_ObstructIgnore.AcquireSharedUsing())
					return value_ObstructIgnore;
			}
			set
			{
				using (lock_ObstructIgnore.AcquireExclusiveUsing())
					value_ObstructIgnore = value;
			}
		}

		public bool CurrentState_FlagSet(State flag)
		{ return (CurrentState & flag) == flag; }

		public bool CurrentState_NotFlag(State flag)
		{ return (CurrentState & flag) != flag; }

		/// <summary>Invoked on game thread, every update.</summary>
		protected abstract void Update();

		/// <summary>Invoked on targeting thread, every 100 updates.</summary>
		protected abstract void Update_Options(TargetingOptions current);

		/// <summary>
		/// Used to apply restrictions on rotation, such as min/max elevation/azimuth.
		/// </summary>
		/// <param name="targetPoint">The point of the target.</param>
		/// <returns>true if the rotation is allowed</returns>
		/// <remarks>Invoked on targeting thread.</remarks>
		protected abstract bool CanRotateTo(Vector3D targetPoint);

		/// <summary>
		/// Invoked on targeting thread
		/// </summary>
		private void Update_Thread()
		{
			try
			{
				lock_Queued.ReleaseExclusive();
				if (UpdateNumber % 10 == 0)
				{
					if (UpdateNumber % 100 == 0)
					{
						//if (UpdateNumber % 1000 == 0)
						//	Update1000();
						Update100();
					}
					Update10();
				}
				Update1();

				UpdateNumber++;
			}
			catch (Exception ex)
			{				myLogger.alwaysLog("Exception: " + ex, "Update_Thread()", Logger.severity.WARNING);			}
		}

		/// <summary>
		/// Determines firing direction & intersection point.
		/// </summary>
		private void Update1()
		{
			if (CurrentState_NotFlag(State.Targeting) || LoadedAmmo == null || (CurrentTarget.Entity != null && CurrentTarget.Entity.Closed))
				return;

			if (CurrentTarget.TType != TargetType.None)
				SetFiringDirection();

			CheckFire();
		}

		/// <summary>
		/// Updates can control weapon. Checks for ammo and chooses a target (if necessary).
		/// </summary>
		private void Update10()
		{
			if (CurrentState_NotFlag(State.Targeting))
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
		private void Update100()
		{
			UpdateCurrentState();
			if (CurrentState_NotFlag(State.GetOptions))
				return;

			//using (lock_Blacklist.AcquireExclusiveUsing())
				Blacklist = new MyUniqueList<IMyEntity>();

			TargetingOptions newOptions;
			List<string> Errors;
			Interpreter.Parse(out newOptions, out Errors);
			if (Errors.Count <= InterpreterErrorCount)
			{
				Options = newOptions;
				InterpreterErrorCount = Errors.Count;
				Update_Options(Options);
				myLogger.debugLog("updating Options, Error Count = " + Errors.Count + ", Options: " + Options, "Update100()");
			}
			else
				myLogger.debugLog("not updation Options, Error Count = " + Errors.Count, "Update100()");
			WriteErrors(Errors);
		}

		// no longer appropriate as shooting toggle is delayed
		///// <summary>Verifies that the weapon is in correct firing state.</summary>
		//private void Update1000()
		//{
		//	if (CurrentState_NotFlag(State.Targeting))
		//		return;

		//	var builder = CubeBlock.GetSlimObjectBuilder_Safe() as MyObjectBuilder_UserControllableGun;
		//	using (lock_IsShooting.AcquireExclusiveUsing())
		//		if (IsShooting != builder.IsShootingFromTerminal)
		//		{
		//			myLogger.debugLog("Shooting toggled incorrectly", "Update1000()", Logger.severity.WARNING);
		//			IsShooting = builder.IsShootingFromTerminal;
		//		}
		//}

		private Vector3D BarrelPositionWorld()
		{
			return CubeBlock.GetPosition();
		}

		private void UpdateCurrentState()
		{
			if (!CubeBlock.IsWorking
			|| (IsNormalTurret && myTurret.IsUnderControl)
			|| CubeBlock.OwnerId == 0
			|| (CubeBlock.OwnedNPC() && !InterpreterWeapon.allowedNPC)
			|| (!CubeBlock.DisplayNameText.Contains("[") || !CubeBlock.DisplayNameText.Contains("]"))
			|| (!IsNormalTurret && CubeBlock.CubeGrid.IsStatic))
			{
				myLogger.debugLog(!CubeBlock.IsWorking + ", " + (IsNormalTurret && myTurret.IsUnderControl) + ", " + (CubeBlock.OwnerId == 0) + ", " + (CubeBlock.OwnedNPC() && !InterpreterWeapon.allowedNPC) + ", "
					+ (!CubeBlock.DisplayNameText.Contains("[") || !CubeBlock.DisplayNameText.Contains("]")) + ", " + (!IsNormalTurret && CubeBlock.CubeGrid.IsStatic), "UpdateCurrentState()");

				CanControl = false;
				CurrentState = State.Off;
				return;
			}

			CanControl = true;
			CurrentState = AllowedState;
		}

		private void UpdateAmmo()
		{
			List<IMyInventoryItem> loaded = (CubeBlock as IMyInventoryOwner).GetInventory(0).GetItems();
			if (loaded.Count == 0 || loaded[0].Amount < 1)
			{
				LoadedAmmo = null;
				StopFiring("No ammo loaded.");
				return;
			}

			//myLogger.debugLog("loaded = " + loaded[0] + ", type = " + loaded[0].GetType() + ", content = " + loaded[0].Content + ", TypeId = " + loaded[0].Content.TypeId, "UpdateAmmo()");

			Ammo currentAmmo;
			if (!KnownAmmo.TryGetValue(loaded[0].Content.SubtypeName, out currentAmmo))
			{
				MyDefinitionId magazineId = loaded[0].Content.GetId(); //.GetObjectId();
				//myLogger.debugLog("magazineId = " + magazineId, "UpdateAmmo()");
				MyDefinitionId ammoDefId = MyDefinitionManager.Static.GetAmmoMagazineDefinition(magazineId).AmmoDefinitionId;
				//myLogger.debugLog("ammoDefId = " + ammoDefId, "UpdateAmmo()");
				currentAmmo = new Ammo(MyDefinitionManager.Static.GetAmmoDefinition(ammoDefId));
				//myLogger.debugLog("new ammo = " + currentAmmo.Definition + ", ammo ItemId = " + loaded[0].Content.GetObjectId(), "UpdateAmmo()");

				KnownAmmo.Add(loaded[0].Content.SubtypeName, currentAmmo);
			}
			//else
			//	myLogger.debugLog("Got ammo from Dictionary: " + currentAmmo.Definition + ", ammo ItemId = " + loaded[0].Content.GetObjectId(), "UpdateAmmo()");

			if (LoadedAmmo == null || LoadedAmmo != currentAmmo) // ammo has changed
			{
				myLogger.debugLog("Ammo changed to: " + currentAmmo.Definition, "UpdateAmmo()");
				LoadedAmmo = currentAmmo;
			}
		}

		private float LoadedAmmoSpeed(Vector3D target)
		{
			if (LoadedAmmo.DistanceToMaxSpeed < 1)
			{
				myLogger.debugLog("DesiredSpeed = " + LoadedAmmo.Definition.DesiredSpeed, "LoadedAmmoSpeed()");
				return LoadedAmmo.Definition.DesiredSpeed;
			}

			MyMissileAmmoDefinition missileAmmo = LoadedAmmo.Definition as MyMissileAmmoDefinition;
			if (missileAmmo == null)
			{
				myLogger.alwaysLog("Missile Ammo expected: " + LoadedAmmo.Definition.DisplayNameText, "LoadedAmmoSpeed()", Logger.severity.ERROR);
				return LoadedAmmo.Definition.DesiredSpeed;
			}

			float distance = Vector3.Distance(BarrelPositionWorld(), target);

			myLogger.debugLog("distance = " + distance + ", DistanceToMaxSpeed = " + LoadedAmmo.DistanceToMaxSpeed, "LoadedAmmoSpeed()");
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

				myLogger.debugLog("DistanceToMaxSpeed = " + LoadedAmmo.DistanceToMaxSpeed + ", TimeToMaxSpeed = " + LoadedAmmo.TimeToMaxSpeed + ", distanceAfterMaxVel = " + distanceAfterMaxVel + ", timeAfterMaxVel = " + timeAfterMaxVel
					+ ", average speed = " + (distance / (LoadedAmmo.TimeToMaxSpeed + timeAfterMaxVel)), "LoadedAmmoSpeed()");
				//myLogger.debugLog("far missile calc: " + (distance / (LoadedAmmo.TimeToMaxSpeed + timeAfterMaxVel)), "LoadedAmmoSpeed()");
				return distance / (LoadedAmmo.TimeToMaxSpeed + timeAfterMaxVel);
			}
		}

		private Vector3 CurrentDirection;
		private readonly FastResourceLock lock_CurrentDirection = new FastResourceLock();
		private Vector3 previousFiringDirection;

		/// <summary>
		/// <para>If the direction will put shots on target, fire the weapon.</para>
		/// <para>If the direction will miss the target, stop firing.</para>
		/// </summary>
		/// <param name="direction">The direction the weapon is pointing in. Must be normalized.</param>
		protected void CheckFire(Vector3 direction)
		{
			using (lock_CurrentDirection.AcquireExclusiveUsing())
				CurrentDirection = direction;
		}

		private void CheckFire()
		{
			if (!CurrentTarget.InterceptionPoint.HasValue)
			{
				StopFiring("No interception point.");
				return;
			}

			Vector3 weaponPosition = BarrelPositionWorld();

			//float distance = Vector3.Distance(weaponPosition, CurrentTarget.InterceptionPoint.Value); // check for obstructions between weapon and target
			float distance = LoadedAmmo.Definition.MaxTrajectory; // test for obstructions between weapon and max range of weapon

			Vector3 finalPosition;
			Line shot;
			float speed;
			using (lock_CurrentDirection.AcquireSharedUsing())
			{
				finalPosition = weaponPosition + CurrentDirection * distance;
				shot = new Line(weaponPosition, finalPosition, false);

				//myLogger.debugLog("final position = " + finalPosition + ", weaponPosition = " + weaponPosition + ", direction = " + Vector3.Normalize(direction) + ", distanceToTarget = " + distanceToTarget, "CheckFire()");
				//myLogger.debugLog("shot is from " + weaponPosition + " to " + finalPosition + ", target is at " + CurrentTarget.InterceptionPoint.Value + ", distance to target = " + distanceToTarget, "CheckFire()");
				//myLogger.debugLog("100 m out: " + (weaponPosition + Vector3.Normalize(direction) * 100), "CheckFire()");
				//myLogger.debugLog("distance between weapon and target is " + Vector3.Distance(weaponPosition, CurrentTarget.InterceptionPoint.Value) + ", distance between finalPosition and target is " + Vector3.Distance(finalPosition, CurrentTarget.InterceptionPoint.Value), "CheckFire()");
				//myLogger.debugLog("distance between shot and target is " + shot.Distance(CurrentTarget.InterceptionPoint.Value), "CheckFire()");

				speed = Vector3.RectangularDistance(ref CurrentDirection, ref previousFiringDirection);
				previousFiringDirection = CurrentDirection;
			}

			float relativeSpeed = Vector3.Distance(CurrentTarget.Entity.GetLinearVelocity(), CubeBlock.CubeGrid.GetLinearVelocity());
			float firingThreshold = 2.5f + relativeSpeed / 10f;

			if (!IsNormalTurret && !Options.FlagSet(TargetingFlags.Turret))
				firingThreshold += 5;

			myLogger.debugLog("change in direction = " + speed + ", threshold is " + firingThreshold + ", proximity = " + shot.Distance(CurrentTarget.InterceptionPoint.Value) + " shot from " + shot.From + " to " + shot.To, "CheckFire()");

			if (firingThreshold > 0 && shot.DistanceLessEqual(CurrentTarget.InterceptionPoint.Value, firingThreshold))
			{
				if (Obstructed(finalPosition))
				{
					myLogger.debugLog("final position is obstructed", "CheckFire()");
					if (speed < 0.01)
					{
						myLogger.debugLog("blacklisting: " + CurrentTarget.Entity.getBestName(), "CheckFire()");
						//using (lock_Blacklist.AcquireExclusiveUsing())
							Blacklist.Add(CurrentTarget.Entity);
					}
					StopFiring("Obstructed");
				}
				else
					FireWeapon();
			}
			else
				StopFiring("shot is off target");
		}

		private void FireWeapon()
		{
			using (lock_IsShooting.AcquireExclusiveUsing())
			{
				if (IsShooting)
					return;

				myLogger.debugLog("Open fire", "FireWeapon()");

				GameThreadActions.Enqueue(() => 
					(CubeBlock as IMyTerminalBlock).GetActionWithName("Shoot").Apply(CubeBlock));

				IsShooting = true;
			}
		}

		protected void StopFiring(string reason)
		{
			using (lock_IsShooting.AcquireExclusiveUsing())
			{
				if (!IsShooting)
					return;

				myLogger.debugLog("Hold fire: " + reason, "StopFiring()"); ;

				GameThreadActions.Enqueue(() => 
					(CubeBlock as IMyTerminalBlock).GetActionWithName("Shoot").Apply(CubeBlock));

				IsShooting = false;
			}
		}

		/// <summary>
		/// Fills Available_Targets and PotentialObstruction
		/// </summary>
		private void CollectTargets()
		{
			//myLogger.debugLog("Entered CollectTargets", "CollectTargets()");

			Available_Targets = new Dictionary<TargetType, List<IMyEntity>>();
			PotentialObstruction = new List<IMyEntity>();

			BoundingSphereD nearbySphere = new BoundingSphereD(BarrelPositionWorld(), Options.TargetingRange);
			HashSet<IMyEntity> nearbyEntities = new HashSet<IMyEntity>();
			MyAPIGateway.Entities.GetEntitiesInSphere_Safe_NoBlock(nearbySphere, nearbyEntities);

			//myLogger.debugLog("found " + nearbyEntities.Count + " entities", "CollectTargets()");

			foreach (IMyEntity entity in nearbyEntities)
			{
				//myLogger.debugLog("Nearby entity: " + entity.getBestName(), "CollectTargets()");

				//using (lock_Blacklist.AcquireSharedUsing())
					if (Blacklist.Contains(entity))
						continue;

				if (entity is IMyFloatingObject)
				{
					//myLogger.debugLog("floater: " + entity.getBestName(), "CollectTargets()");
					if (entity.Physics == null || entity.Physics.Mass > 100)
					{
						AddTarget(TargetType.Moving, entity);
						continue;
					}
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

					if (asIdentity == null || CubeBlock.canConsiderHostile(asIdentity.PlayerId))
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

					if (CubeBlock.canConsiderHostile(asGrid))
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

			if (target.ToString().StartsWith("MyMissile"))
			{
				myLogger.debugLog("missile: " + target.getBestName() + ", type = " + tType + ", allowed targets = " + Options.CanTarget, "AddTarget()");
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

			myLogger.debugLog("getting closest " + tType + ", from list of " + targetsOfType.Count, "SetClosest()");

			IMyEntity closest = null;

			Vector3D weaponPosition = BarrelPositionWorld();

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
					if (distanceValue > Options.TargetingRangeSquared)
					{
						myLogger.debugLog("for type: " + tType + ", too far to target: " + target.getBestName(), "SetClosest()");
						continue;
					}

					if (Obstructed(targetPosition))
					{
						myLogger.debugLog("can't target: " + target.getBestName() + ", obstructed", "SetClosest()");
						//using (lock_Blacklist.AcquireExclusiveUsing())
							Blacklist.Add(target);
						continue;
					}
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
		private bool TargetableBlock(IMyCubeBlock block, bool Disable)
		{
			if (!(block is IMyTerminalBlock))
				return false;

			if (Disable && !block.IsWorking)
				return block.IsFunctional && Options.FlagSet(TargetingFlags.Functional);

			//myLogger.debugLog("mass of " + block.DisplayNameText + " is " + block.Mass, "TargetableBlock()");

			if (block.Mass < 100)
				return false;

			// control panels are too small
			//if (block.BlockDefinition.TypeId == type_ControlPanel)
			//{
			//	myLogger.debugLog("is a control panel: " + block.DisplayNameText, "TargetableBlock()");
			//	return false;
			//}
			//myLogger.debugLog("not a control panel: " + block.DisplayNameText, "TargetableBlock()");

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
			myLogger.debugLog("getting block from " + grid.DisplayName + ", target type = " + tType, "GetTargetBlock()");

			Vector3D myPosition = BarrelPositionWorld();
			CubeGridCache cache = CubeGridCache.GetFor(grid);

			target = null;
			distanceValue = double.MaxValue;

			if (cache.TotalByDefinition() == 0)
			{
				myLogger.debugLog("no terminal blocks on grid: " + grid.DisplayName, "GetTargetBlock()");
				return false;
			}

			// get decoy block
			{
				var decoyBlockList = cache.GetBlocksOfType(typeof(MyObjectBuilder_Decoy));
				if (decoyBlockList != null)
					foreach (IMyTerminalBlock block in decoyBlockList)
					{
						if (!block.IsWorking)
							continue;

						//using (lock_Blacklist.AcquireSharedUsing())
							if (Blacklist.Contains(block))
								continue;

						double distanceSq = Vector3D.DistanceSquared(myPosition, block.GetPosition());
						if (distanceSq > Options.TargetingRangeSquared)
							continue;

						//myLogger.debugLog("decoy search, block = " + block.DisplayNameText + ", distance = " + distance, "GetTargetBlock()");

						if (distanceSq < distanceValue && CubeBlock.canConsiderHostile(block as IMyCubeBlock))
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
						if (!TargetableBlock(block, true))
						{
							myLogger.debugLog("not targetable: " + block.DisplayNameText, "GetTargetBlock()");
							continue;
						}

						//using (lock_Blacklist.AcquireSharedUsing())
							if (Blacklist.Contains(block))
							{
								myLogger.debugLog("blacklisted: " + block.DisplayNameText, "GetTargetBlock()");
								continue;
							}

						double distanceSq = Vector3D.DistanceSquared(myPosition, block.GetPosition());
						if (distanceSq > Options.TargetingRangeSquared)
						{
							myLogger.debugLog("too far: " + block.DisplayNameText + ", distanceSq = " + distanceSq + ", TargetingRangeSquared = " + Options.TargetingRangeSquared, "GetTargetBlock()");
							continue;
						}
						distanceSq *= multiplier * multiplier * multiplier;

						myLogger.debugLog("blocksSearch = " + blocksSearch + ", block = " + block.DisplayNameText + ", distance value = " + distanceSq, "GetTargetBlock()");
						if (distanceSq < distanceValue && CubeBlock.canConsiderHostile(block))
						{
							target = block;
							distanceValue = distanceSq;
						}
						else
							myLogger.debugLog("have a closer block than " + block.DisplayNameText + ", close = " + target.getBestName() + ", distance value = " + distanceValue, "GetTargetBlock()");
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
					if (TargetableBlock(slim.FatBlock, false))
					{
						//using (lock_Blacklist.AcquireSharedUsing())
							if (Blacklist.Contains(slim.FatBlock))
								continue;

						double distanceSq = Vector3D.DistanceSquared(myPosition, slim.FatBlock.GetPosition());
						if (distanceSq > Options.TargetingRangeSquared)
							continue;
						distanceSq *= 1e12;

						if (CubeBlock.canConsiderHostile(slim.FatBlock))
						{
							target = slim.FatBlock;
							distanceValue = distanceSq;
							myLogger.debugLog("for type = " + tType + " and grid = " + grid.DisplayName + ", found a block: " + target.DisplayNameText + ", distanceValue = " + distanceValue, "GetTargetBlock()");
							return true;
						}
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
			BoundingSphereD ignoreArea = new BoundingSphereD(BarrelPositionWorld(), Options.TargetingRange / 10f);
			if (ignoreArea.Contains(projectilePosition) == ContainmentType.Contains)
			{
				//myLogger.debugLog("projectile is inside ignore area: " + projectile.getBestName(), "ProjectileIsThreat()");
				return false;
			}

			Vector3D weaponPosition = BarrelPositionWorld();
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
		}

		/// <summary>
		/// <para>Test line segment between weapon and target for obstructing entities.</para>
		/// <para>Tests for obstructing voxel map, non-hostile character, or non-hostile grid.</para>
		/// </summary>
		/// <param name="targetPosition">position of entity to shoot</param>
		/// Not going to add a ready-to-fire bypass for ignoring source grid it would only protect against suicidal designs
		/// TODO: use RayCast class
		private bool Obstructed(Vector3D targetPosition)
		{
			if (CubeBlock == null)
				throw new ArgumentNullException("weapon");

			if (!CanRotateTo(targetPosition))
			{
				//myLogger.debugLog("cannot rotate to", "Obstructed()");
				return true;
			}

			// build offset rays
			List<Line> AllTestLines = new List<Line>();
			if (Options.FlagSet(TargetingFlags.Interior))
				AllTestLines.Add(new Line(BarrelPositionWorld(), targetPosition, false));
			else
			{
				List<Vector3> obstructionOffsets;
				if (IsNormalTurret)
					obstructionOffsets = obstructionOffsets_turret;
				else
					obstructionOffsets = obstructionOffsets_fixed;

				Vector3D BarrelPosition = BarrelPositionWorld();
				foreach (Vector3 offsetBlock in obstructionOffsets)
				{
					Vector3 offsetWorld = RelativeDirection3F.FromBlock(CubeBlock, offsetBlock).ToWorld();
					AllTestLines.Add(new Line(BarrelPosition + offsetWorld, targetPosition + offsetWorld, false));
				}
			}

			// Voxel Test
			Vector3 boundary;
			foreach (Line testLine in AllTestLines)
				if (MyAPIGateway.Entities.RayCastVoxel_Safe(testLine.From, testLine.To, out boundary))
				{
					//myLogger.debugLog("from "+testLine.From+" to "+testLine.To+ "obstructed by voxel", "Obstructed()");
					return true;
				}

			// Test each entity
			List<IMyEntity> ignore = ObstructionIgnore;
			foreach (IMyEntity entity in PotentialObstruction)
			{
				if (entity.Closed)
					continue;

				if (ignore != null && ignore.Contains(entity))
				{
					myLogger.debugLog("ignoring " + entity.getBestName(), "Obstructed()");
					continue;
				}

				IMyCharacter asChar = entity as IMyCharacter;
				if (asChar != null)
				{
					double distance;
					foreach (Line testLine in AllTestLines)
						if (entity.WorldAABB.Intersects(new LineD(testLine.From, testLine.To), out distance))
						{
							//myLogger.debugLog("from " + testLine.From + " to " + testLine.To + "obstructed by character: " + entity.getBestName(), "Obstructed()");
							return true;
						}
					continue;
				}

				IMyCubeGrid asGrid = entity as IMyCubeGrid;
				if (asGrid != null)
				{
					if (!IsNormalTurret && asGrid == CubeBlock.CubeGrid)
						continue;

					//if (ignore != null && asGrid == ignore)
					//	continue;

					ICollection<Vector3I> allHitCells;

					if (AllTestLines.Count == 1)
					{
						List<Vector3I> hitCells = new List<Vector3I>();
						asGrid.RayCastCells(AllTestLines[0].From, AllTestLines[0].To, hitCells);

						//myLogger.debugLog("from " + AllTestLines[0].From + " to " + AllTestLines[0].To + " hits " + hitCells.Count + " cells of " + asGrid.getBestName(), "Obstructed()");

						allHitCells = hitCells;
					}
					else
					{
						allHitCells = new HashSet<Vector3I>();
						foreach (Line testLine in AllTestLines)
						{
							List<Vector3I> hitCells = new List<Vector3I>();
							asGrid.RayCastCells(testLine.From, testLine.To, hitCells);

							//myLogger.debugLog("from " + testLine.From + " to " + testLine.To + " hits " + hitCells.Count + " cells of " + asGrid.getBestName(), "Obstructed()");

							foreach (Vector3I cell in hitCells)
								allHitCells.Add(cell);
						}
					}

					foreach (Vector3I pos in allHitCells)
					{
						IMySlimBlock slim = asGrid.GetCubeBlock(pos);
						if (slim == null)
							continue;

						if (ignore != null && slim.FatBlock != null && ignore.Contains(slim.FatBlock))
						{
							myLogger.debugLog("ignoring " + slim.getBestName() + " of grid " + asGrid.getBestName(), "Obstructed()");
							continue;
						}

						//if (asGrid.CubeExists(pos))
						//{
						//List<IMySlimBlock> ignore = ObstructionIgnore;
						//if (ignore != null && ignore.Contains(asGrid.GetCubeBlock(pos)))
						//	continue;

						if (IsNormalTurret && asGrid == CubeBlock.CubeGrid)
						{
							//IMySlimBlock block = asGrid.GetCubeBlock(pos);
							if (slim.FatBlock == null || slim.FatBlock != CubeBlock)
							{
								myLogger.debugLog("normal turret obstructed by block: " + slim.getBestName() + " of grid " + asGrid.getBestName(), "Obstructed()");
								return true;
							}
						}
						else // not normal turret and not my grid
						{
							myLogger.debugLog("fixed weapon obstructed by block: " + asGrid.GetCubeBlock(pos).getBestName() + " of grid " + asGrid.getBestName(), "Obstructed()");
							return true;
						}
						//}
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

			Vector3D RelativeVelocity = TargetVelocity - CubeBlock.GetLinearVelocity();

			//TargetPosition += RelativeVelocity / 60f;

			//myLogger.debugLog("weapon position = " + BarrelPositionWorld() + ", ammo speed = " + LoadedAmmoSpeed(TargetPosition) + ", TargetPosition = " + TargetPosition + ", target velocity = " + target.GetLinearVelocity(), "GetFiringDirection()");
			FindInterceptVector(BarrelPositionWorld(), LoadedAmmoSpeed(TargetPosition), TargetPosition, RelativeVelocity);
			if (CurrentTarget.FiringDirection == null)
			{
				myLogger.debugLog("Blacklisting " + target.getBestName(), "SetFiringDirection()");
				//using (lock_Blacklist.AcquireExclusiveUsing())
					Blacklist.Add(target);
				return;
			}
			if (Obstructed(CurrentTarget.InterceptionPoint.Value))
			{
				myLogger.debugLog("Shot path is obstructed, blacklisting " + target.getBestName(), "SetFiringDirection()");
				//using (lock_Blacklist.AcquireExclusiveUsing())
					Blacklist.Add(target);
				CurrentTarget = new Target();
				return;
			}
		}

		/// <remarks>From http://danikgames.com/blog/moving-target-intercept-in-3d/</remarks>
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
				CurrentTarget = CurrentTarget.AddDirectionPoint(null, null);
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
				Vector3 firingDirection = Vector3.Normalize(shotVelOrth + shotVelTang);

				// Find the time of collision (distance / relative velocity)
				float timeToCollision = distanceToTarget / (shotSpeedOrth - targetSpeedOrth);

				// Calculate where the shot will be at the time of collision
				Vector3 shotVel = shotVelOrth + shotVelTang;
				Vector3 interceptionPoint = shotOrigin + shotVel * timeToCollision;

				CurrentTarget = CurrentTarget.AddDirectionPoint(firingDirection, interceptionPoint);
			}
		}

		/// <summary>
		/// Write errors to weapon, using angle brackets.
		/// </summary>
		private void WriteErrors(List<string> Errors)
		{
			string DisplayName = CubeBlock.DisplayNameText;
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
				GameThreadActions.Enqueue(() => 
					(CubeBlock as IMyTerminalBlock).SetCustomName(build));
			}
			else
				GameThreadActions.Enqueue(() => 
					(CubeBlock as IMyTerminalBlock).SetCustomName(DisplayName));
		}
	}
}
