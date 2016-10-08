using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Autopilot.Navigator;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class Weld : ACommand
	{

		private StringBuilder m_target = new StringBuilder();
		private bool m_fetch;

		public override ACommand Clone()
		{
			return new Weld() { m_target = m_target, m_fetch = m_fetch };
		}

		public override string Identifier
		{
			get { return "weld"; }
		}

		public override string AddName
		{
			get { return "Weld"; }
		}

		public override string AddDescription
		{
			get { return "Weld a friendly ship"; }
		}

		public override string Description
		{
			get { return "Weld the ship: " + m_target + (m_fetch ? ", fetching components" : string.Empty); }
		}

		public override void AddControls(List<Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl> controls)
		{
			MyTerminalControlTextbox<MyShipController> gridName = new MyTerminalControlTextbox<MyShipController>("GridName", MyStringId.GetOrCompute("Grid"), MyStringId.GetOrCompute("Weld the specified grid"));
			gridName.Getter = block => m_target;
			gridName.Setter = (block, value) => m_target = value;
			controls.Add(gridName);

			IMyTerminalControlCheckbox fetch = new MyTerminalControlCheckbox<MyShipController>("FetchComponents", MyStringId.GetOrCompute("Fetch components"), MyStringId.GetOrCompute("Fetch components the next time the ship lands"));
			fetch.Getter = block => m_fetch;
			fetch.Setter = (block, value) => m_fetch = value;
			controls.Add(fetch);
		}

		protected override AutopilotActionList.AutopilotAction Parse(VRage.Game.ModAPI.IMyCubeBlock autopilot, string command, out string message)
		{
			if (string.IsNullOrWhiteSpace(command))
			{
				message = "No target";
				return null;
			}

			string[] split = command.Split(',');
			if (split.Length == 1)
				m_fetch = false;
			else if (split.Length == 2)
			{
				if (split[1].TrimStart().StartsWith("f", StringComparison.InvariantCultureIgnoreCase))
					m_fetch = true;
				else
				{
					message = "Invalid argument: " + split[1];
					return null;
				}
			}
			else
			{
				message = "Too many arguments: " + split.Length;
				return null;
			}
			m_target = new StringBuilder(split[0]);
			message = null;
			return mover => new WeldGrid(mover, split[0], m_fetch);
		}

		protected override string TermToString()
		{
			return Identifier + ' ' + m_target + (m_fetch ? ",f" : string.Empty);
		}
	}
}
