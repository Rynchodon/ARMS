using System;

using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.ObjectBuilders;

namespace Rynchodon.Weapons.SystemDisruption
{
	public class JumpDriveDrain: Disruption
	{

		private static readonly MyObjectBuilderType[] s_affects = new MyObjectBuilderType[] { typeof(MyObjectBuilder_JumpDrive) };

		public static void Update()
		{
			Registrar.ForEach((JumpDriveDrain jdd) => jdd.UpdateEffect());
		}

		public static int DrainJumpers(IMyCubeGrid grid, int strength, TimeSpan duration)
		{
			JumpDriveDrain jdd;
			if (!Registrar.TryGetValue(grid, out jdd))
				jdd = new JumpDriveDrain(grid);
			return jdd.AddEffect(duration, strength);
		}

		private readonly Logger m_logger;

		private JumpDriveDrain(IMyCubeGrid grid)
			: base(grid, s_affects)
		{
			m_logger = new Logger(GetType().Name, grid);
			Registrar.Add(grid, this);
		}

		protected override int StartEffect(IMyCubeBlock block, int strength)
		{
			MyJumpDrive drive = (block as MyJumpDrive);
			if (!drive.IsFull)
				return 0;

			m_logger.debugLog("Draining: " + block.DisplayNameText + ", remaining strength: " + (strength - MinCost), "StartEffect()");
			drive.SetStoredPower(0.9f);
			return MinCost;
		}

		protected override int EndEffect(IMyCubeBlock block, int strength)
		{
			m_logger.debugLog("Restoring Control: " + block.DisplayNameText + ", remaining strength: " + (strength - MinCost), "StartEffect()");
			return MinCost;
		}

	}
}
