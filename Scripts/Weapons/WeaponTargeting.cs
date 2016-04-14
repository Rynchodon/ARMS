using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.AntennaRelay;
using Rynchodon.Threading;
using Rynchodon.Utility;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Weapons
{
	/// <summary>
	/// Contains functions that are common to turrets and fixed weapons
	/// </summary>
	public abstract class WeaponTargeting : TargetingBase
	{

		public enum Control : byte { Off, On, Engager }

		/// <remarks>
		/// <para>Increasing the number of threads would require locks to be added in many areas.</para>
		/// <para>One thread has no trouble putting enough projectiles into play to slow the game to a crawl.</para>
		/// </remarks>
		private static ThreadManager Thread = new ThreadManager(threadName: "WeaponTargeting");
		//private static List<Vector3> obstructionOffsets_turret = new List<Vector3>();
		//private static List<Vector3> obstructionOffsets_fixed = new List<Vector3>();

		private static ITerminalProperty<bool> TPro_Shoot;

		static WeaponTargeting()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
			//obstructionOffsets_turret.Add(new Vector3(0, -1.25f, 0));
			//obstructionOffsets_turret.Add(new Vector3(2.5f, 5f, 2.5f));
			//obstructionOffsets_turret.Add(new Vector3(2.5f, 5f, -2.5f));
			//obstructionOffsets_turret.Add(new Vector3(-2.5f, 5f, 2.5f));
			//obstructionOffsets_turret.Add(new Vector3(-2.5f, 5f, -2.5f));

			//obstructionOffsets_fixed.Add(new Vector3(0, 0, 0));
			//obstructionOffsets_fixed.Add(new Vector3(-2.5f, -2.5f, 0));
			//obstructionOffsets_fixed.Add(new Vector3(-2.5f, 2.5f, 0));
			//obstructionOffsets_fixed.Add(new Vector3(2.5f, -2.5f, 0));
			//obstructionOffsets_fixed.Add(new Vector3(2.5f, 2.5f, 0));
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			Thread = null;
			TPro_Shoot = null;
			//obstructionOffsets_turret = null;
			//obstructionOffsets_fixed = null;
		}

		public readonly Ingame.IMyLargeTurretBase myTurret;

		/// <remarks>Simple turrets can potentially shoot their own grids so they must be treated differently</remarks>
		public readonly bool IsNormalTurret;
		/// <summary>Locked while an update on targeting thread is queued but not while it is running.</summary>
		private readonly FastResourceLock lock_Queued = new FastResourceLock();

		private Logger myLogger;
		public Ammo LoadedAmmo { get; private set; }
		private long UpdateNumber = 0;

		private InterpreterWeapon Interpreter;
		private int InterpreterErrorCount = int.MaxValue;

		protected bool FireWeapon;
		private bool IsFiringWeapon;
		private Control value_currentControl;

		private List<IMyEntity> value_ObstructIgnore;

		private LockedQueue<Action> GameThreadActions = new LockedQueue<Action>(1);
		public readonly NetworkClient m_netClient;

		public readonly WeaponDefinitionExpanded WeaponDefinition;

		public Control CurrentControl
		{
			get { return value_currentControl; }
			set
			{
				if (value_currentControl == value)
					return;

				//myLogger.debugLog("Control changed from " + value_currentControl + " to " + value, "get_CurrentControl()");

				if (IsNormalTurret && MyAPIGateway.Multiplayer.IsServer)
				{
					if (value == Control.Off)
						GameThreadActions.Enqueue(() => myTurret.ResetTargetingToDefault());
					else
						GameThreadActions.Enqueue(() => myTurret.SetTarget(ProjectilePosition() + (CubeBlock.WorldMatrix.Backward + CubeBlock.WorldMatrix.Up) * 10));
				}

				if (value == Control.Engager)
					UpdateAmmo();

				value_currentControl = value;
				FireWeapon = false;
			}
		}

		/// <summary>Checks that it is possible to control the weapon: working, not in use, etc.</summary>
		public bool CanControl
		{
			get { return CubeBlock.IsWorking && (!IsNormalTurret || !myTurret.IsUnderControl) && CubeBlock.OwnerId != 0; }
		}

		public bool HasAmmo
		{
			get { return LoadedAmmo != null; }
		}

		public bool GuidedLauncher { get; set; }

		public WeaponTargeting(IMyCubeBlock weapon)
			: base(weapon)
		{
			if (weapon == null)
				throw new ArgumentNullException("weapon");
			if (!(weapon is IMyTerminalBlock) || !(weapon is IMyFunctionalBlock) || !((MyEntity)weapon).HasInventory || !(weapon is Ingame.IMyUserControllableGun))
				throw new ArgumentException("weapon(" + weapon.DefinitionDisplayNameText + ") is not of correct type");

			this.myTurret = weapon as Ingame.IMyLargeTurretBase;
			this.myLogger = new Logger("WeaponTargeting", weapon);

			this.Interpreter = new InterpreterWeapon(weapon);
			this.Options = new TargetingOptions();
			this.IsNormalTurret = myTurret != null;
			this.CubeBlock.OnClose += weapon_OnClose;
			this.FuncBlock.AppendingCustomInfo += FuncBlock_AppendingCustomInfo;
			this.ObstructionIgnore = new List<IMyEntity>();

			if (TPro_Shoot == null)
				TPro_Shoot = (weapon as IMyTerminalBlock).GetProperty("Shoot").AsBool();

			if (WeaponDescription.GetFor(weapon).LastSeenTargeting)
				m_netClient = new NetworkClient(weapon);

			WeaponDefinition = MyDefinitionManager.Static.GetWeaponDefinition(((MyWeaponBlockDefinition)weapon.GetCubeBlockDefinition()).WeaponDefinitionId);

			//myLogger.debugLog("initialized", "WeaponTargeting()", Logger.severity.INFO);
		}

		private void weapon_OnClose(IMyEntity obj)
		{
			//myLogger.debugLog("entered weapon_OnClose()", "weapon_OnClose()");

			CubeBlock.OnClose -= weapon_OnClose;
			if (Options == null)
				Options.Flags = TargetingFlags.None;

			//myLogger.debugLog("leaving weapon_OnClose()", "weapon_OnClose()");
		}

		/// <summary>
		/// UpdateManger invokes this every update.
		/// </summary>
		public void Update_Targeting()
		{
			if (!MyAPIGateway.Multiplayer.IsServer && !MyAPIGateway.Session.Player.IdentityId.canControlBlock(CubeBlock))
				return;

			try
			{
				GameThreadActions.DequeueAll(action => action.Invoke());
				if (CurrentControl != Control.Off && FireWeapon != IsFiringWeapon && MyAPIGateway.Multiplayer.IsServer)
				{
					IsFiringWeapon = FireWeapon;
					if (FireWeapon)
					{
						//myLogger.debugLog("Opening fire", "Update_Targeting()");
						(CubeBlock as IMyTerminalBlock).GetActionWithName("Shoot_On").Apply(CubeBlock);
					}
					else
					{
						//myLogger.debugLog("Holding fire", "Update_Targeting()");
						IMyFunctionalBlock func = CubeBlock as IMyFunctionalBlock;
						func.GetActionWithName("Shoot_Off").Apply(CubeBlock);

						// Shoot_Off is not working for gatling/interior turrets, this seems to do the trick
						if (myTurret != null)
							myTurret.SetTarget(ProjectilePosition() + (CubeBlock.WorldMatrix.Backward + CubeBlock.WorldMatrix.Up) * 10);
					}
				}

				Update1_GameThread();

				if (lock_Queued.TryAcquireExclusive())
					Thread.EnqueueAction(Update_Thread);
			}
			catch (Exception ex)
			{
				myLogger.alwaysLog("Exception: " + ex, "Update_Targeting()", Logger.severity.ERROR);
				if (MyAPIGateway.Multiplayer.IsServer)
					FuncBlock.RequestEnable(false);

				((IMyFunctionalBlock)CubeBlock).AppendCustomInfo("ARMS targeting crashed, see log for details");
			}
		}

		// rarely changes, so not really optimized
		protected List<IMyEntity> ObstructionIgnore
		{
			get { return value_ObstructIgnore; }
			set
			{
				if (IsNormalTurret)
				{
					if (!value.Contains(CubeBlock))
						value.Add(CubeBlock);
				}
				else
				{
					if (!value.Contains(CubeBlock.CubeGrid))
						value.Add(CubeBlock.CubeGrid);
				}
				value_ObstructIgnore = value;
			}
		}

		/// <summary>Invoked on game thread, every updated, if targeting is permitted.</summary>
		protected abstract void Update1_GameThread();

		/// <summary>Invoked on targeting thread, every 100 updates, if targeting is permitted.</summary>
		protected abstract void Update100_Options_TargetingThread(TargetingOptions current);

		/// <summary>World direction that the weapon is facing.</summary>
		protected abstract Vector3 Facing();

		protected override float ProjectileSpeed(ref Vector3D targetPos)
		{
			if (LoadedAmmo == null)
				return 1f;

			if (LoadedAmmo.DistanceToMaxSpeed < 1)
			{
				//myLogger.debugLog("DesiredSpeed = " + LoadedAmmo.AmmoDefinition.DesiredSpeed, "LoadedAmmoSpeed()");
				return LoadedAmmo.AmmoDefinition.DesiredSpeed;
			}

			if (LoadedAmmo.MissileDefinition == null)
			{
				myLogger.alwaysLog("Missile Ammo expected: " + LoadedAmmo.AmmoDefinition.DisplayNameText, "LoadedAmmoSpeed()", Logger.severity.ERROR);
				return LoadedAmmo.AmmoDefinition.DesiredSpeed;
			}

			float distance = Vector3.Distance(ProjectilePosition(), targetPos);
			return LoadedAmmo.MissileSpeed(distance);
		}

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
						Profiler.Profile(Update100);
					Profiler.Profile(Update10);
				}
				Profiler.Profile(Update1);

				UpdateNumber++;
			}
			catch (Exception ex)
			{ myLogger.alwaysLog("Exception: " + ex, "Update_Thread()", Logger.severity.WARNING); }
		}

		/// <summary>
		/// Determines firing direction & intersection point.
		/// </summary>
		private void Update1()
		{
			if (CurrentControl == Control.Off || LoadedAmmo == null || CurrentTarget == null || CurrentTarget.Entity == null || CurrentTarget.Entity.Closed)
			{
				FireWeapon = false;
				return;
			}

			SetFiringDirection();
			CheckFire();
		}

		/// <summary>
		/// Checks for ammo and chooses a target (if necessary).
		/// </summary>
		private void Update10()
		{
			if (GuidedLauncher)
				UpdateAmmo();

			if (CurrentControl == Control.Off)
				return;

			if (!GuidedLauncher)
				UpdateAmmo();
			if (LoadedAmmo == null)
			{
				//myLogger.debugLog("No ammo loaded", "Update10()");
				CurrentTarget = NoTarget.Instance;
				return;
			}

			UpdateTarget();

			if ((CurrentTarget.TType == TargetType.None || CurrentTarget is LastSeenTarget) && m_netClient != null)
				GetLastSeenTarget(m_netClient.GetStorage(), LoadedAmmo.MissileDefinition.MaxTrajectory);
		}

		private void Update100()
		{
			CheckCustomInfo();

			if (!CanControl)
			{
				//myLogger.debugLog("cannot control", "Update100()");
				CurrentControl = Control.Off;
				Options.Flags = TargetingFlags.None;
				return;
			}

			IsFiringWeapon = TPro_Shoot.GetValue(CubeBlock);
			//myLogger.debugLog("fire: " + FireWeapon + ", isFiring: " + IsFiringWeapon, "Update100()");
			ClearBlacklist();

			if (Interpreter.UpdateInstruction())
				UpdateOptions();
			else
			{
				if (Interpreter.Options == null)
					Options = new TargetingOptions();
				else
					Options = Interpreter.Options.Clone();
				Update100_Options_TargetingThread(Options);
			}
			if (CurrentControl == Control.Engager)
				return;
			if (Interpreter.HasInstructions && (IsNormalTurret || Options.FlagSet(TargetingFlags.Turret)))
			{
				CurrentControl = Control.On;
				return;
			}

			//myLogger.debugLog("Not running targeting", "Update100()");
			CurrentControl = Control.Off;
		}

		private void UpdateOptions()
		{
			if (Interpreter.Errors.Count <= InterpreterErrorCount)
			{
				if (Interpreter.Options == null)
					Options = new TargetingOptions();
				else
					Options = Interpreter.Options.Clone();
				InterpreterErrorCount = Interpreter.Errors.Count;
				Update100_Options_TargetingThread(Options);
				myLogger.debugLog("updating Options, Error Count = " + Interpreter.Errors.Count + ", Options: " + Options, "UpdateOptions()");
			}
			else
				myLogger.debugLog("not updating Options, Error Count = " + Interpreter.Errors.Count, "UpdateOptions()");
		}

		private void UpdateAmmo()
		{
			LoadedAmmo = MyAPIGateway.Session.CreativeMode ? WeaponDefinition.FirstAmmo : Ammo.GetLoadedAmmo(CubeBlock);
		}

		private Vector3 previousFiringDirection;

		private void CheckFire()
		{
			Target target = CurrentTarget;

			if (!target.FiringDirection.HasValue || !target.ContactPoint.HasValue)
			{
				FireWeapon = false;
				return;
			}

			Vector3 weaponPosition = ProjectilePosition();

			Vector3 CurrentDirection = Facing();
			float directionChange;
			Vector3.DistanceSquared(ref CurrentDirection, ref previousFiringDirection, out directionChange);
			previousFiringDirection = CurrentDirection;

			if (directionChange > 0.01f)
			{
				// weapon is still being aimed
				//myLogger.debugLog("still turning, change: " + directionChange, "CheckFire()");
				FireWeapon = false;
				return;
			}

			Vector3 firingDirection = target.FiringDirection.Value;
			float accuracy;
			Vector3.Dot(ref CurrentDirection, ref firingDirection, out accuracy);

			if (accuracy < WeaponDefinition.RequiredAccuracy)
			{
				// not facing target
				//myLogger.debugLog("not facing, accuracy: " + accuracy + ", required: " + WeaponDefinition.RequiredAccuracy, "CheckFire()");
				FireWeapon = false;
				return;
			}

			if (Obstructed(target.ContactPoint.Value))
			{
				//myLogger.debugLog("target is obstructed", "CheckFire()");
				//myLogger.debugLog("blacklisting: " + target.Entity.getBestName(), "CheckFire()");
				BlacklistTarget();
				FireWeapon = false;
				return;
			}

			FireWeapon = true;
		}

		/// <summary>
		/// <para>Test line segment between weapon and target for obstructing entities.</para>
		/// <para>Tests for obstructing voxel map, non-hostile character, or non-hostile grid.</para>
		/// </summary>
		/// <param name="targetPosition">position of entity to shoot</param>
		/// Not going to add a ready-to-fire bypass for ignoring source grid it would only protect against suicidal designs
		protected override bool Obstructed(Vector3D targetPosition)
		{
			if (CubeBlock == null)
				throw new ArgumentNullException("weapon");

			// build offset rays
			List<Line> AllTestLines = new List<Line>();
			//if (Options.FlagSet(TargetingFlags.Interior))
			AllTestLines.Add(new Line(ProjectilePosition(), targetPosition, false));
			//else
			//{
			//	List<Vector3> obstructionOffsets;
			//	if (IsNormalTurret)
			//		obstructionOffsets = obstructionOffsets_turret;
			//	else
			//		obstructionOffsets = obstructionOffsets_fixed;

			//	Vector3D BarrelPosition = ProjectilePosition();
			//	foreach (Vector3 offsetBlock in obstructionOffsets)
			//	{
			//		Vector3 offsetWorld = RelativeDirection3F.FromBlock(CubeBlock, offsetBlock).ToWorld();
			//		AllTestLines.Add(new Line(BarrelPosition + offsetWorld, targetPosition + offsetWorld, false));
			//	}
			//}

			return RayCast.Obstructed(AllTestLines, PotentialObstruction, ObstructionIgnore, true);
		}

		private bool condition_changed;
		private bool prev_working, prev_playerControl, prev_noOwn, prev_ammo;
		private int prev_errors;
		private Target prev_target;
		private Control prev_control;

		/// <summary>
		/// Look for changes that would affect custom info.
		/// </summary>
		private void CheckCustomInfo()
		{
			condition_changed = false;

			ConditionChange(CubeBlock.IsWorking, ref prev_working);
			ConditionChange(IsNormalTurret && myTurret.IsUnderControl, ref prev_playerControl);
			ConditionChange(CubeBlock.OwnerId == 0, ref prev_noOwn);

			ConditionChange(Interpreter.Errors.Count, ref prev_errors);

			ConditionChange(CurrentControl, ref prev_control);
			ConditionChange(LoadedAmmo == null, ref prev_ammo);
			ConditionChange(CurrentTarget, ref prev_target);

			if (condition_changed)
				MyAPIGateway.Utilities.InvokeOnGameThread(FuncBlock.RefreshCustomInfo);
		}

		private void ConditionChange<T>(T condition, ref T previous)
		{
			if (!condition.Equals(previous))
			{
				condition_changed = true;
				previous = condition;
			}
		}

		private void FuncBlock_AppendingCustomInfo(IMyTerminalBlock block, StringBuilder customInfo)
		{
			if (block == null || block.Closed)
				return;

			if (Interpreter.Errors.Count != 0)
			{
				customInfo.AppendLine("Syntax Errors: ");
				customInfo.AppendLine(string.Join("\n", Interpreter.Errors));
				customInfo.AppendLine();
			}

			if (GuidedLauncher)
			{
				Target t = CurrentTarget;
				if (t.Entity != null)
				{
					Ammo la = LoadedAmmo;
					if (la != null && !string.IsNullOrEmpty(la.AmmoDefinition.DisplayNameString))
						customInfo.Append(la.AmmoDefinition.DisplayNameString);
					else
						customInfo.Append("Guided Missile");
					customInfo.Append(" fired at ");

					LastSeenTarget lst = t as LastSeenTarget;
					if (lst != null)
					{
						if (lst.Block != null)
						{
							customInfo.Append(lst.Block.DefinitionDisplayNameText);
							customInfo.Append(" on ");
						}
						customInfo.AppendLine(lst.LastSeen.HostileName());
					}
					else
						customInfo.AppendLine(t.Entity.GetNameForDisplay(CubeBlock.OwnerId));
				}
				// else, guided missile has no initial target though it may acquire one
			}

			if (!CubeBlock.IsWorking)
			{
				customInfo.AppendLine("Off");
				return;
			}
			if (IsNormalTurret && myTurret.IsUnderControl)
			{
				customInfo.AppendLine("Being controlled by player");
				return;
			}
			if (CubeBlock.OwnerId == 0)
				customInfo.AppendLine("No owner");

			switch (CurrentControl)
			{
				case Control.Off:
					if (IsNormalTurret)
						customInfo.AppendLine("Vanilla targeting enabled");
					return;
				case Control.On:
					if (IsNormalTurret)
						customInfo.AppendLine("ARMS controlling");
					else
						customInfo.AppendLine("ARMS rotor-turret");
					break;
				case Control.Engager:
					customInfo.AppendLine("Engager controlling");
					break;
			}

			if (LoadedAmmo == null)
				customInfo.AppendLine("No ammo");
			if (CurrentTarget.Entity == null)
				customInfo.AppendLine("No target");
			else
			{
				customInfo.Append("Has target: ");
				customInfo.AppendLine(CurrentTarget.Entity.GetNameForDisplay(CubeBlock.OwnerId));
			}
		}

	}
}
