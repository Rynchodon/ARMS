using System;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.ObjectBuilders;

namespace Rynchodon.Weapons.SystemDisruption
{
	public class DisableTurret : Disruption
	{

		private static readonly MyObjectBuilderType[] s_affects = new MyObjectBuilderType[] 
		{ typeof(MyObjectBuilder_LargeGatlingTurret), typeof(MyObjectBuilder_LargeMissileTurret), typeof(MyObjectBuilder_InteriorTurret) };

		public static void Update()
		{
			Registrar.ForEach((DisableTurret dt) => dt.UpdateEffect());
		}

		public static int DisableTurrets(IMyCubeGrid grid, int strength, TimeSpan duration)
		{
			DisableTurret dt;
			if (!Registrar.TryGetValue(grid, out dt))
				dt = new DisableTurret(grid);
			return dt.AddEffect(duration, strength);
		}

		private readonly Logger m_logger;

		protected override int MinCost { get { return 15; } }

		private DisableTurret(IMyCubeGrid grid)
			: base(grid, s_affects)
		{
			m_logger = new Logger(GetType().Name, grid);
			Registrar.Add(grid, this);
		}

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
