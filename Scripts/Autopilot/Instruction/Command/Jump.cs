using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class Jump : ACommand
	{

		private const double minJumpDistance = Sandbox.Game.GameSystems.MyGridJumpDriveSystem.MIN_JUMP_DISTANCE, underMinJump = minJumpDistance - 1d;
		private const double normExponent = 4d, sliderMax = 1e5d;
		private const double scaleFactor = sliderMax - underMinJump;

		private float m_distSetting = 0f;

		public override ACommand Clone()
		{
			return new Jump() { m_distSetting = m_distSetting };
		}

		public override string Identifier
		{
			get { return "j"; }
		}

		public override string AddName
		{
			get { return "Jump"; }
		}

		public override string AddDescription
		{
			get { return "Minimum distance to jump"; }
		}

		public override string Description
		{
			get
			{
				return m_distSetting >= minJumpDistance ?
					"Jump when destination is more than " + PrettySI.makePretty(m_distSetting) + "m" :
					"Disable jumping";
			}
		}

		public override void AppendCustomInfo(StringBuilder sb)
		{
			base.AppendCustomInfo(sb);
			sb.Append("Set to < ");
			sb.Append(minJumpDistance);
			sb.AppendLine(" to disable jumping");
		}

		public override void AddControls(List<IMyTerminalControl> controls)
		{
			MyTerminalControlSlider<MyShipController> distance = new MyTerminalControlSlider<MyShipController>("Distance", MyStringId.GetOrCompute("Distance"), MyStringId.GetOrCompute(AddDescription));
			distance.DefaultValue = 0;
			distance.Normalizer = Normalizer;
			distance.Denormalizer = Denormalizer;
			distance.Writer = (block, sb) => {
				if (m_distSetting >= minJumpDistance)
				{
					sb.Append(PrettySI.makePretty(m_distSetting));
					sb.Append("m");
				}
				else
					sb.AppendLine("Disable");
			};
			IMyTerminalValueControl<float> valueControler = distance;
			valueControler.Getter = block => m_distSetting;
			valueControler.Setter = (block, value) => m_distSetting = value;
			controls.Add(distance);
		}

		protected override AutopilotActionList.AutopilotAction Parse(IMyCubeBlock autopilot, string command, out string message)
		{
			if (!PrettySI.TryParse(command, out m_distSetting))
			{
				message = "Failed to parse: " + command;
				return null;
			}

			message = null;
			return pathfinder => pathfinder.NavSet.Settings_Commands.MinDistToJump = m_distSetting;
		}

		protected override string TermToString()
		{
			return Identifier + ' ' + (m_distSetting < minJumpDistance ? "0" : PrettySI.makePretty(m_distSetting));
		}

		private float Normalizer(IMyTerminalBlock block, float value)
		{
			const double invNormExp = 1d / normExponent, invScaleFactor = 1d / scaleFactor;
			return (float)Math.Pow((value - underMinJump) * invScaleFactor, invNormExp);
		}

		private float Denormalizer(IMyTerminalBlock block, float norm)
		{
			return (float)(Math.Pow(norm, normExponent) * scaleFactor + underMinJump);
		}

	}
}
