using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Attached;
using Rynchodon.Autopilot.Pathfinding;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public abstract class TerminalProperty<T> : ACommand
	{

		protected StringBuilder m_targetBlock;
		// don't save the actual property, as string is more consistent when commands are sent over network
		protected string m_termProp;
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

			IMyTerminalControlListbox propertyList = new MyTerminalControlListbox<MyShipController>("PropertyList", MyStringId.GetOrCompute("Property"), MyStringId.NullOrEmpty);
			propertyList.ListContent = ListContent;
			propertyList.ItemSelected = ItemSelected;

			MyTerminalControlButton<MyShipController> searchButton = new MyTerminalControlButton<MyShipController>("SearchButton", MyStringId.GetOrCompute("Search"),
				MyStringId.GetOrCompute("Search for properties"), block => propertyList.UpdateVisual());

			controls.Add(searchButton);
			controls.Add(propertyList);

			AddValueControl(controls);
		}

		protected abstract void AddValueControl(List<Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl> controls);

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
			try
			{
				m_value = (T)Convert.ChangeType(split2, typeof(T));
			}
			catch (Exception ex)
			{
				Logger.DebugLog("string: " + split2 + ", exception: " + ex);
				message = ex.GetType() + ex.Message;
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
			return result + m_value;
		}

		private void ListContent(IMyTerminalBlock autopilot, List<MyTerminalControlListBoxItem> items, List<MyTerminalControlListBoxItem> selected)
		{
			string blockName = m_targetBlock.ToString().Trim();
			if (string.IsNullOrWhiteSpace(blockName))
				return;

			HashSet<ITerminalProperty<T>> termProps = new HashSet<ITerminalProperty<T>>();
			foreach (IMyCubeBlock block in AttachedGrid.AttachedCubeBlocks((IMyCubeGrid)autopilot.CubeGrid, AttachedGrid.AttachmentKind.Permanent, true))
			{
				if (!block.DisplayNameText.Contains(blockName, StringComparison.InvariantCultureIgnoreCase))
					continue;
				IMyTerminalBlock term = block as IMyTerminalBlock;
				if (term == null)
					continue;
				term.GetProperties(null, property => {
					ITerminalProperty<T> propT = property as ITerminalProperty<T>;
					if (propT == null)
						return false;
					if (termProps.Add(propT))
						items.Add(new MyTerminalControlListBoxItem(MyStringId.GetOrCompute(propT.Id), MyStringId.NullOrEmpty, propT));
					return false;
				});
			}
		}

		private void ItemSelected(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> selected)
		{
			if (selected.Count == 0)
				m_termProp = null;
			else
				m_termProp = ((ITerminalProperty<T>)selected[0].UserData).Id;
		}

		protected void SetPropertyOfBlock(Pathfinder pathfinder, string blockName, string propName, T propValue)
		{
			blockName = blockName.LowerRemoveWhitespace();
			propName = propName.Trim(); // leave spaces in propName

			foreach (IMyCubeBlock fatblock in AttachedGrid.AttachedCubeBlocks(pathfinder.Mover.Block.CubeGrid, AttachedGrid.AttachmentKind.Permanent, true))
			{
				if (!(fatblock is IMyTerminalBlock))
					continue;

				if (!pathfinder.Mover.Block.Controller.canControlBlock(fatblock))
					continue;

				if (!fatblock.DisplayNameText.LowerRemoveWhitespace().Contains(blockName))
					continue;

				IMyTerminalBlock terminalBlock = fatblock as IMyTerminalBlock;
				ITerminalProperty<T> property = terminalBlock.GetProperty(propName) as ITerminalProperty<T>;
				if (property != null)
					property.SetValue(fatblock, propValue);
			}
		}

	}
}
