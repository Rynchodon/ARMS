using System.Text;
using Rynchodon.Autopilot.Movement;
using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Instruction.Command
{
	/// <summary>
	/// Common for finding a block on autopilot's ship
	/// </summary>
	public abstract class ALocalBlock : ABlockSearch
	{
		protected IMyCubeBlock m_block;

		protected override sealed AutopilotActionList.AutopilotAction Parse(IMyCubeBlock autopilot, string command, out string message)
		{
			string blockName;
			Base6Directions.Direction? forward, upward;
			if (!SplitNameDirections(command, out blockName, out forward, out upward, out message) || !GetLocalBlock(autopilot, blockName, out m_block, out message))
				return null;

			message = null;
			m_searchBlockName = new StringBuilder(blockName);
			m_forward = forward;
			m_upward = upward;

			return ActionMethod;
		}

	}
}
