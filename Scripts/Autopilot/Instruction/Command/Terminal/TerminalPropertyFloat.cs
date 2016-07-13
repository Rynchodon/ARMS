using System.Collections.Generic;
using System.Text;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using VRage.Utils;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class TerminalPropertyFloat : TerminalProperty<float>
	{

		private StringBuilder m_textBox;

		protected override string ShortType
		{
			get { return "float"; }
		}

		public override ACommand Clone()
		{
			return new TerminalPropertyFloat() { m_targetBlock = m_targetBlock.Clone(), m_termProp = m_termProp, m_value = m_value };
		}

		protected override void AddValueControl(List<Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl> controls)
		{
			MyTerminalControlTextbox<MyShipController> valueBox = new MyTerminalControlTextbox<MyShipController>("ValueBox", MyStringId.GetOrCompute("Value"), MyStringId.GetOrCompute("Value to set propety to"));
			valueBox.Getter = block => m_textBox;
			valueBox.Setter = (block, value) => {
				m_textBox = value;
				m_hasValue = float.TryParse(value.ToString(), out m_value);
			};
			controls.Add(valueBox);
		}
	}
}
