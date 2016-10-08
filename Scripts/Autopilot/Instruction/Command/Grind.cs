using System;
using System.Collections.Generic;
using Rynchodon.Autopilot.Navigator;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class Grind : ACommand
	{

		private float m_radius;

		public override ACommand Clone()
		{
			return new Grind() { m_radius = m_radius };
		}

		public override string Identifier
		{
			get { return "r"; }
		}

		public override string AddName
		{
			get { return "Grind"; }
		}

		public override string AddDescription
		{
			get { return "Grind enemy ships within a certain radius"; }
		}

		public override string Description
		{
			get
			{
				return m_radius > 0f ?
					"Grind enemy ships within " + PrettySI.makePretty(m_radius) + 'm' :
					"Grind any detected enemy ship";
			}
		}

		public override void AddControls(List<Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl> controls)
		{
			MyTerminalControlSlider<MyShipController> radius = new MyTerminalControlSlider<MyShipController>("Radius", MyStringId.GetOrCompute("Radius"), MyStringId.GetOrCompute(AddDescription));
			radius.DefaultValue = 100f;
			radius.Normalizer = Normalizer;
			radius.Denormalizer = Denormalizer;
			radius.Writer = (block, sb) => {
				sb.Append(PrettySI.makePretty(m_radius));
				sb.Append('m');
			};
			IMyTerminalValueControl<float> valueControler = radius;
			valueControler.Getter = block => m_radius;
			valueControler.Setter = (block, value) => m_radius = value;
			controls.Add(radius);
		}

		protected override AutopilotActionList.AutopilotAction Parse(VRage.Game.ModAPI.IMyCubeBlock autopilot, string command, out string message)
		{
			if (!PrettySI.TryParse(command, out m_radius))
			{
				message = "Failed to parse: " + command;
				return null;
			}

			message = null;
			return mover => new Grinder(mover, m_radius);
		}

		protected override string TermToString()
		{
			return Identifier + ' ' + PrettySI.makePretty(m_radius);
		}

		private float Normalizer(IMyTerminalBlock block, float value)
		{
			return value / 100000f;
		}

		private float Denormalizer(IMyTerminalBlock block, float norm)
		{
			return norm * 100000f;
		}
	}
}
