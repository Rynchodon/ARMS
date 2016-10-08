using System;
using System.Collections.Generic;
using Rynchodon.Autopilot.Data;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class Proximity : ACommand
	{
		private float m_distance = 100f;

		public override ACommand Clone()
		{
			return new Proximity() { m_distance = m_distance };
		}

		public override string Identifier
		{
			get { return "p"; }
		}

		public override string AddName
		{
			get { return "Proximity"; }
		}

		public override string AddDescription
		{
			get { return "How close to get to the target"; }
		}

		public override string Description
		{
			get
			{
				return m_distance > 0f ?
					"Get within " + PrettySI.makePretty(m_distance) + "m of the target" :
					"Restore default proximity";
			}
		}

		public override void AppendCustomInfo(System.Text.StringBuilder sb)
		{
			base.AppendCustomInfo(sb);
			sb.AppendLine("A value of zero restores the default");
		}

		public override void AddControls(List<Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl> controls)
		{
			MyTerminalControlSlider<MyShipController> distance = new MyTerminalControlSlider<MyShipController>("Distance", MyStringId.GetOrCompute("Distance"), MyStringId.GetOrCompute(AddDescription));
			distance.DefaultValue = 100f;
			distance.Normalizer = Normalizer;
			distance.Denormalizer = Denormalizer;
			distance.Writer = (block, sb) => {
				sb.Append(PrettySI.makePretty(m_distance));
				sb.Append('m');
			};
			IMyTerminalValueControl<float> valueControler = distance;
			valueControler.Getter = block => m_distance;
			valueControler.Setter = (block, value) => m_distance = value;
			controls.Add(distance);
		}

		protected override AutopilotActionList.AutopilotAction Parse(VRage.Game.ModAPI.IMyCubeBlock autopilot, string command, out string message)
		{
			if (!PrettySI.TryParse(command, out m_distance))
			{
				message = "Failed to parse: " + command;
				return null;
			}

			message = null;
			return mover => mover.NavSet.Settings_Commands.DestinationRadius = m_distance > 0f ? m_distance : AllNavigationSettings.DefaultRadius;
		}

		protected override string TermToString()
		{
			return Identifier + ' ' + PrettySI.makePretty(m_distance);
		}

		private float Normalizer(IMyTerminalBlock block, float value)
		{
			return value / 1000f;
		}

		private float Denormalizer(IMyTerminalBlock block, float norm)
		{
			return norm * 1000f;
		}
	}
}
