using Rynchodon.Autopilot.Navigator;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class UnlandBlock : ALocalBlock
	{

		public override ACommand Clone()
		{
			return new UnlandBlock() { m_searchBlockName = m_searchBlockName.Clone(), m_forward = m_forward, m_upward = m_upward };
		}

		public override string Identifier
		{
			get { return "u"; }
		}

		public override string AddName
		{
			get { return "Unland Block"; }
		}

		public override string AddDescription
		{
			get { return "Unlock a specific block and move away from the attached entity."; }
		}

		public override string Description
		{
			get
			{
				if (m_block.NullOrClosed())
					return "Missing block!";
				return "Unlock " + m_block.DisplayNameText + " and move away from the attached entity.";
			}
		}

		protected override void ActionMethod(Pathfinding.Pathfinder pathfinder)
		{
			new UnLander(pathfinder, new Data.PseudoBlock(m_block, m_forward, m_upward));
		}
	}
}
