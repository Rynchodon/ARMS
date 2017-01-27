using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;

namespace Rynchodon.Weapons.SystemDisruption
{
	public class DisableTurret : Disruption
	{

		protected override MyObjectBuilderType[] BlocksAffected
		{
			get { return new MyObjectBuilderType[] { typeof(MyObjectBuilder_LargeGatlingTurret), typeof(MyObjectBuilder_LargeMissileTurret), typeof(MyObjectBuilder_InteriorTurret) }; }
		}

		protected override float MinCost { get { return 15f; } }

		protected override void StartEffect(IMyCubeBlock block)
		{
			(block as MyFunctionalBlock).Enabled = false;
		}

		protected override void EndEffect(IMyCubeBlock block)
		{
			(block as MyFunctionalBlock).Enabled = true;
		}

	}
}
