using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Navigator;
using Sandbox.ModAPI;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class NavigationBlock : LocalBlock
	{
		public override ACommand Clone()
		{
			return new NavigationBlock() { m_searchBlockName = m_searchBlockName.Clone(), m_forward = m_forward, m_upward = m_upward };
		}

		public override string Identifier
		{
			get { return "n"; }
		}

		public override string AddName
		{
			get { return "Navigation Block"; }
		}

		public override string AddDescription
		{
			get { return "Block to navigate by or face towards its target"; }
		}

		public override string Description
		{
			get
			{
				if (m_block == null)
					return "Missing block!";
				if (m_block is SpaceEngineers.Game.ModAPI.Ingame.IMySolarPanel || m_block is SpaceEngineers.Game.ModAPI.Ingame.IMyOxygenFarm)
					return "Face " + m_block.DisplayNameText + " towards the sun";
				if (m_block is IMyLaserAntenna)
					return "Face " + m_block.DisplayNameText + " towards its target";
				return "Bring " + m_block.DisplayNameText + " to the destination";
			}
		}

		protected override System.Action<Movement.Mover> Parse(VRage.Game.ModAPI.IMyCubeBlock autopilot, string command, out string message)
		{
			string blockName;
			if (!SplitNameDirections(command, out blockName, out m_forward, out m_upward, out message))
				return null;

			if (!GetLocalBlock(autopilot, blockName, out m_block, out message))
				return null;

			message = null;
			m_searchBlockName = new StringBuilder(blockName);
			return mover => {
				PseudoBlock pseudo = new PseudoBlock(m_block, m_forward, m_upward);
				if (m_block is IMyLaserAntenna || m_block is SpaceEngineers.Game.ModAPI.Ingame.IMySolarPanel || m_block is SpaceEngineers.Game.ModAPI.Ingame.IMyOxygenFarm)
					new Facer(mover, pseudo);
				else
					mover.NavSet.Settings_Task_NavRot.NavigationBlock = pseudo;
			};
		}

	}
}
