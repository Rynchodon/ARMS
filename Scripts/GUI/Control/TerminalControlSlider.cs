using System;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using VRage.Utils;

namespace Rynchodon.GUI.Control
{
	public class TerminalControlSlider<TBlock> : MyTerminalControlSlider<TBlock> where TBlock : MyTerminalBlock
	{

		public Func<TBlock, float> Getter { private get; set; }
		public Action<TBlock, float> Setter { private get; set; }

		public TerminalControlSlider(string id, MyStringId title, MyStringId tooltip)
			: base(id, title, tooltip) { }

		public override float GetValue(TBlock block)
		{
			return Getter(block);
		}

		public override void SetValue(TBlock block, float value)
		{
			Setter(block, value);
		}

	}
}
