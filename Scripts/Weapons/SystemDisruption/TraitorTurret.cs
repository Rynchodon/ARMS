using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities.Cube;
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

		protected override float MinCost { get { return 40f; } }

		protected override bool EffectOwnerCanAccess
		{
			get { return true; }
		}

		protected override void StartEffect(IMyCubeBlock block)
		{
			// stop turret from shooting its current target
			((MyFunctionalBlock)block).Enabled = false;
			block.ApplyAction("OnOff_On");
		}

		protected override void EndEffect(IMyCubeBlock block)
		{
			// stop turret from shooting its current target
			((MyFunctionalBlock)block).Enabled = false;
			block.ApplyAction("OnOff_On");
		}

	}
}
