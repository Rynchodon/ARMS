
using Sandbox.Common.ObjectBuilders;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;

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
			IMyAirVent vent = (IMyAirVent)block;
			return !vent.Depressurize && vent.CanPressurize;
		}

		protected override void StartEffect(IMyCubeBlock block)
		{
			((IMyAirVent)block).Depressurize = true;
		}

		protected override void EndEffect(IMyCubeBlock block)
		{
			((IMyAirVent)block).Depressurize = false;
		}

	}
}
