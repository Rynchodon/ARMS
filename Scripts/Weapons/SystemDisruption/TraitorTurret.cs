using System;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.ObjectBuilders;
using Ingame = Sandbox.ModAPI.Ingame;

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

		protected override void EndEffect(IMyCubeBlock block)
		{
			// stop turret from shooting its current target
			(block as IMyFunctionalBlock).RequestEnable(false);
			block.ApplyAction("OnOff_On");
		}

	}
}
