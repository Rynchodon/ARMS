
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
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
			return (block as MyJumpDrive).IsFull;
		}

		protected override void StartEffect(IMyCubeBlock block)
		{
			(block as MyJumpDrive).SetStoredPower(0.9f);
		}

	}
}
