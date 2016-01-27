
using System;
using Sandbox.Game.Gui;
using VRage.Utils;

namespace Rynchodon.GUI.Control
{
	public class TerminalControlOnOffSwitch<TBlock> : MyTerminalControlOnOffSwitch<TBlock> where TBlock : Sandbox.Game.Entities.Cube.MyTerminalBlock
	{

		public Func<TBlock, bool> Getter { private get; set; }
		public Action<TBlock, bool> Setter { private get; set; }

		public TerminalControlOnOffSwitch(string id, MyStringId title, MyStringId tooltip = default(MyStringId), MyStringId? on = null, MyStringId? off = null)
			: base(id, title, tooltip, on, off) { }

		public override bool GetValue(TBlock block)
		{
			return Getter(block);
		}

		public override void SetValue(TBlock block, bool value)
		{
			Setter(block, value);
		}

	}
}
