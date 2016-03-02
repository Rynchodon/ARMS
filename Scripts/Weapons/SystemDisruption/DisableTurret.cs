using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.ObjectBuilders;

namespace Rynchodon.Weapons.SystemDisruption
{
	public class DisableTurret : Disruption
	{

		protected override MyObjectBuilderType[] BlocksAffected
		{
			get { return new MyObjectBuilderType[] { typeof(MyObjectBuilder_LargeGatlingTurret), typeof(MyObjectBuilder_LargeMissileTurret), typeof(MyObjectBuilder_InteriorTurret) }; }
		}

		protected override int MinCost { get { return 15; } }

		protected override void StartEffect(IMyCubeBlock block)
		{
			(block as IMyFunctionalBlock).RequestEnable(false);
		}

		protected override void EndEffect(IMyCubeBlock block)
		{
			(block as IMyFunctionalBlock).RequestEnable(true);
		}

	}
}
