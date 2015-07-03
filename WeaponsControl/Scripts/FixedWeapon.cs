using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.ModAPI;

namespace Rynchodon.Weapons
{
	/// <summary>
	/// For rotor-turrets and Autopilot-usable weapons.
	/// </summary>
	public class FixedWeapon : WeaponTargeting
	{
		private static Dictionary<IMyCubeBlock, FixedWeapon> registry = new Dictionary<IMyCubeBlock, FixedWeapon>();

		/// <remarks>Before becoming a turret this will need to be checked.</remarks>
		private Engager ControllingEngager = null;

		private bool TurretFlagSet = false;

		private Logger myLogger;

		public FixedWeapon(IMyCubeBlock block)
			: base(block)
		{
			myLogger = new Logger("FixedWeapon", block);
			registry.Add(CubeBlock, this);
			CubeBlock.OnClose += weapon_OnClose;
		}

		private void weapon_OnClose(IMyEntity obj)
		{ registry.Remove(CubeBlock); }

		internal static FixedWeapon GetFor(IMyCubeBlock weapon)
		{ return registry[weapon]; }

		/// <summary>
		/// <para>If this FixedWeapon is already being controlled, returns if the given controller is controlling it.</para>
		/// <para>Otherwise, attempts to take control of this FixedWeapon. Control requires that targeting options be set-up and turret not set.</para>
		/// </summary>
		internal bool EngagerTakeControl(Engager controller)
		{
			if (ControllingEngager != null)
				return ControllingEngager == controller;

			if (CanControlWeapon(false) && !TurretFlagSet)// && Options.TargetingRange > 0)
			{
				myLogger.debugLog("no issues", "EngagerTakeControl()");
				ControllingEngager = controller;
				EnableWeaponTargeting();
				return true;
			}
			//if (IsControllingWeapon)
			//	myLogger.debugLog("turret flag set", "EngagerTakeControl()");
			//else
			//	myLogger.debugLog("not controlling weapon", "EngagerTakeControl()");

			return false;
		}

		internal void EngagerReleaseControl(Engager controller)
		{
			if (ControllingEngager != controller)
				throw new InvalidOperationException("Engager does not have authority to release control");
			ControllingEngager = null;
			DisableWeaponTargeting();
		}

		protected override bool CanRotateTo(VRageMath.Vector3D targetPoint)
		{
			// if controlled by an engager, can always rotate
			if (ControllingEngager != null)
				return true;

			// if controlled by a turret (not implemented)
			return false;
		}

		protected override void Update_Options(TargetingOptions current)
		{ TurretFlagSet = current.FlagSet(TargetingFlags.Turret); }

		/// <summary>
		/// Fires the weapon as it bears.
		/// </summary>
		protected override void Update()
		{
			if (!IsControllingWeapon)
				return;

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
		}
	}
}
