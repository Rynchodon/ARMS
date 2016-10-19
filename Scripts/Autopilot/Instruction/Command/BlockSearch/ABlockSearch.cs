using System.Collections.Generic;
using System.Text;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using VRage.Utils;
using VRageMath;

namespace Rynchodon.Autopilot.Instruction.Command
{
	/// <summary>
	/// Common for commands that take a block name and two directions.
	/// </summary>
	public abstract class ABlockSearch : ACommand
	{
		protected StringBuilder m_searchBlockName;

		private DirectionSelection value_forwardSelector, value_upwardSelector;

		private DirectionSelection m_forwardSelector
		{
			get
			{
				if (value_forwardSelector == null)
					value_forwardSelector = new DirectionSelection("Forward", "Forward direction", null);
				return value_forwardSelector;
			}
		}

		private DirectionSelection m_upwardSelector
		{
			get
			{
				if (value_upwardSelector == null)
					value_upwardSelector = new DirectionSelection("Upward", "Upward direction", null);
				return value_upwardSelector;
			}
		}

		protected Base6Directions.Direction? m_forward
		{
			get { return value_forwardSelector == null ? null : value_forwardSelector.m_selectedDirection; }
			set
			{
				if (value != null || value_forwardSelector != null)
					m_forwardSelector.m_selectedDirection = value;
			}
		}

		protected Base6Directions.Direction? m_upward
		{
			get { return value_upwardSelector == null ? null : value_upwardSelector.m_selectedDirection; }
			set
			{
				if (value != null || value_upwardSelector != null)
					m_upwardSelector.m_selectedDirection = value;
			}
		}

		public override sealed void AddControls(List<Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl> controls)
		{
			MyTerminalControlTextbox<MyShipController> blockName = new MyTerminalControlTextbox<MyShipController>("BlockName", MyStringId.GetOrCompute("Block Name"), MyStringId.GetOrCompute(AddDescription));
			blockName.Getter = block => m_searchBlockName;
			blockName.Setter = (block, value) => m_searchBlockName = value;
			controls.Add(blockName);

			controls.Add(m_forwardSelector.m_listBox);
			controls.Add(m_upwardSelector.m_listBox);
		}

		protected override AutopilotActionList.AutopilotAction Parse(VRage.Game.ModAPI.IMyCubeBlock autopilot, string command, out string message)
		{
			string blockName;
			Base6Directions.Direction? forward, upward;
			if (!SplitNameDirections(command, out blockName, out forward, out upward, out message))
				return null;

			message = null;
			m_searchBlockName = new StringBuilder(blockName);
			m_forward = forward;
			m_upward = upward;

			return ActionMethod;
		}

		protected override sealed string TermToString(out string message)
		{
			if (string.IsNullOrWhiteSpace(m_searchBlockName.ToString()))
			{
				message = "No block name";
				return null;
			}

			message = null;
			string result = Identifier + ' ' + m_searchBlockName;
			Base6Directions.Direction? forward = m_forward;
			if (forward.HasValue)
			{
				result += "," + forward.Value.ToString()[0];
				Base6Directions.Direction? upward = m_upward;
				if (upward.HasValue)
					result += "," + upward.Value.ToString()[0];
			}
			return result;
		}

		protected abstract void ActionMethod(Pathfinding.Pathfinder pathfinder);

	}
}
