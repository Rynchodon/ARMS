using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Attached;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public abstract class TerminalProperty<T> : ACommand where T : IConvertible
	{

		protected StringBuilder m_targetBlock = new StringBuilder(), m_termProp = new StringBuilder();
		protected T m_value;
		protected bool m_hasValue;

		public override string Identifier
		{
			get { return 'p' + ShortType; }
		}

		protected abstract string ShortType { get; }

		public override string AddName
		{
			get { return "Set Property (" + ShortType + ")"; }
		}

		public override string AddDescription
		{
			get { return "Set the terminal property of one or more blocks to a value"; }
		}

		public override string Description
		{
			get { return "For all blocks with " + m_targetBlock + " in the name, set " + m_termProp + " to " + m_value; }
		}

		public override void AddControls(List<Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl> controls)
		{
			MyTerminalControlTextbox<MyShipController> textBox = new MyTerminalControlTextbox<MyShipController>("BlockName", MyStringId.GetOrCompute("Block name"),
				MyStringId.GetOrCompute("Blocks with names containing this string will have their property set."));
			textBox.Getter = block => m_targetBlock;
			textBox.Setter = (block, value) => m_targetBlock = value;
			controls.Add(textBox);

			textBox = new MyTerminalControlTextbox<MyShipController>("TermProperty", MyStringId.GetOrCompute("Property"),
				MyStringId.GetOrCompute("Blocks will have the terminal property with this name set."));
			textBox.Getter = block => m_termProp;
			textBox.Setter = (block, value) => m_termProp = value;
			controls.Add(textBox);

			AddValueControl(controls);
		}

		protected abstract void AddValueControl(List<Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl> controls);

		protected override Action<Movement.Mover> Parse(string command, out string message)
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
			try
			{
				m_value = (T)Convert.ChangeType(split2, typeof(T));
			}
			catch (Exception ex)
			{
				Logger.DebugLog("TerminalProperty", "string: " + split2 + ", exception: " + ex);
				message = ex.GetType() + ex.Message;
				m_hasValue = false;
				return null;
			}
			m_hasValue = true;
			message = null;
			return mover => SetPropertyOfBlock(mover, split[0], split[1], m_value);
		}

		protected override string TermToString()
		{
			return Identifier + ' ' + m_targetBlock + ", " + m_termProp + ", " + (m_hasValue ? m_value.ToString() : string.Empty);
		}

		private void SetPropertyOfBlock(Movement.Mover mover, string blockName, string propName, T propValue)
		{
			blockName = blockName.LowerRemoveWhitespace();
			propName = propName.Trim(); // leave spaces in propName

			AttachedGrid.RunOnAttachedBlock(mover.Block.CubeGrid, AttachedGrid.AttachmentKind.Permanent, block => {
				IMyCubeBlock fatblock = block.FatBlock;
				if (fatblock == null || !(fatblock is IMyTerminalBlock))
					return false;

				if (!mover.Block.Controller.canControlBlock(fatblock))
					return false;

				if (!fatblock.DisplayNameText.LowerRemoveWhitespace().Contains(blockName))
					return false;

				IMyTerminalBlock terminalBlock = fatblock as IMyTerminalBlock;
				ITerminalProperty<T> property = terminalBlock.GetProperty(propName) as ITerminalProperty<T>;
				if (property != null)
					property.SetValue(fatblock, propValue);
				return false;
			}, true);
		}

	}
}
