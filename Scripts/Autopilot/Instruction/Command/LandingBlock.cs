using System;
using System.Text;
using Rynchodon.Autopilot.Data;
using VRageMath;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class LandingBlock : LocalBlock
	{

		public override ACommand Clone()
		{
			return new LandingBlock() { m_searchBlockName = m_searchBlockName.Clone(), m_forward = m_forward, m_upward = m_upward };
		}

		public override string Identifier
		{
			get { return "l"; }
		}

		public override string AddName
		{
			get { return "Landing Block"; }
		}

		public override string AddDescription
		{
			get { return "Block that will be landed on the target ship"; }
		}

		public override string Description
		{
			get
			{
				if (m_block == null)
					return "Missing block!";
				return "Land " + m_block.DisplayNameText + " on the target ship";
			}
		}

		protected override Action<Movement.Mover> Parse(VRage.Game.ModAPI.IMyCubeBlock autopilot, string command, out string message)
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
				mover.NavSet.Settings_Task_NavRot.LandingBlock = pseudo;
				mover.NavSet.LastLandingBlock = pseudo;
			};
		}

	}
}
