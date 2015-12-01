using System;

using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.ObjectBuilders;

namespace Rynchodon.Weapons.SystemDisruption
{
	public class CryoChamberMurder : Disruption
	{

		private static readonly MyObjectBuilderType[] s_affects = new MyObjectBuilderType[] { typeof(MyObjectBuilder_CryoChamber) };

		public static void Update()
		{
			Registrar.ForEach((CryoChamberMurder ccm) => ccm.UpdateEffect());
		}

		public static int MurderPeeps(IMyCubeGrid grid, int strength, TimeSpan duration)
		{
			CryoChamberMurder ccm;
			if (!Registrar.TryGetValue(grid, out ccm))
				ccm = new CryoChamberMurder(grid);
			return ccm.AddEffect(duration, strength);
		}

		private readonly Logger m_logger;

		private CryoChamberMurder(IMyCubeGrid grid)
			: base(grid, s_affects)
		{
			m_logger = new Logger(GetType().Name, grid);
			Registrar.Add(grid, this);
		}

		protected override int StartEffect(IMyCubeBlock block, int strength)
		{
			IMyCharacter pilot = (block as MyCockpit).Pilot as IMyCharacter;
			if (pilot == null)
				return 0;

			m_logger.debugLog("Killing: " + pilot + ", in " + block.DisplayNameText + ", remaining strength: " + (strength - MinCost), "StartEffect()");
			pilot.Kill();
			return MinCost;
		}

		protected override int EndEffect(IMyCubeBlock block, int strength)
		{
			m_logger.debugLog("Restoring: " + block.DisplayNameText + ", remaining strength: " + (strength - MinCost), "StartEffect()");
			return MinCost;
		}

	}
}
