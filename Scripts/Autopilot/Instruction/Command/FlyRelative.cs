using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Autopilot.Navigator;
using Rynchodon.Utility.Vectors;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using VRageMath;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class FlyRelative : ACommand
	{

		private Vector3 destination;

		public override ACommand Clone()
		{
			return new FlyRelative() { destination = destination };
		}

		public override string Identifier
		{
			get { return "f"; }
		}

		public override string AddName
		{
			get { return "Fly Relative"; }
		}

		public override string AddDescription
		{
			get { return "Fly a distance from current position"; }
		}

		public override string Description
		{
			get
			{
				string result = "Fly ";
				bool added = false;

				if (destination.X != 0)
				{
					added = true;
					if (destination.X > 0)
						result += PrettySI.makePretty(destination.X) + "m rightward";
					else
						result += PrettySI.makePretty(-destination.X) + "m leftward";
				}

				if (destination.Y != 0f)
				{
					if (added)
						result += ", ";
					else
						added = true;
					if (destination.Y > 0f)
						result += PrettySI.makePretty(destination.Y) + "m upward";
					else
						result += PrettySI.makePretty(-destination.Y) + "m downward";
				}

				if (destination.Z != 0f)
				{
					if (added)
						result += ", ";
					if (destination.Z > 0f)
						result += PrettySI.makePretty(destination.Z) + "m backward";
					else
						result += PrettySI.makePretty(-destination.Z) + "m forward";
				}

				return result;
			}
		}

		public override void AddControls(List<IMyTerminalControl> controls)
		{
			MyTerminalControlTextbox<MyShipController> control;
			control = new MyTerminalControlTextbox<MyShipController>("RelativeCoordX", MyStringId.GetOrCompute("Rightward"), MyStringId.GetOrCompute("Rightward from current position"));
			AddGetSet(control, 0);
			controls.Add(control);

			control = new MyTerminalControlTextbox<MyShipController>("RelativeCoordY", MyStringId.GetOrCompute("Upward"), MyStringId.GetOrCompute("Upward from current position"));
			AddGetSet(control, 1);
			controls.Add(control);

			control = new MyTerminalControlTextbox<MyShipController>("RelativeCoordZ", MyStringId.GetOrCompute("Backward"), MyStringId.GetOrCompute("Backward from current position"));
			AddGetSet(control, 2);
			controls.Add(control);
		}

		private void AddGetSet(MyTerminalControlTextbox<MyShipController> control, int index)
		{
			control.Getter = block => new StringBuilder(destination.GetDim(index).ToString());
			control.Setter = (block, strBuild) => {
				float value;
				if (!PrettySI.TryParse(strBuild.ToString(), out value))
					value = float.NaN;
				destination.SetDim(index, value);
				Logger.DebugLog("Set dim " + index + " to " + value);
			};
		}

		protected override AutopilotActionList.AutopilotAction Parse(VRage.Game.ModAPI.IMyCubeBlock autopilot, string command, out string message)
		{
			if (!GetVector(command, out destination))
			{
				message = "Failed to parse: " + command;
				return null;
			}

			message = null;
			return mover => new GOLIS(mover, ((PositionBlock)destination).ToWorld(mover.NavSet.Settings_Current.NavigationBlock.Block));
		}

		protected override string TermToString(out string message)
		{
			if (!destination.X.IsValid())
			{
				message = "Invalid right vector";
				return null;
			}
			if (!destination.Y.IsValid())
			{
				message = "Invalid up vector";
				return null;
			}
			if (!destination.Z.IsValid())
			{
				message = "Invalid back vector";
				return null;
			}

			message = null;
			return Identifier + ' ' + destination.X + ',' + destination.Y + ',' + destination.Z;
		}
	}
}
