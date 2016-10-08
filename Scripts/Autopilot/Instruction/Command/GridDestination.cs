using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Autopilot.Navigator;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using VRage.Utils;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class GridDestination : ACommand
	{

		private StringBuilder m_gridName;

		public override ACommand Clone()
		{
			return new GridDestination() { m_gridName = m_gridName.Clone() };
		}

		public override string Identifier
		{
			get { return "g"; }
		}

		public override string AddName
		{
			get { return "Grid"; }
		}

		public override string AddDescription
		{
			get { return "Seach for a friendly grid to fly towards, land on, etc."; }
		}

		public override string Description
		{
			get { return "Search for " + m_gridName + " to fly towards, land on, etc."; }
		}

		public override void AddControls(List<Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl> controls)
		{
			MyTerminalControlTextbox<MyShipController> gridName = new MyTerminalControlTextbox<MyShipController>("GridName", MyStringId.GetOrCompute("Grid Name"), MyStringId.NullOrEmpty);
			gridName.Getter = block => m_gridName;
			gridName.Setter = (block, value) => m_gridName = value;
			controls.Add(gridName);
		}

		protected override AutopilotActionList.AutopilotAction Parse(VRage.Game.ModAPI.IMyCubeBlock autopilot, string command, out string message)
		{
			if (string.IsNullOrWhiteSpace(command))
			{
				message = "No grid name";
				return null;
			}

			m_gridName = new StringBuilder(command);
			message = null;
			return mover => new FlyToGrid(mover, command);
		}

		protected override string TermToString()
		{
			return Identifier + ' ' + m_gridName;
		}
	}
}
