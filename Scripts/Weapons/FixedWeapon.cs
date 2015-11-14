using System;
using System.Collections.Generic;
using Rynchodon.Attached;
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

		private bool ControllingEngager;
		private MotorTurret MyMotorTurret = null;

		private readonly Logger myLogger;
		private readonly bool AllowFighterControl;

		public FixedWeapon(IMyCubeBlock block)
			: base(block)
		{
			myLogger = new Logger("FixedWeapon", block);
			Registrar.Add(CubeBlock, this);
			myLogger.debugLog("Initialized", "FixedWeapon()");

			AllowFighterControl = WeaponDescription.GetFor(block).AllowFighterControl;

			AllowedState = State.GetOptions;
		}

		/// <summary>
		/// <para>If this FixedWeapon is already being controlled, returns if the given controller is controlling it.</para>
		/// <para>Otherwise, attempts to take control of this FixedWeapon. Control requires that targeting options be set-up and turret not set.</para>
		/// </summary>
		public bool EngagerTakeControl()
		{
			if (AllowFighterControl && CanControl && MyMotorTurret == null)
			{
				myLogger.debugLog("engager takes control", "EngagerTakeControl()");
				ControllingEngager = true;
				AllowedState = State.Targeting;
				return true;
			}

			return false;
		}

		public void EngagerReleaseControl()
		{
			ControllingEngager = false;
			AllowedState = State.Off;
		}

		/// <summary>
		/// Fires the weapon as it bears.
		/// </summary>
		protected override void Update()
		{
			if (CurrentState_NotFlag(State.Targeting))
				return;

			// CurrentTarget may be changed by WeaponTargeting
			Target GotTarget = CurrentTarget;
			if (GotTarget.Entity == null)
			{
				FireWeapon = false;
				if (MyMotorTurret != null)
					MyMotorTurret.Stop();
				return;
			}
			if (!GotTarget.FiringDirection.HasValue || !GotTarget.InterceptionPoint.HasValue) // happens alot
				return;

			CheckFire(CubeBlock.WorldMatrix.Forward);

			if (MyMotorTurret != null)
			{
				RelativeDirection3F FiringDirection = RelativeDirection3F.FromWorld(CubeBlock.CubeGrid, GotTarget.FiringDirection.Value);
				MyMotorTurret.FaceTowards(FiringDirection);
			}
		}

		protected override void Update_Options(TargetingOptions current)
		{
			if (ControllingEngager)
				return;

			//myLogger.debugLog("Turret flag: " + current.FlagSet(TargetingFlags.Turret) + ", No motor turret: " + (MyMotorTurret == null) + ", CanControl = " + CanControl, "Update_Options()");
			if (current.FlagSet(TargetingFlags.Turret))
			{
				if (MyMotorTurret == null && CanControl)
				{
					myLogger.debugLog("Turret is now enabled", "Update_Options()", Logger.severity.INFO);
					MyMotorTurret = new MotorTurret(CubeBlock, MyMotorTurret_OnStatorChange);
					AllowedState = State.Targeting;
				}
			}
			else
			{
				if (MyMotorTurret != null)
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
			if (ControllingEngager)
				return true;

			// if controlled by a turret (not implemented)
			return true;
		}

		/// <summary>
		/// Creates an obstruction list from stators and rotors.
		/// </summary>
		private void MyMotorTurret_OnStatorChange(IMyMotorStator statorEl, IMyMotorStator statorAz)
		{
			myLogger.debugLog("entered MyMotorTurret_OnStatorChange()", "MyMotorTurret_OnStatorChange()");

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

				IMyCubeBlock statorAzRotor;
				if (StatorRotor.TryGetRotor(statorAz, out statorAzRotor))
				{
					myLogger.debugLog("added statorAzRotor: " + statorAzRotor.getBestName(), "MyMotorTurret_OnStatorChange()");
					ignore.Add(statorAzRotor);
				}
			}
			ObstructionIgnore = ignore;
		}
	}
}
