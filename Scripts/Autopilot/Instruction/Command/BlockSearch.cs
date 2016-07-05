using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using VRage.Utils;
using VRageMath;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class BlockSearch : ACommand
	{
		private StringBuilder m_blockName;
		private Base6Directions.Direction? m_forward, m_upward;

		public override ACommand Clone()
		{
			return new BlockSearch() { m_blockName = m_blockName.Clone(), m_forward = m_forward, m_upward = m_upward };
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

		public override void AddControls(List<Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl> controls)
		{
			MyTerminalControlTextbox<MyShipController> blockName = new MyTerminalControlTextbox<MyShipController>("BlockName", MyStringId.GetOrCompute("Block Name"), MyStringId.GetOrCompute(AddDescription));
			blockName.Getter = block => m_blockName;
			blockName.Setter = (block, value) => m_blockName = value;
			controls.Add(blockName);
		}

		protected override Action<Movement.Mover> Parse(VRage.Game.ModAPI.IMyCubeBlock autopilot, string command, out string message)
		{
			string blockName;
			if (!SplitNameDirections(command, out blockName, out m_forward, out m_upward, out message))
				return null;

			message = null;
			this.m_blockName = new StringBuilder(blockName);
			return mover => mover.NavSet.Settings_Task_NavRot.DestinationBlock = new Data.BlockNameOrientation(blockName, m_forward, m_upward);
		}

		protected override string TermToString(out string message)
		{
			if (string.IsNullOrWhiteSpace(m_blockName.ToString()))
			{
				message = "No block name";
				return null;
			}

			message = null;
			return Identifier + ' ' + m_blockName;
		}
	}
}
