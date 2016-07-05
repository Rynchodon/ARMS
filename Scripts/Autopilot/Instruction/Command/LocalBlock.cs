using System.Collections.Generic;
using System.Text;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public abstract class LocalBlock : ACommand
	{
		protected StringBuilder m_searchBlockName;
		protected IMyCubeBlock m_block;
		protected Base6Directions.Direction? m_forward, m_upward;

		public override void AddControls(List<Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl> controls)
		{
			MyTerminalControlTextbox<MyShipController> blockName = new MyTerminalControlTextbox<MyShipController>(AddName.RemoveWhitespace(), MyStringId.GetOrCompute(AddName), MyStringId.GetOrCompute(AddDescription));
			blockName.Getter = block => m_searchBlockName;
			blockName.Setter = (block, value) => m_searchBlockName = value;
			controls.Add(blockName);
		}

		protected override string TermToString(out string message)
		{
			if (string.IsNullOrWhiteSpace(m_searchBlockName.ToString()))
			{
				message = "No block name";
				return null;
			}

			message = null;
			return Identifier + ' ' + m_searchBlockName;
		}
	}
}
