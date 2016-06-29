using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Attached;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class TerminalAction : ACommand
	{

		private StringBuilder m_targetBlock = new StringBuilder(), m_termAction = new StringBuilder();
	
		public override ACommand Clone()
		{
			return new TerminalAction() { m_targetBlock = new StringBuilder(m_targetBlock.ToString()), m_termAction = new StringBuilder(m_termAction.ToString()) };
		}

		public override string Identifier
		{
			get { return "a"; }
		}

		public override string AddName
		{
			get { return "Terminal Action"; }
		}

		public override string AddDescription
		{
			get { return "Run a terminal action on one or more blocks"; }
		}

		public override string Description
		{
			get { return "For all blocks with " + m_targetBlock + " in the name, run " + m_termAction; }
		}

		public override void AddControls(List<Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl> controls)
		{
			MyTerminalControlTextbox<MyShipController> textBox = new MyTerminalControlTextbox<MyShipController>("BlockName", MyStringId.GetOrCompute("Block name"), 
				MyStringId.GetOrCompute("Blocks with names containing this string will run the action."));
			textBox.Getter = block => m_targetBlock;
			textBox.Setter = (block, value) => m_targetBlock = value;
			controls.Add(textBox);

			textBox = new MyTerminalControlTextbox<MyShipController>("TermAction", MyStringId.GetOrCompute("Action"),
				MyStringId.GetOrCompute("Blocks will run the terminal action with this name."));
			textBox.Getter = block => m_termAction;
			textBox.Setter = (block, value) => m_termAction = value;
			controls.Add(textBox);
		}

		protected override Action<Movement.Mover> Parse(string command, out string message)
		{
			string[] split = command.Split(',');
			if (split.Length != 2)
			{
				if (split.Length > 2)
					message = "Too many arguments: " + split.Length;
				else
					message = "Too few arguments: " + split.Length;
				return null;
			}

			message = null;
			return mover => RunActionOnBlock(mover, split[0], split[1]);
		}

		protected override string TermToString()
		{
			return Identifier + ' ' + m_targetBlock + ", " + m_termAction;
		}

		private void RunActionOnBlock(Movement.Mover mover, string blockName, string actionString)
		{
			blockName = blockName.LowerRemoveWhitespace();
			actionString = actionString.Trim(); // leave spaces in actionString

			AttachedGrid.RunOnAttachedBlock(mover.Block.CubeGrid, AttachedGrid.AttachmentKind.Permanent, block => {
				IMyCubeBlock fatblock = block.FatBlock;
				if (fatblock == null || !(fatblock is IMyTerminalBlock))
					return false;

				if (!mover.Block.Controller.canControlBlock(fatblock))
					return false;

				if (!fatblock.DisplayNameText.LowerRemoveWhitespace().Contains(blockName))
					return false;

				IMyTerminalBlock terminalBlock = fatblock as IMyTerminalBlock;
				Sandbox.ModAPI.Interfaces.ITerminalAction actionToRun = terminalBlock.GetActionWithName(actionString); // get actionToRun on every iteration so invalid blocks can be ignored
				if (actionToRun != null)
					actionToRun.Apply(fatblock);

				return false;
			}, true);
		}

	}
}
