
using Sandbox.Common.ObjectBuilders;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using Ingame = SpaceEngineers.Game.ModAPI.Ingame;

namespace Rynchodon.Weapons.SystemDisruption
{
	public class AirVentDepressurize : Disruption
	{

		protected override MyObjectBuilderType[] BlocksAffected
		{
			get { return new MyObjectBuilderType[] { typeof(MyObjectBuilder_AirVent) }; }
		}

		protected override bool CanDisrupt(IMyCubeBlock block)
		{
			return !(block as Ingame.IMyAirVent).IsDepressurizing;
		}

		protected override void StartEffect(IMyCubeBlock block)
		{
			(block as Ingame.IMyAirVent).ApplyAction("Depressurize");
		}

		protected override void EndEffect(IMyCubeBlock block)
		{
			(block as Ingame.IMyAirVent).ApplyAction("Depressurize");
		}

	}
}
