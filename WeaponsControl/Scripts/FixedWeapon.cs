using System;
using System.Collections.Generic;
using Rynchodon.AttachedGrid;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Weapons
{
	/// <summary>
	/// For rotor-turrets and Autopilot-usable weapons.
	/// </summary>
	public class FixedWeapon : WeaponTargeting
	{
		private static Dictionary<IMyCubeBlock, FixedWeapon> registry = new Dictionary<IMyCubeBlock, FixedWeapon>();
		private static readonly FastResourceLock lock_registry = new FastResourceLock();

		/// <remarks>Before becoming a turret this will need to be checked.</remarks>
		private Engager ControllingEngager = null;
		private MotorTurret MyMotorTurret = null;

		private Logger myLogger;

		public FixedWeapon(IMyCubeBlock block)
			: base(block)
		{
			myLogger = new Logger("FixedWeapon", block);
			using (lock_registry.AcquireExclusiveUsing())
				registry.Add(CubeBlock, this);
			CubeBlock.OnClose += weapon_OnClose;
			myLogger.debugLog("Initialized", "FixedWeapon()");

			AllowedState = State.GetOptions;
		}

		private void weapon_OnClose(IMyEntity obj)
		{
			myLogger.debugLog("entered weapon_OnClose()", "weapon_OnClose()");

			using (lock_registry.AcquireExclusiveUsing())
				registry.Remove(CubeBlock);

			myLogger.debugLog("leaving weapon_OnClose()", "weapon_OnClose()");
		}

		internal static FixedWeapon GetFor(IMyCubeBlock weapon)
		{
			using (lock_registry.AcquireSharedUsing())
				return registry[weapon];
		}

		/// <summary>
		/// <para>If this FixedWeapon is already being controlled, returns if the given controller is controlling it.</para>
		/// <para>Otherwise, attempts to take control of this FixedWeapon. Control requires that targeting options be set-up and turret not set.</para>
		/// </summary>
		internal bool EngagerTakeControl(Engager controller)
		{
			if (ControllingEngager != null)
				return ControllingEngager == controller;

			if (CanControl && MyMotorTurret == null)
			{
				myLogger.debugLog("no issues", "EngagerTakeControl()");
				ControllingEngager = controller;
				AllowedState = State.On;
				return true;
			}

			return false;
		}

		internal void EngagerReleaseControl(Engager controller)
		{
			if (ControllingEngager != controller)
				throw new InvalidOperationException("Engager does not have authority to release control");
			ControllingEngager = null;
			AllowedState = State.Off;
		}

		/// <summary>
		/// Fires the weapon as it bears.
		/// </summary>
		protected override void Update()
		{
			if (CurrentState_NotFlag(State.Targeting))
			{
				//if (MyMotorTurret != null)
				//{
				//	myLogger.debugLog("Turret is disabled", "Update()", Logger.severity.INFO);
				//	MyMotorTurret = null;
				//	AllowedState = State.Off;
				//}
				return;
			}

			// CurrentTarget may be changed by WeaponTargeting
			Target GotTarget = CurrentTarget;
			if (GotTarget.Entity == null)
			{
				StopFiring("No target.");
				return;
			}
			if (!GotTarget.FiringDirection.HasValue || !GotTarget.InterceptionPoint.HasValue) // happens alot
				return;

			CheckFire(CubeBlock.WorldMatrix.Forward);

			if (MyMotorTurret != null && MyMotorTurret.StatorAz != null && MyMotorTurret.StatorEl != null && CurrentTarget.InterceptionPoint.HasValue)
			{

				//Vector3 offset = MyMotorTurret.StatorAz.GetPosition() - CubeBlock.GetPosition();
				//Vector3 offsetTarget = CurrentTarget.InterceptionPoint.Value + offset;
				//RelativeDirection3F direction = RelativeDirection3F.FromWorld(

				RelativeDirection3F FiringDirection = RelativeDirection3F.FromWorld(CubeBlock.CubeGrid, CurrentTarget.FiringDirection.Value);
				MyMotorTurret.FaceTowards(FiringDirection);
			}
		}

		protected override void Update_Options(TargetingOptions current)
		{
			if (ControllingEngager != null)
				return;

			if (current.FlagSet(TargetingFlags.Turret))
			{
				if (MyMotorTurret == null && CanControl)
				{
					myLogger.debugLog("Turret is now enabled", "Update_Options()", Logger.severity.INFO);
					MyMotorTurret = new MotorTurret(CubeBlock);
					MyMotorTurret.OnStatorChange = MyMotorTurret_OnStatorChange;
					AllowedState = State.On;
				}
			}
			else
			{
				if (MyMotorTurret != null || !CanControl)
				{
					myLogger.debugLog("Turret is now disabled", "Update_Options()", Logger.severity.INFO);
					MyMotorTurret = null; // MyMotorTurret will not be updated, so it will be recreated later incase something weird happens to motors
					AllowedState = State.GetOptions;
				}
			}
		}

		protected override bool CanRotateTo(VRageMath.Vector3D targetPoint)
		{
			// if controlled by an engager, can always rotate
			if (ControllingEngager != null)
				return true;

			// if controlled by a turret (not implemented)
			return true;
		}

		/// <summary>
		/// Creates an obstruction list from stators and rotors.
		/// </summary>
		private void MyMotorTurret_OnStatorChange(IMyMotorStator statorEl, IMyMotorStator statorAz)
		{
			//List<IMySlimBlock> ignore = new List<IMySlimBlock>();

			//foreach (IMyMotorStator stator in new IMyMotorStator[] { statorEl, statorAz })
			//	if (stator != null)
			//	{
			//		ignore.Add((stator as IMyCubeBlock).getSlim());
			//		IMyCubeBlock rotor;
			//		if (StatorRotor.TryGetRotor(stator, out rotor))
			//			ignore.Add(rotor.getSlim());
			//	}

			//myLogger.debugLog("updated ObstructionIgnore, " + ignore.Count + "entries", "MyMotorTurret_OnStatorChange()", Logger.severity.DEBUG);
			//ObstructionIgnore = ignore;

			//ObstructionIgnore = statorEl.CubeGrid as IMyCubeGrid;

			List<IMyEntity> ignore = new List<IMyEntity>();
			if (statorEl != null)
			{
				myLogger.debugLog("added statorEl.CubeGrid: " + statorEl.CubeGrid.getBestName(), "MyMotorTurret_OnStatorChange()");
				ignore.Add(statorEl.CubeGrid);
			}
			if (statorAz != null)
			{
				myLogger.debugLog("added statorAz: " + statorAz.getBestName(), "MyMotorTurret_OnStatorChange()");
				ignore.Add(statorAz);
			}
			ObstructionIgnore = ignore;
		}
	}
}
