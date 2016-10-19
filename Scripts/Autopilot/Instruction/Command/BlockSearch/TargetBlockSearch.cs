using System.Text;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class TargetBlockSearch : ABlockSearch
	{
		public override ACommand Clone()
		{
			return new TargetBlockSearch() { m_searchBlockName = m_searchBlockName.Clone(), m_forward = m_forward, m_upward = m_upward };
		}

		public override string Identifier
		{
			get { return "b"; }
		}

		public override string AddName
		{
			get { return "Target Block"; }
		}

		public override string AddDescription
		{
			get { return "Block to search for on the target"; }
		}

		public override string Description
		{
			get { return "Search for " + m_searchBlockName + " on the target grid"; }
		}

		public override void AppendCustomInfo(StringBuilder sb)
		{
			sb.AppendLine("For navigating to a specific block on the target ship, only affects the next use of G");
		}

		protected override void ActionMethod(Pathfinding.Pathfinder pathfinder)
		{
			pathfinder.NavSet.Settings_Task_NavRot.DestinationBlock = new Data.BlockNameOrientation(m_searchBlockName.ToString(), m_forward, m_upward); 
		}

	}
}
