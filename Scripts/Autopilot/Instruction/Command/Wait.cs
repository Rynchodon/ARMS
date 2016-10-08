using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Autopilot.Movement;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class Wait : ACommand
	{

		private TimeSpan duration = new TimeSpan(0, 1, 0);

		public override ACommand Clone()
		{
			return new Wait() { duration = duration };
		}

		public override string Identifier
		{
			get { return "w"; }
		}

		public override string AddName
		{
			get { return "Wait"; }
		}

		public override string AddDescription
		{
			get { return "Wait before continuing, searching for enemies will continue while waiting."; }
		}

		public override string Description
		{
			get { return "Wait for " + PrettySI.makePretty(duration) + " before continuing, searching for enemies will continue while waiting."; }
		}

		public override void AddControls(List<IMyTerminalControl> controls)
		{
			MyTerminalControlSlider<MyShipController> 
				hours = new MyTerminalControlSlider<MyShipController>("WaitForHours", MyStringId.GetOrCompute("Hours"), MyStringId.GetOrCompute("Hours to wait for")),
				minutes = new MyTerminalControlSlider<MyShipController>("WaitForMinutes", MyStringId.GetOrCompute("Minutes"), MyStringId.GetOrCompute("Minutes to wait for")),
				seconds = new MyTerminalControlSlider<MyShipController>("WaitForSeconds", MyStringId.GetOrCompute("Seconds"), MyStringId.GetOrCompute("Seconds to wait for"));

			hours.DefaultValue = 0f;
			hours.Normalizer = Normalizer;
			hours.Denormalizer = Denormalizer;
			hours.Writer = Writer;
			IMyTerminalValueControl<float> valueControl = hours;
			valueControl.Getter = block => duration.Hours;
			valueControl.Setter = (block, value) => {
				duration = TimeSpan.FromHours((int)value);
				minutes.UpdateVisual();
				seconds.UpdateVisual();
			};
			controls.Add(hours);

			minutes.DefaultValue = 0f;
			minutes.Normalizer = Normalizer;
			minutes.Denormalizer = Denormalizer;
			minutes.Writer = Writer;
			valueControl = minutes;
			valueControl.Getter = block => duration.Minutes;
			valueControl.Setter = (block, value) => {
				duration = TimeSpan.FromMinutes((int)value);
				hours.UpdateVisual();
				seconds.UpdateVisual();
			};
			controls.Add(minutes);

			seconds.DefaultValue = 0f;
			seconds.Normalizer = Normalizer;
			seconds.Denormalizer = Denormalizer;
			seconds.Writer = Writer;
			valueControl = seconds;
			valueControl.Getter = block => duration.Seconds;
			valueControl.Setter = (block, value) => {
				duration = TimeSpan.FromSeconds((int)value);
				hours.UpdateVisual();
				minutes.UpdateVisual();
			};
			controls.Add(seconds);
		}

		protected override AutopilotActionList.AutopilotAction Parse(VRage.Game.ModAPI.IMyCubeBlock autopilot, string command, out string message)
		{
			if (PrettySI.TryParse(command.RemoveWhitespace(), out duration))
			{
				message = null;
				return mover => mover.NavSet.Settings_Task_NavWay.WaitUntil = Globals.ElapsedTime.Add(duration);
			}
			else
			{
				message = "Not a time span: " + command;
				return null;
			}
		}

		protected override string TermToString()
		{
			return Identifier + ' ' + PrettySI.makePretty(duration);
		}

		private float Normalizer(IMyTerminalBlock block, float value)
		{
			return value / 300;
		}

		private float Denormalizer(IMyTerminalBlock block, float norm)
		{
			return norm * 300;
		}

		private void Writer(IMyTerminalBlock block, StringBuilder writeTo)
		{
			writeTo.Append(PrettySI.makePretty(duration));
		}

	}
}
