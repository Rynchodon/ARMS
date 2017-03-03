using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using VRage.Utils;
using VRageMath;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class Offset : ACommand
	{

		private Vector3 m_offsetValue = Vector3.Invalid;

		public override ACommand Clone()
		{
			return new Offset() { m_offsetValue = m_offsetValue };
		}

		public override string Identifier
		{
			get { return "o"; }
		}

		public override string AddName
		{
			get { return "Offset"; }
		}

		public override string AddDescription
		{
			get { return "Offset from the target block"; }
		}

		public override string Description
		{
			get { return "Fly to " + m_offsetValue + " from the target block"; }
		}

		public override void AddControls(List<Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl> controls)
		{
			MyTerminalControlTextbox<MyShipController> control;
			control = new MyTerminalControlTextbox<MyShipController>("GolisCoordX", MyStringId.GetOrCompute("Rightward"), MyStringId.GetOrCompute("Rightward from target block"));
			AddGetSet(control, 0);
			controls.Add(control);

			control = new MyTerminalControlTextbox<MyShipController>("GolisCoordY", MyStringId.GetOrCompute("Upward"), MyStringId.GetOrCompute("Upward from target block"));
			AddGetSet(control, 1);
			controls.Add(control);

			control = new MyTerminalControlTextbox<MyShipController>("GolisCoordZ", MyStringId.GetOrCompute("Backward"), MyStringId.GetOrCompute("Backward from target block"));
			AddGetSet(control, 2);
			controls.Add(control);
		}

		private void AddGetSet(MyTerminalControlTextbox<MyShipController> control, int index)
		{
			control.Getter = block => new StringBuilder(m_offsetValue.GetDim(index).ToString());
			control.Setter = (block, strBuild) => {
				float value;
				if (!PrettySI.TryParse(strBuild.ToString(), out value))
					value = float.NaN;
				m_offsetValue.SetDim(index, value);
			};
		}

		protected override AutopilotActionList.AutopilotAction Parse(VRage.Game.ModAPI.IMyCubeBlock autopilot, string command, out string message)
		{
			if (!GetVector(command, out m_offsetValue))
			{
				message = "Failed to parse: " + command;
				return null;
			}

			message = null;
			return mover => mover.NavSet.Settings_Task_NavMove.DestinationOffset = m_offsetValue;
		}

		protected override string TermToString(out string message)
		{
			if (!m_offsetValue.X.IsValid())
			{
				message = "Invalid X offset";
				return null;
			}
			if (!m_offsetValue.Y.IsValid())
			{
				message = "Invalid Y offset";
				return null;
			}
			if (!m_offsetValue.Z.IsValid())
			{
				message = "Invalid Z offset";
				return null;
			}

			message = null;
			return Identifier + ' ' + m_offsetValue.X + ',' + m_offsetValue.Y + ',' + m_offsetValue.Z;
		}

	}
}
