using System;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.ObjectBuilders;

namespace Rynchodon.Weapons.SystemDisruption
{
	public class TraitorTurret : Disruption
	{

		private static readonly MyObjectBuilderType[] s_affects = new MyObjectBuilderType[] 
		{ typeof(MyObjectBuilder_LargeGatlingTurret), typeof(MyObjectBuilder_LargeMissileTurret), typeof(MyObjectBuilder_InteriorTurret) };

		public static void Update()
		{
			Registrar.ForEach((TraitorTurret tt) => tt.UpdateEffect());
		}

		public static int TurnTurrets(IMyCubeGrid grid, int strength, TimeSpan duration, long effectOwner)
		{
			TraitorTurret tt;
			if (!Registrar.TryGetValue(grid, out tt))
				tt = new TraitorTurret(grid);
			return tt.AddEffect(duration, strength, effectOwner);
		}

		private readonly Logger m_logger;

		protected override int MinCost { get { return 40; } }

		private TraitorTurret(IMyCubeGrid grid)
			: base(grid, s_affects)
		{
			m_logger = new Logger(GetType().Name, grid);
			Registrar.Add(grid, this);
		}

		protected override int StartEffect(IMyCubeBlock block, int strength)
		{
			m_logger.debugLog("Turning: " + block.DisplayNameText + ", remaining strength: " + (strength - MinCost), "StartEffect()");
			return MinCost;
		}

		protected override int EndEffect(IMyCubeBlock block, int strength)
		{
			m_logger.debugLog("Restoring: " + block.DisplayNameText + ", remaining strength: " + (strength - MinCost), "StartEffect()");
			return MinCost;
		}

	}
}
