
using Sandbox.Game.Gui;
using VRage.Utils;

namespace Rynchodon.Utility.GUI
{
	public class TerminalControlOnOffSwitch<TBlock> : MyTerminalControlOnOffSwitch<TBlock> where TBlock : Sandbox.Game.Entities.Cube.MyTerminalBlock
	{

		public delegate bool GetterDelegate(TBlock block);
		public delegate void SetterDelegate(TBlock block, bool value);

		public GetterDelegate Getter { private get; set; }
		public SetterDelegate Setter { private get; set; }

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

		public override bool GetDefaultValue(TBlock block)
		{
			return false;
		}

		public override bool GetMininum(TBlock block)
		{
			return false;
		}

		public override bool GetMaximum(TBlock block)
		{
			return true;
		}

	}
}
