using System;
using System.Collections.Generic;
using VRageMath;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class BlockSearch : ACommand
	{
		private string m_blockName;
		private Base6Directions.Direction? m_forward, m_upward;

		public override ACommand Clone()
		{
			return new BlockSearch() { m_blockName = m_blockName, m_forward = m_forward, m_upward = m_upward };
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
			get { return "Search for " + m_blockName + " on the target grid"; }
		}

		public override bool HasControls
		{
			get { return false; }
		}

		public override void AddControls(List<Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl> controls)
		{
			VRage.Exceptions.ThrowIf<NotImplementedException>(true);
		}

		protected override Action<Movement.Mover> Parse(string command, out string message)
		{
			if (!SplitNameDirections(command, out m_blockName, out m_forward, out m_upward, out message))
				return null;

			message = null;
			return mover => mover.m_navSet.Settings_Task_NavRot.DestinationBlock = new Data.BlockNameOrientation(m_blockName, m_forward, m_upward);
		}

		protected override string TermToString()
		{
			return Identifier + ' ' + m_blockName;
		}
	}
}
