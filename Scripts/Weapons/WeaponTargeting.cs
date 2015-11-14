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
		[Flags]
		public enum State : byte
		{
			Off = 0,
			/// <summary>Fetch options from name and Update_Options()</summary>
			GetOptions = 1 << 0,
			/// <summary>Indicates targeting is enabled, targeting may not be possible</summary>
			Targeting = GetOptions | 1 << 1
		}

		/// <remarks>
		/// <para>Increasing the number of threads would require locks to be added in many areas.</para>
		/// <para>One thread has no trouble putting enough projectiles into play to slow the game to a crawl.</para>
		/// </remarks>
		private static ThreadManager Thread = new ThreadManager(threadName: "WeaponTargeting");
		private static List<Vector3> obstructionOffsets_turret = new List<Vector3>();
		private static List<Vector3> obstructionOffsets_fixed = new List<Vector3>();

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

		public bool CanControl { get; private set; }

		/// <remarks>Simple turrets can potentially shoot their own grids so they must be treated differently</remarks>
		private readonly bool IsNormalTurret;
		private readonly FastResourceLock lock_Queued = new FastResourceLock();

		private Logger myLogger;
		private State value_AllowedState = State.Off;
		private State value_CurrentState = State.Off;
		private Ammo LoadedAmmo;
		private long UpdateNumber = 0;

		private InterpreterWeapon Interpreter;
		private int InterpreterErrorCount = int.MaxValue;

		protected bool FireWeapon;
		private bool IsFiringWeapon = true;

		private List<IMyEntity> value_ObstructIgnore;
		private readonly FastResourceLock lock_ObstructIgnore = new FastResourceLock();

		private LockedQueue<Action> GameThreadActions = new LockedQueue<Action>(1);

		public WeaponTargeting(IMyCubeBlock weapon)
			: base(weapon)
		{
			if (weapon == null)
				throw new ArgumentNullException("weapon");
			if (!(weapon is IMyTerminalBlock) || !(weapon is IMyFunctionalBlock) || !(weapon is IMyInventoryOwner) || !(weapon is Ingame.IMyUserControllableGun))
				throw new ArgumentException("weapon(" + weapon.DefinitionDisplayNameText + ") is not of correct type");

			this.myTurret = weapon as Ingame.IMyLargeTurretBase;
			this.myLogger = new Logger("WeaponTargeting", weapon);

			this.Interpreter = new InterpreterWeapon(weapon);
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
				if (FireWeapon != IsFiringWeapon)
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

		public State AllowedState
		{
			get { return value_AllowedState; }
			set
			{
				value_AllowedState = value;

				CurrentState &= value;
				FireWeapon = false;
			}
		}

		protected State CurrentState
		{
			get { return value_CurrentState; }
			private set
			{
				if (value_CurrentState == value)
					return;

				myLogger.debugLog("CurrentState changed to " + value, "set_CurrentState()", Logger.severity.DEBUG);
				FireWeapon = false;

				if (IsNormalTurret)
				{
					if ((value & State.Targeting) == State.Targeting) // now targeting
						GameThreadActions.Enqueue(() => myTurret.SetTarget(ProjectilePosition() + CubeBlock.WorldMatrix.Forward * 10));	// disable default targeting
					else // not targeting
						GameThreadActions.Enqueue(() => myTurret.ResetTargetingToDefault());
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

		protected override bool PhysicalProblem(Vector3D targetPos)
		{
			return !CanRotateTo(targetPos) || Obstructed(targetPos);
		}

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
			if (CurrentState_NotFlag(State.Targeting) || LoadedAmmo == null || CurrentTarget.Entity == null || CurrentTarget.Entity.Closed)
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
			{
				myLogger.debugLog("No ammo loaded", "Update10()");
				return;
			}

			UpdateTarget();
		}

		/// <summary>
		/// Gets targeting options from name.
		/// </summary>
		private void Update100()
		{
			UpdateCurrentState();
			if (CurrentState_NotFlag(State.GetOptions))
				return;

			ClearBlacklist();

			if (Interpreter.UpdateInstruction() && Interpreter.Errors.Count <= InterpreterErrorCount)
			{
				Options = Interpreter.Options;
				InterpreterErrorCount = Interpreter.Errors.Count;
				Update_Options(Options);
				myLogger.debugLog("updating Options, Error Count = " + Interpreter.Errors.Count + ", Options: " + Options, "Update100()");
			}
			else
				myLogger.debugLog("not updation Options, Error Count = " + Interpreter.Errors.Count, "Update100()");
			WriteErrors(Interpreter.Errors);
		}

		private void UpdateCurrentState()
		{
			if (!CubeBlock.IsWorking
			|| (IsNormalTurret && myTurret.IsUnderControl)
			|| CubeBlock.OwnerId == 0
			|| (!CubeBlock.DisplayNameText.Contains("[") || !CubeBlock.DisplayNameText.Contains("]")))
			{
				myLogger.debugLog("not working: " + !CubeBlock.IsWorking + ", controlled: " + (IsNormalTurret && myTurret.IsUnderControl) + ", unowned: " + (CubeBlock.OwnerId == 0)
					+ ", missing brackets: " + (!CubeBlock.DisplayNameText.Contains("[") || !CubeBlock.DisplayNameText.Contains("]")), "UpdateCurrentState()");

				CanControl = false;
				CurrentState = State.Off;
				return;
			}

			CanControl = true;
			CurrentState = AllowedState;
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
			if (!CurrentTarget.InterceptionPoint.HasValue)
			{
				FireWeapon = false;
				return;
			}

			Vector3 weaponPosition = ProjectilePosition();

			float distance = LoadedAmmo.AmmoDefinition.MaxTrajectory; // test for obstructions between weapon and max range of weapon

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
			const float firingThreshold = 1.25f;

			myLogger.debugLog("change in direction = " + speed + ", threshold is " + firingThreshold + ", proximity = " + shot.Distance(CurrentTarget.InterceptionPoint.Value) + " shot from " + shot.From + " to " + shot.To, "CheckFire()");

			if (shot.DistanceLessEqual(CurrentTarget.InterceptionPoint.Value, firingThreshold))
			{
				if (Obstructed(finalPosition))
				{
					myLogger.debugLog("final position is obstructed", "CheckFire()");
					if (speed < 0.01)
					{
						myLogger.debugLog("blacklisting: " + CurrentTarget.Entity.getBestName(), "CheckFire()");
						BlacklistTarget();
					}
					FireWeapon = false;
				}
				else
					FireWeapon = true;
			}
			else
				FireWeapon = false;
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
						if (entity.WorldAABB.Intersects(new LineD(testLine.From, testLine.To), out distance))
						{
							myLogger.debugLog("from " + testLine.From + " to " + testLine.To + "obstructed by character: " + entity.getBestName(), "Obstructed()");
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
