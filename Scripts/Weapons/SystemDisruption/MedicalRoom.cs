using System;

using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.ObjectBuilders;

namespace Rynchodon.Weapons.SystemDisruption
{
	public class MedicalRoom : Disruption
	{

		private static readonly MyObjectBuilderType[] s_affects = new MyObjectBuilderType[] { typeof(MyObjectBuilder_MedicalRoom) };

		public static void Update()
		{
			Registrar.ForEach((MedicalRoom mr) => mr.UpdateEffect());
		}

		public static int Hijack(IMyCubeGrid grid, int strength, TimeSpan duration, long effectOwner)
		{
			MedicalRoom mr;
			if (!Registrar.TryGetValue(grid, out mr))
				mr = new MedicalRoom(grid);
			return mr.AddEffect(duration, strength, effectOwner);
		}

		private readonly Logger m_logger;

		private MedicalRoom(IMyCubeGrid grid)
			: base(grid, s_affects)
		{
			m_logger = new Logger(GetType().Name, grid);
			Registrar.Add(grid, this);
		}

		protected override int StartEffect(IMyCubeBlock block, int strength)
		{
			m_logger.debugLog("Hijacking: " + block.DisplayNameText + ", remaining strength: " + (strength - MinCost), "StartEffect()");
			return MinCost;
		}

		protected override int EndEffect(IMyCubeBlock block, int strength)
		{
			m_logger.debugLog("Restoring: " + block.DisplayNameText + ", remaining strength: " + (strength - MinCost), "StartEffect()");
			return MinCost;
		}

	}
}
