using System.Collections.Generic;
using System.Text;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using VRageMath;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class TerminalPropertyColour : TerminalProperty<uint>
	{

		private const uint fullRGB = 255 + (255 << 8) + (255 << 16);

		public TerminalPropertyColour()
		{
			m_value = Color.Black.PackedValue; // set alpha
			m_hasValue = true;
		}

		protected override string ShortType
		{
			get { return "colour"; }
		}

		public override string Description
		{
			get { return "For all blocks with " + m_targetBlock + " in the name, set " + m_termProp + " to " + new Color(m_value); }
		}

		public override ACommand Clone()
		{
			return new TerminalPropertyColour() { m_targetBlock = new StringBuilder(m_targetBlock.ToString()), m_termProp = new StringBuilder(m_termProp.ToString()), m_value = m_value };
		}

		protected override void AddValueControl(List<Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl> controls)
		{
			IMyTerminalControlColor colour = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlColor, Sandbox.ModAPI.Ingame.IMyShipController>("ColourValue");
			colour.Title = MyStringId.GetOrCompute("Value");
			colour.Tooltip = MyStringId.GetOrCompute("Value to set propety to");
			colour.Getter = (block) => new Color(m_value);
			colour.Setter = (block, value) => {
				m_value = value.PackedValue;
				m_hasValue = true;
			};
			controls.Add(colour);

			MyTerminalControlSlider<MyShipController> alpha = new MyTerminalControlSlider<MyShipController>("AlphaChannel", MyStringId.GetOrCompute("A"), MyStringId.NullOrEmpty);
			alpha.DefaultValue = 255f;
			alpha.Normalizer = Normalizer;
			alpha.Denormalizer = Denormalizer;
			alpha.Writer = (block, sb) => sb.Append(A);
			IMyTerminalValueControl<float> valueControl = alpha;
			valueControl.Getter = block => A;
			valueControl.Setter = (block, value) => {
				A = (byte)value;
				m_hasValue = true;
				colour.UpdateVisual();
			};
			controls.Add(alpha);
		}

		private float Normalizer(IMyTerminalBlock block, float value)
		{
			return value / 255f;
		}

		private float Denormalizer(IMyTerminalBlock block, float norm)
		{
			return norm * 255f;
		}

		private byte A
		{
			get { return (byte)(m_value >> 24); }
			set
			{
				Logger.DebugLog("TerminalPropertyColour", "value: " + value + ", shifted: " + (value << 24) + ", m_value: " + m_value + ", & RGB: " + (m_value & fullRGB));
				m_value = (m_value & fullRGB) + (uint)(value << 24);
			}
		}

	}
}
