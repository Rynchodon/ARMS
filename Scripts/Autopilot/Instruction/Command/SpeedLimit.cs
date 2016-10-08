using System;
using System.Collections.Generic;
using Rynchodon.Autopilot.Data;
using Rynchodon.Settings;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class SpeedLimit : ACommand
	{
		private float m_speed = ServerSettings.GetSetting<float>(ServerSettings.SettingName.fDefaultSpeed);

		public override ACommand Clone()
		{
			return new SpeedLimit() { m_speed = m_speed };
		}

		public override string Identifier
		{
			get { return "v"; }
		}

		public override string AddName
		{
			get { return "Speed"; }
		}

		public override string AddDescription
		{
			get { return "Maximum speed to travel at"; }
		}

		public override string Description
		{
			get
			{
				return m_speed > 0f ?
					"Travel at up to " + PrettySI.makePretty(m_speed) + "m/s" :
					"Restore default speed limit";
			}
		}

		public override void AppendCustomInfo(System.Text.StringBuilder sb)
		{
			base.AppendCustomInfo(sb);
			sb.AppendLine("A value of zero restores the default.");
		} 

		public override void AddControls(List<Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl> controls)
		{
			MyTerminalControlSlider<MyShipController> speed = new MyTerminalControlSlider<MyShipController>("Speed", MyStringId.GetOrCompute("Speed"), MyStringId.GetOrCompute(AddDescription));
			speed.DefaultValue = ServerSettings.GetSetting<float>(ServerSettings.SettingName.fDefaultSpeed);
			speed.Normalizer = Normalizer;
			speed.Denormalizer = Denormalizer;
			speed.Writer = (block, sb) => {
				sb.Append(PrettySI.makePretty(m_speed));
				sb.Append("m/s");
			};
			IMyTerminalValueControl<float> valueControler = speed;
			valueControler.Getter = block => m_speed;
			valueControler.Setter = (block, value) => m_speed = value;
			controls.Add(speed);
		}

		protected override AutopilotActionList.AutopilotAction Parse(VRage.Game.ModAPI.IMyCubeBlock autopilot, string command, out string message)
		{
			if (!PrettySI.TryParse(command, out m_speed))
			{
				message = "Failed to parse: " + command;
				return null;
			}

			message = null;
			return mover => mover.NavSet.Settings_Commands.SpeedTarget = m_speed > 0f ? m_speed : AllNavigationSettings.DefaultSpeed;
		}

		protected override string TermToString()
		{
			return Identifier + ' ' + PrettySI.makePretty(m_speed);
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
