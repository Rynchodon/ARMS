using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Threading;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
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

		/// <remarks>
		/// <para>Increasing the number of threads would require locks to be added in many areas.</para>
		/// <para>One thread has no trouble putting enough projectiles into play to slow the game to a crawl.</para>
		/// </remarks>
		private static ThreadManager Thread = new ThreadManager(threadName: "WeaponTargeting");
		private static List<Vector3> obstructionOffsets_turret = new List<Vector3>();
		private static List<Vector3> obstructionOffsets_fixed = new List<Vector3>();

		private static ITerminalProperty<bool> TPro_Shoot;

		static WeaponTargeting()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
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

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			Thread = null;
			obstructionOffsets_turret = null;
			obstructionOffsets_fixed = null;
		}

		public readonly Ingame.IMyLargeTurretBase myTurret;

		/// <remarks>Simple turrets can potentially shoot their own grids so they must be treated differently</remarks>
		public readonly bool IsNormalTurret;
		/// <summary>Locked while an update on targeting thread is queued but not while it is running.</summary>
		private readonly FastResourceLock lock_Queued = new FastResourceLock();

		private Logger myLogger;
		private Ammo LoadedAmmo;
		private long UpdateNumber = 0;

		private InterpreterWeapon Interpreter;
		private int InterpreterErrorCount = int.MaxValue;

		protected bool FireWeapon;
		private bool IsFiringWeapon;
		private bool value_engagerControlling;
		private bool value_defaultTargeting;

		private List<IMyEntity> value_ObstructIgnore;
		private readonly FastResourceLock lock_ObstructIgnore = new FastResourceLock();

		private LockedQueue<Action> GameThreadActions = new LockedQueue<Action>(1);

		/// <summary>Iff true, ARMS should target the weapon.</summary>
		public bool RunTargeting
		{
			get { return IsNormalTurret ? Options.FlagSet(TargetingFlags.ArmsEnabled) : (Options.FlagSet(TargetingFlags.Rotor_Turret) || EngagerControlling); }
		}

		/// <summary>Checks that it is possible to control the weapon: working, not in use, etc.</summary>
		public bool CanControl
		{
			get { return CubeBlock.IsWorking && (!IsNormalTurret || !myTurret.IsUnderControl) && CubeBlock.OwnerId != 0; }
		}

		public bool GuidedLauncher { get; set; }

		/// <summary>Has no effect on normal turrets, enables ARMS targetting for fixed weapons.</summary>
		public bool EngagerControlling
		{
			get { return value_engagerControlling; }
			set
			{
				if (IsNormalTurret)
					return;

				value_engagerControlling = value;
				FireWeapon = false;
			}
		}

		/// <summary>True iff the vanilla targeting is running for this weapon.</summary>
		private bool DefaultTargeting
		{
			get { return value_defaultTargeting; }
			set
			{
				if (!IsNormalTurret)
					return;

				if (value_defaultTargeting == value)
					return;

				value_defaultTargeting = value;

				FireWeapon = false;

				if (value_defaultTargeting)
					GameThreadActions.Enqueue(() => myTurret.ResetTargetingToDefault());
				else
					GameThreadActions.Enqueue(() => myTurret.SetTarget(ProjectilePosition() + CubeBlock.WorldMatrix.Forward * 10));
			}
		}

		public WeaponTargeting(IMyCubeBlock weapon)
			: base(weapon)
		{
			if (weapon == null)
				throw new ArgumentNullException("weapon");
			if (!(weapon is IMyTerminalBlock) || !(weapon is IMyFunctionalBlock) || !(weapon is IMyInventoryOwner) || !(weapon is Ingame.IMyUserControllableGun))
				throw new ArgumentException("weapon(" + weapon.DefinitionDisplayNameText + ") is not of correct type");

			this.myTurret = weapon as Ingame.IMyLargeTurretBase;
			this.myLogger = new Logger("WeaponTargeting", weapon);// { MinimumLevel = Logger.severity.DEBUG };

			this.Interpreter = new InterpreterWeapon(weapon);
			this.Options = new TargetingOptions();
			this.IsNormalTurret = myTurret != null;
			this.CubeBlock.OnClose += weapon_OnClose;
			this.value_defaultTargeting = IsNormalTurret;
			this.FuncBlock.AppendingCustomInfo += FuncBlock_AppendingCustomInfo;

			if (TPro_Shoot == null)
				TPro_Shoot = (weapon as IMyTerminalBlock).GetProperty("Shoot").AsBool();

			myLogger.debugLog("initialized", "WeaponTargeting()", Logger.severity.INFO);
		}

		private void weapon_OnClose(IMyEntity obj)
		{
			myLogger.debugLog("entered weapon_OnClose()", "weapon_OnClose()");

			CubeBlock.OnClose -= weapon_OnClose;
			if (Options == null)
				Options.Flags = TargetingFlags.None;

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
				if (RunTargeting && FireWeapon != IsFiringWeapon)
				{
					IsFiringWeapon = FireWeapon;
					if (FireWeapon)
					{
						myLogger.debugLog("Opening fire", "Update_Targeting()");
						(CubeBlock as IMyTerminalBlock).GetActionWithName("Shoot_On").Apply(CubeBlock);
					}
					else
					{
						myLogger.debugLog("Holding fire", "Update_Targeting()");
						(CubeBlock as IMyTerminalBlock).GetActionWithName("Shoot_Off").Apply(CubeBlock);
					}
				}

				Update1_GameThread();

				if (lock_Queued.TryAcquireExclusive())
					Thread.EnqueueAction(Update_Thread);
			}
			catch (Exception ex)
			{
				myLogger.alwaysLog("Exception: " + ex, "Update_Targeting()", Logger.severity.ERROR);
				ArmsGuiWeapons.DisableArmsControl(FuncBlock);
				Options.Flags = TargetingFlags.None;

				IMyFunctionalBlock func = CubeBlock as IMyFunctionalBlock;
				func.SetCustomName("<Broken>" + func.DisplayNameText);
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

		/// <summary>Invoked on game thread, every updated, if targeting is permitted.</summary>
		protected abstract void Update1_GameThread();

		/// <summary>Invoked on targeting thread, every 100 updates, if targeting is permitted.</summary>
		protected abstract void Update100_Options_TargetingThread(TargetingOptions current);

		protected override float ProjectileSpeed(Vector3D targetPos)
		{
			if (LoadedAmmo.DistanceToMaxSpeed < 1)
			{
				myLogger.debugLog("DesiredSpeed = " + LoadedAmmo.AmmoDefinition.DesiredSpeed, "LoadedAmmoSpeed()");
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
			{ myLogger.alwaysLog("Exception: " + ex, "Update_Thread()", Logger.severity.WARNING); }
		}

		/// <summary>
		/// Determines firing direction & intersection point.
		/// </summary>
		private void Update1()
		{
			if (!RunTargeting || LoadedAmmo == null || CurrentTarget.Entity == null || CurrentTarget.Entity.Closed)
				return;

			if (CurrentTarget.TType != TargetType.None)
				SetFiringDirection();

			CheckFire();
		}

		/// <summary>
		/// Checks for ammo and chooses a target (if necessary).
		/// </summary>
		private void Update10()
		{
			if (!RunTargeting)
				return;

			UpdateAmmo();
			if (LoadedAmmo == null)
			{
				myLogger.debugLog("No ammo loaded", "Update10()");
				return;
			}

			UpdateTarget();
		}

		private void Update100()
		{
			CheckCustomInfo();

			if (!CanControl)
			{
				myLogger.debugLog("cannot control", "Update100()");
				DefaultTargeting = true;
				Options.Flags = TargetingFlags.None;
				return;
			}

			IsFiringWeapon = TPro_Shoot.GetValue(CubeBlock);
			ClearBlacklist();

			if (RunTargeting || GuidedLauncher)
			{
				Interpreter.UpdateInstruction();
				if (Interpreter.Errors.Count <= InterpreterErrorCount)
				{
					Options = Interpreter.Options;
					if (Options == null)
						Options = new TargetingOptions();
					InterpreterErrorCount = Interpreter.Errors.Count;
					ArmsGuiWeapons.UpdateTerm(FuncBlock, Options);
					Update100_Options_TargetingThread(Options);
					myLogger.debugLog("updating Options, Error Count = " + Interpreter.Errors.Count + ", Options: " + Options, "Update100()");
				}
				else
					myLogger.debugLog("not updating Options, Error Count = " + Interpreter.Errors.Count, "Update100()");

				DefaultTargeting = false;
				WriteErrors(Interpreter.Errors);
			}
			else
			{
				myLogger.debugLog("Not running targeting", "Update100()");
				DefaultTargeting = true;
				ArmsGuiWeapons.UpdateArmsEnabled(FuncBlock, Options);
			}
		}

		private void UpdateAmmo()
		{
			Ammo newAmmo = Ammo.GetLoadedAmmo(CubeBlock);
			if (newAmmo != null && newAmmo != LoadedAmmo)
			{
				LoadedAmmo = newAmmo;
				myLogger.debugLog("loaded ammo: " + LoadedAmmo.AmmoDefinition, "UpdateLoadedMissile()");
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
			Target target = CurrentTarget;

			if (!target.FiringDirection.HasValue || !target.ContactPoint.HasValue)
			{
				FireWeapon = false;
				return;
			}

			Vector3 weaponPosition = ProjectilePosition();
			float distance = Vector3.Distance(weaponPosition, target.ContactPoint.Value);

			using (lock_CurrentDirection.AcquireSharedUsing())
			{
				float directionChange = Vector3.RectangularDistance(ref CurrentDirection, ref previousFiringDirection);
				previousFiringDirection = CurrentDirection;

				Vector3 p0 = weaponPosition + target.FiringDirection.Value * distance;
				Vector3 p1 = weaponPosition + CurrentDirection * distance;
				float threshold;
				if (directionChange <= 0.01f)
					threshold = 100f;
				else if (directionChange >= 1f)
					threshold = 1f;
				else
					threshold = 1f / directionChange;
				//myLogger.debugLog("origin: " + weaponPosition + ", direction to target: " + Vector3.Normalize(p0 - weaponPosition) + ", diff to FiringDirection: " + Vector3.DistanceSquared(Vector3.Normalize(p0 - weaponPosition), target.FiringDirection.Value), "CheckFire()");
				myLogger.debugLog("firing direction: " + target.FiringDirection + ", current direct: " + CurrentDirection + ", P0: " + p0 + ", P1: " + p1 + ", diff sq: " + Vector3.DistanceSquared(p0, p1) + ", threshold: " + threshold, "CheckFire()");
				if (Vector3.DistanceSquared(p0, p1) > threshold)
				{
					FireWeapon = false;
					return;
				}

				if (Obstructed(target.ContactPoint.Value))
				{
					myLogger.debugLog("target is obstructed", "CheckFire()");
					if (directionChange < 0.01f)
					{
						myLogger.debugLog("blacklisting: " + target.Entity.getBestName(), "CheckFire()");
						BlacklistTarget();
					}
					FireWeapon = false;
					return;
				}

				FireWeapon = true;
			}
		}

		/// <summary>
		/// <para>Test line segment between weapon and target for obstructing entities.</para>
		/// <para>Tests for obstructing voxel map, non-hostile character, or non-hostile grid.</para>
		/// </summary>
		/// <param name="targetPosition">position of entity to shoot</param>
		/// Not going to add a ready-to-fire bypass for ignoring source grid it would only protect against suicidal designs
		/// TODO: use RayCast class
		protected override bool Obstructed(Vector3D targetPosition)
		{
			if (CubeBlock == null)
				throw new ArgumentNullException("weapon");

			// build offset rays
			List<Line> AllTestLines = new List<Line>();
			if (Options.FlagSet(TargetingFlags.Interior))
				AllTestLines.Add(new Line(ProjectilePosition(), targetPosition, false));
			else
			{
				List<Vector3> obstructionOffsets;
				if (IsNormalTurret)
					obstructionOffsets = obstructionOffsets_turret;
				else
					obstructionOffsets = obstructionOffsets_fixed;

				Vector3D BarrelPosition = ProjectilePosition();
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
					myLogger.debugLog("from " + testLine.From + " to " + testLine.To + "obstructed by voxel", "Obstructed()");
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
					{
						LineD l = (LineD)testLine;
						if (entity.WorldAABB.Intersects(ref l, out distance))
						{
							myLogger.debugLog("from " + testLine.From + " to " + testLine.To + "obstructed by character: " + entity.getBestName(), "Obstructed()");
							return true;
						}
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

		private bool condition_changed;
		private bool prev_working, prev_control, prev_noOwn, prev_vanilla, prev_ammo, prev_targeting, prev_noTarget;

		/// <summary>
		/// Look for changes that would affect custom info.
		/// </summary>
		private void CheckCustomInfo()
		{
			condition_changed = false;

			ConditionChange(CubeBlock.IsWorking, ref prev_working);
			ConditionChange(IsNormalTurret && myTurret.IsUnderControl, ref prev_control);
			ConditionChange(CubeBlock.OwnerId == 0, ref prev_noOwn);
			ConditionChange(CubeBlock.IsWorking && DefaultTargeting, ref prev_vanilla);

			ConditionChange(RunTargeting, ref prev_targeting);
			ConditionChange(LoadedAmmo == null, ref prev_ammo);
			ConditionChange(CurrentTarget == null || CurrentTarget.Entity == null, ref prev_noTarget);

			if (condition_changed)
				MyAPIGateway.Utilities.InvokeOnGameThread(FuncBlock.RefreshCustomInfo);
		}

		private void ConditionChange(bool condition, ref bool previous)
		{
			if (condition != previous)
			{
				condition_changed = true;
				previous = condition;
			}
		}

		private void FuncBlock_AppendingCustomInfo(IMyTerminalBlock block, StringBuilder customInfo)
		{
			if (!CubeBlock.IsWorking)
				customInfo.AppendLine("Off");
			if (IsNormalTurret && myTurret.IsUnderControl)
				customInfo.AppendLine("Being controlled by player");
			if (CubeBlock.OwnerId == 0)
				customInfo.AppendLine("No owner");
			if (CubeBlock.IsWorking && DefaultTargeting)
				customInfo.AppendLine("Vanilla targeting enabled");

			if (RunTargeting)
			{
				if (Options.FlagSet(TargetingFlags.ArmsEnabled))
					customInfo.AppendLine("ARMS controlling");
				if (Options.FlagSet(TargetingFlags.Rotor_Turret))
					customInfo.AppendLine("Rotor-turret");
				if (EngagerControlling)
					customInfo.AppendLine("Engager controlling");
				if (LoadedAmmo == null)
					customInfo.AppendLine("No ammo");
				if (CurrentTarget == null || CurrentTarget.Entity == null)
					customInfo.AppendLine("No target");
				else
				{
					IMyCubeBlock targetBlock = CurrentTarget.Entity as IMyCubeBlock;
					if (targetBlock != null)
					{
						customInfo.Append("Has target: ");
						customInfo.AppendLine(targetBlock.DefinitionDisplayNameText);
					}
					else
						customInfo.AppendLine("Has target");
				}
			}
		}

	}
}
