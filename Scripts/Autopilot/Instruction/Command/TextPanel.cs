using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
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

		public string SearchPanelName
		{
			get { return m_panelName.ToString(); }
		}

		public override void AddControls(List<Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl> controls)
		{
			MyTerminalControlTextbox<MyShipController> ctrl = new MyTerminalControlTextbox<MyShipController>("PanelName", MyStringId.GetOrCompute("Panel Name"), MyStringId.GetOrCompute("Text panel to get commands from"));
			ctrl.Getter = block => m_panelName;
			ctrl.Setter = (block, value) => m_panelName = value;
			controls.Add(ctrl);

			ctrl = new MyTerminalControlTextbox<MyShipController>("SearchString", MyStringId.GetOrCompute("Search String"), MyStringId.GetOrCompute("String that occurs before commands"));
			ctrl.Getter = block => m_identifier;
			ctrl.Setter = (block, value) => m_identifier = value;
		}

		protected override AutopilotActionList.AutopilotAction Parse(VRage.Game.ModAPI.IMyCubeBlock autopilot, string command, out string message)
		{
			string[] split = command.Split(',');

			switch (split.Length)
			{
				case 1:
					m_panelName = new StringBuilder(split[0]);
					m_identifier = new StringBuilder();
					break;
				case 2:
					m_panelName = new StringBuilder(split[0]);
					m_identifier = new StringBuilder(split[1]);
					break;
				default:
					message = "Too many arguments: " + split.Length;
					return null;
			}

			message = null;
			return mover => VRage.Exceptions.ThrowIf<NotImplementedException>(true);
		}

		protected override string TermToString()
		{
			return Identifier + ' ' + m_panelName + (m_identifier == null || m_identifier.Length == 0 ? string.Empty : "," + m_identifier);
		}

		public TextPanelMonitor GetTextPanelMonitor(IMyTerminalBlock autopilot, AutopilotCommands autoCmds)
		{
			string panelName = m_panelName.ToString();

			IMyTextPanel textPanel = null;
			int bestMatchLength = int.MaxValue;
			foreach (IMyCubeGrid grid in Attached.AttachedGrid.AttachedGrids((IMyCubeGrid)autopilot.CubeGrid, Attached.AttachedGrid.AttachmentKind.Permanent, true))
			{
				CubeGridCache cache = CubeGridCache.GetFor(grid);
				if (cache == null)
					continue;
				foreach (IMyTextPanel panel in cache.BlocksOfType(typeof(MyObjectBuilder_TextPanel)))
				{
					if (!((IMyCubeBlock)autopilot).canControlBlock((IMyCubeBlock)panel))
						continue;

					string name = panel.DisplayNameText;
					if (name.Length < bestMatchLength && name.Contains(panelName))
					{
						textPanel = panel;
						bestMatchLength = name.Length;
						if (name.Length == panelName.Length)
							return new TextPanelMonitor(textPanel, autoCmds, m_identifier.ToString());
					}
				}
			}

			if (textPanel == null)
				return null;

			return new TextPanelMonitor(textPanel, autoCmds, m_identifier.ToString());
		}

	}
}
