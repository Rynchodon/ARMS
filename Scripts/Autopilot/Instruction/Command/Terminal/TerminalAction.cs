using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Attached;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class TerminalAction : ACommand
	{

		private StringBuilder m_targetBlock;
		// don't save the actual action, as string is more consistent when commands are sent over network
		private string m_termAction;
	
		public override ACommand Clone()
		{
			return new TerminalAction() { m_targetBlock = m_targetBlock.Clone(), m_termAction = m_termAction };
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

			IMyTerminalControlListbox actionList = new MyTerminalControlListbox<MyShipController>("ActionList", MyStringId.GetOrCompute("Action"), MyStringId.NullOrEmpty);
			actionList.ListContent = ListContent;
			actionList.ItemSelected = ItemSelected;

			MyTerminalControlButton<MyShipController> searchButton = new MyTerminalControlButton<MyShipController>("SearchButton", MyStringId.GetOrCompute("Search"),
				MyStringId.GetOrCompute("Search for actions"), block => actionList.UpdateVisual());
			
			controls.Add(searchButton);
			controls.Add(actionList);
		}

		protected override Action<Movement.Mover> Parse(VRage.Game.ModAPI.IMyCubeBlock autopilot, string command, out string message)
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

			if (string.IsNullOrWhiteSpace(split[0]))
			{
				message = "missing block name";
				return null;
			}
			if (string.IsNullOrWhiteSpace(split[1]))
			{
				message = "missing action";
				return null;
			}

			m_targetBlock = new StringBuilder(split[0]);
			m_termAction = split[1];
			message = null;
			return mover => RunActionOnBlock(mover, split[0], split[1]);
		}

		protected override string TermToString(out string message)
		{
			if (string.IsNullOrWhiteSpace(m_targetBlock.ToString()))
			{
				message = "Need a block name";
				return null;
			}
			if (string.IsNullOrWhiteSpace(m_termAction))
			{
				message = "Need an action";
				return null;
			}

			message = null;
			return Identifier + ' ' + m_targetBlock + ", " + m_termAction;
		}

		private void ListContent(IMyTerminalBlock autopilot, List<MyTerminalControlListBoxItem> items, List<MyTerminalControlListBoxItem> selected)
		{
			string blockName = m_targetBlock.ToString().Trim();
			if (string.IsNullOrWhiteSpace(blockName))
				return;

			HashSet<ITerminalAction> termActs = new HashSet<ITerminalAction>();
			foreach (IMyCubeBlock block in AttachedGrid.AttachedCubeBlocks((IMyCubeGrid)autopilot.CubeGrid, AttachedGrid.AttachmentKind.Permanent, true))
			{
				if (!block.DisplayNameText.Contains(blockName, StringComparison.InvariantCultureIgnoreCase))
					continue;
				IMyTerminalBlock term = block as IMyTerminalBlock;
				if (term == null)
					continue;
				term.GetActions(null, action => {
					if (termActs.Add((ITerminalAction)action))
						items.Add(new MyTerminalControlListBoxItem(MyStringId.GetOrCompute(action.Id), MyStringId.NullOrEmpty, action));
					return false;
				});
			}
		}

		private void ItemSelected(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> selected)
		{
			if (selected.Count == 0)
				m_termAction = null;
			else
				m_termAction = ((ITerminalAction)selected[0].UserData).Id;
		}

		private void RunActionOnBlock(Movement.Mover mover, string blockName, string actionString)
		{
			blockName = blockName.Trim();
			actionString = actionString.Trim(); // leave spaces in actionString

			AttachedGrid.RunOnAttachedBlock(mover.Block.CubeGrid, AttachedGrid.AttachmentKind.Permanent, block => {
				IMyCubeBlock fatblock = block.FatBlock;
				if (fatblock == null || !(fatblock is IMyTerminalBlock))
					return false;

				if (!mover.Block.Controller.canControlBlock(fatblock))
					return false;

				if (!fatblock.DisplayNameText.Contains(blockName, StringComparison.InvariantCultureIgnoreCase))
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
