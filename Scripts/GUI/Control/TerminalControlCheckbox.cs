using System;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using VRage.Utils;

namespace Rynchodon.GUI.Control
{
	public class TerminalControlCheckbox<TBlock> : MyTerminalControlCheckbox<TBlock> where TBlock : MyTerminalBlock
	{

		public Func<TBlock, bool> Getter { private get; set; }
		public Action<TBlock, bool> Setter { private get; set; }

		public TerminalControlCheckbox(string id, MyStringId title, MyStringId tooltip, MyStringId? on = null, MyStringId? off = null)
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
