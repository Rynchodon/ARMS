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

		public static int Depressurize(IMyCubeGrid grid, int strength, TimeSpan duration)
		{
			AirVentDepressurize av;
			if (!Registrar.TryGetValue(grid, out av))
				av = new AirVentDepressurize(grid);
			return av.AddEffect(duration, strength);
		}

		private readonly Logger m_logger;

		private AirVentDepressurize(IMyCubeGrid grid)
			: base(grid, s_affects)
		{
			m_logger = new Logger(GetType().Name, () => grid.DisplayName);
			Registrar.Add(grid, this);
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
