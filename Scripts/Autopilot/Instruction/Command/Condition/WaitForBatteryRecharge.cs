using Rynchodon.Attached;
using Rynchodon.Autopilot.Navigator;
using Rynchodon.Autopilot.Pathfinding;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class WaitForBatteryRecharge : ASingleWord
	{
		public override ACommand Clone()
		{
			return new WaitForBatteryRecharge();
		}

		public override string Identifier
		{
			get { return "chargebattery"; }
		}

		public override string AddDescription
		{
			get { return "Wait for battery to recharge"; }
		}

		protected override void ActionMethod(Pathfinder pathfinder)
		{
			new WaitForCondition(pathfinder, () => BatteriesCharged(pathfinder.Mover.Block.CubeGrid), "Waiting for battery to recharge");
		}

		private bool BatteriesCharged(IMyCubeGrid startGrid)
		{
			foreach (IMyCubeGrid attachedGrid in AttachedGrid.AttachedGrids(startGrid, AttachedGrid.AttachmentKind.Permanent, true))
			{
				CubeGridCache cache = CubeGridCache.GetFor(attachedGrid);
				if (cache == null)
					return false;
				foreach (IMyBatteryBlock battery in cache.BlocksOfType(typeof(MyObjectBuilder_BatteryBlock)))
					if (battery.IsCharging)
						return false;
			}
			Logger.DebugLog("All batteries are recharged", Logger.severity.DEBUG);
			return true;
		}
	}
}
