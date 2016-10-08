using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using VRageMath;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class TerminalPropertyColour : TerminalProperty<Color>
	{

		private const uint fullRGB = 255 + (255 << 8) + (255 << 16);

		public TerminalPropertyColour()
		{
			m_value = Color.Black; // set alpha
			m_hasValue = true;
		}

		protected override string ShortType
		{
			get { return "colour"; }
		}

		public override ACommand Clone()
		{
			return new TerminalPropertyColour() { m_targetBlock = m_targetBlock.Clone(), m_termProp = m_termProp, m_value = m_value };
		}

		protected override void AddValueControl(List<Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl> controls)
		{
			IMyTerminalControlColor colour = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlColor, Sandbox.ModAPI.Ingame.IMyShipController>("ColourValue");
			colour.Title = MyStringId.GetOrCompute("Value");
			colour.Tooltip = MyStringId.GetOrCompute("Value to set propety to");
			colour.Getter = (block) => m_value;
			colour.Setter = (block, value) => {
				m_value = value;
				m_hasValue = true;
			};
			controls.Add(colour);

			MyTerminalControlSlider<MyShipController> alpha = new MyTerminalControlSlider<MyShipController>("AlphaChannel", MyStringId.GetOrCompute("A"), MyStringId.NullOrEmpty);
			alpha.DefaultValue = 255f;
			alpha.Normalizer = Normalizer;
			alpha.Denormalizer = Denormalizer;
			alpha.Writer = (block, sb) => sb.Append(m_value.A);
			IMyTerminalValueControl<float> valueControl = alpha;
			valueControl.Getter = block => m_value.A;
			valueControl.Setter = (block, value) => {
				m_value.A = (byte)value;
				m_hasValue = true;
				colour.UpdateVisual();
			};
			controls.Add(alpha);
		}

		protected override AutopilotActionList.AutopilotAction Parse(VRage.Game.ModAPI.IMyCubeBlock autopilot, string command, out string message)
		{
			string[] split = command.Split(',');
			if (split.Length != 3)
			{
				if (split.Length > 3)
					message = "Too many arguments: " + split.Length;
				else
					message = "Too few arguments: " + split.Length;
				return null;
			}

			string split2 = split[2].Trim();
			uint packedValue;
			if (uint.TryParse(split2, out packedValue))
				m_value = new Color(packedValue);
			else
			{
				message = "Not a colour value: " + split2;
				m_hasValue = false;
				return null;
			}
			m_hasValue = true;
			message = null;
			return mover => SetPropertyOfBlock(mover, split[0], split[1], m_value);
		}

		protected override string TermToString(out string message)
		{
			string result = Identifier + ' ' + m_targetBlock + ", " + m_termProp + ", ";
			if (!m_hasValue)
			{
				message = "Property has no value";
				return null;
			}

			message = null;
			Color value = m_value;
			return result + value.PackedValue;
		}

		private float Normalizer(IMyTerminalBlock block, float value)
		{
			return value / 255f;
		}

		private float Denormalizer(IMyTerminalBlock block, float norm)
		{
			return norm * 255f;
		}

	}
}
