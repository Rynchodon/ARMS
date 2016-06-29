using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Autopilot.Movement;
using Rynchodon.Autopilot.Navigator;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using VRageMath;

namespace Rynchodon.Autopilot.Instruction.Command
{
	/// <summary>
	/// Create a GOLIS from specified coordinates.
	/// </summary>
	public class GolisCoordinate : ACommand
	{

		protected Vector3D destination;

		public override ACommand Clone()
		{
			return new GolisCoordinate() { destination = destination };
		}

		public override string Identifier
		{
			get { return "c"; }
		}

		public override string AddName
		{
			get { return "Coordinates"; }
		}

		public override string AddDescription
		{
			get { return "Fly to manually entered coordinates."; }
		}

		public override string Description
		{
			get { return "Fly to the coordinates: " + destination.X + ',' + destination.Y + ',' + destination.Z; }
		}

		public override void AddControls(List<IMyTerminalControl> controls)
		{
			MyTerminalControlTextbox<MyShipController> control;
			control = new MyTerminalControlTextbox<MyShipController>("GolisCoordX", MyStringId.GetOrCompute("X Coordinate"), MyStringId.NullOrEmpty);
			AddGetSet(control, 0);
			controls.Add(control);

			control = new MyTerminalControlTextbox<MyShipController>("GolisCoordY", MyStringId.GetOrCompute("Y Coordinate"), MyStringId.NullOrEmpty);
			AddGetSet(control, 1);
			controls.Add(control);

			control = new MyTerminalControlTextbox<MyShipController>("GolisCoordZ", MyStringId.GetOrCompute("Z Coordinate"), MyStringId.NullOrEmpty);
			AddGetSet(control, 2);
			controls.Add(control);
		}

		private void AddGetSet(MyTerminalControlTextbox<MyShipController> control, int index)
		{
			control.Getter = block => new StringBuilder(destination.GetDim(index).ToString());
			control.Setter = (block, strBuild) => {
				double value;
				if (!PrettySI.TryParse(strBuild.ToString(), out value))
					value = double.NaN;
				destination.SetDim(index, value);
			};
		}

		protected override Action<Mover> Parse(string command, out string message)
		{
			string[] split = command.LowerRemoveWhitespace().Split(',');
			if (split.Length == 3)
			{
				double[] coords = new double[3];
				for (int i = 0; i < 3; i++)
					if (!double.TryParse(split[i], out coords[i]) || !coords[i].IsValid())
					{
						message = "Failed to parse: " + split[i];
						return null;
					}

				message = null;
				destination = new Vector3D(coords[0], coords[1], coords[2]);
				return mover => new GOLIS(mover, destination);
			}

			message = "Wrong number of coordinates: " + split.Length;
			return null;
		}

		protected override string TermToString()
		{
			return Identifier + ' ' + destination.X + ',' + destination.Y + ',' + destination.Z;
		}

	}

}
