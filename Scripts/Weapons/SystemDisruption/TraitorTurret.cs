using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;

namespace Rynchodon.Weapons.SystemDisruption
{
	public class TraitorTurret : Disruption
	{

		protected override MyObjectBuilderType[] BlocksAffected
		{
			get { return new MyObjectBuilderType[] { typeof(MyObjectBuilder_LargeGatlingTurret), typeof(MyObjectBuilder_LargeMissileTurret), typeof(MyObjectBuilder_InteriorTurret) }; }
		}

		protected override int MinCost { get { return 40; } }

		protected override bool EffectOwnerCanAccess
		{
			get { return true; }
		}

		protected override void EndEffect(IMyCubeBlock block)
		{
			// stop turret from shooting its current target
			(block as IMyFunctionalBlock).RequestEnable(false);
			block.ApplyAction("OnOff_On");
		}

	}
}
