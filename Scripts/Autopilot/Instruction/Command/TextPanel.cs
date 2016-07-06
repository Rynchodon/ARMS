using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using VRage.Utils;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class TextPanel : ACommand
	{

		private StringBuilder m_panelName, m_identifier;

		public override ACommand Clone()
		{
			return new TextPanel() { m_identifier = m_identifier.Clone(), m_panelName = m_panelName.Clone() };
		}

		public override string Identifier
		{
			get { return "t"; }
		}

		public override string AddName
		{
			get { return "Text Panel"; }
		}

		public override string AddDescription
		{
			get { return "Get commands from a text panel"; }
		}

		public override string Description
		{
			get { return "Get commands from " + m_panelName + (m_identifier.Length == 0 ? string.Empty : " after " + m_identifier); }
		}

		public override void AddControls(List<Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl> controls)
		{
			MyTerminalControlTextbox<MyShipController> ctrl = new MyTerminalControlTextbox<MyShipController>("PanelName", MyStringId.GetOrCompute("Panel Name"), MyStringId.GetOrCompute("Text panel to get commands from"));
			ctrl.Getter = block => m_panelName;
			ctrl.Setter = (block, value) => m_panelName = value;
			controls.Add(ctrl);

			//ctrl = new MyTerminalControlTextbox<MyShipController>("SearchString", MyStringId.GetOrCompute("Search"), MyStringId.GetOrCompute("Search String", MyStringId.GetOrCompute("String in text panel after which to 
		}

		protected override Action<Movement.Mover> Parse(VRage.Game.ModAPI.IMyCubeBlock autopilot, string command, out string message)
		{
			message = null;
			return null;
		}

		protected override string TermToString()
		{
			return Identifier + ' ' + m_panelName + (m_identifier.Length == 0 ? string.Empty : ("," + m_identifier));
		}
	}
}
