
using System;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;

namespace Rynchodon.Weapons.SystemDisruption
{
	public class JumpDriveDrain: Disruption
	{

		protected override MyObjectBuilderType[] BlocksAffected
		{
			get { return new MyObjectBuilderType[] { typeof(MyObjectBuilder_JumpDrive) }; }
		}

		protected override bool CanDisrupt(IMyCubeBlock block)
		{
			IMyJumpDrive jumpDrive = (IMyJumpDrive)block;
			float chargedRatio = jumpDrive.CurrentStoredPower / jumpDrive.MaxStoredPower;
			return chargedRatio > 0.5f;
		}

		protected override void StartEffect(IMyCubeBlock block)
		{
			IMyJumpDrive jumpDrive = (IMyJumpDrive)block;
			float maxChargeRateMW = jumpDrive.ResourceSink.MaxRequiredInputByType(Globals.Electricity);
			float maxDrainMWs = 1.1f * maxChargeRateMW * (float)Hacker.s_hackLength.TotalSeconds;
			float chargedRatio = jumpDrive.CurrentStoredPower / jumpDrive.MaxStoredPower;
			float drainMWh = maxDrainMWs * chargedRatio / 3600;

			jumpDrive.CurrentStoredPower = Math.Max(jumpDrive.CurrentStoredPower - drainMWh, 0f);
		}

	}
}
