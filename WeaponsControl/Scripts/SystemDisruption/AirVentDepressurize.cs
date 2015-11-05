using System;

using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.ObjectBuilders;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Weapons.SystemDisruption
{
	public class AirVentDepressurize : Disruption
	{

		private static readonly MyObjectBuilderType[] s_affects = new MyObjectBuilderType[] { typeof(MyObjectBuilder_AirVent) };

		public static void Update()
		{
			Registrar.ForEach((AirVentDepressurize av) => av.UpdateEffect());
		}

		public static int Depressurize(IMyCubeGrid grid, int strength, TimeSpan duration, long effectOwner)
		{
			AirVentDepressurize av;
			if (!Registrar.TryGetValue(grid, out av))
				av = new AirVentDepressurize(grid);
			return av.AddEffect(duration, strength, effectOwner);
		}

		private readonly Logger m_logger;

		private AirVentDepressurize(IMyCubeGrid grid)
			: base(grid, s_affects)
		{
			m_logger = new Logger(GetType().Name, () => grid.DisplayName);
			Registrar.Add(grid, this);
		}

		protected override int StartEffect(IMyCubeBlock block, int strength)
		{
			Ingame.IMyAirVent airVent = block as Ingame.IMyAirVent;
			if (airVent.IsDepressurizing)
				return 0;
			m_logger.debugLog("Depressurizing: " + block.DisplayNameText + ", remaining strength: " + (strength - 1), "StartEffect()");
			airVent.ApplyAction("Depressurize");
			return 1;
		}

		protected override int EndEffect(IMyCubeBlock block, int strength)
		{
			m_logger.debugLog("No longer depressurizing: " + block.DisplayNameText + ", remaining strength: " + (strength - 1), "EndEffect()");
			Ingame.IMyAirVent airVent = block as Ingame.IMyAirVent;
			if (airVent.IsDepressurizing)
				airVent.ApplyAction("Depressurize");
			return 1;
		}

	}
}
