using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Rynchodon.Autopilot.Instruction.Command
{
	/// <summary>
	/// Creates a listbox for choosing a direction.
	/// </summary>
	public class DirectionSelection
	{

		public readonly string m_id, m_title, m_tooltip;
		private IMyTerminalControlListbox value_listBox;

		public IMyTerminalControlListbox m_listBox
		{
			get
			{
				if (value_listBox == null)
				{
					value_listBox = new MyTerminalControlListbox<MyShipController>(m_id, MyStringId.GetOrCompute(m_title), m_tooltip == null ? MyStringId.NullOrEmpty : MyStringId.GetOrCompute(m_tooltip), false, 7);
					value_listBox.ListContent = ListContent;
					value_listBox.ItemSelected = ItemSelected;
				}
				return value_listBox;
			}
		}

		public Base6Directions.Direction? m_selectedDirection;

		public DirectionSelection(string id, string title, string tooltip, Base6Directions.Direction? selected = null)
		{
			this.m_id = id;
			this.m_title = title;
			this.m_tooltip = tooltip;
			this.m_selectedDirection = selected;
		}

		private void ListContent(IMyTerminalBlock autopilot, List<MyTerminalControlListBoxItem> items, List<MyTerminalControlListBoxItem> selected)
		{
			MyTerminalControlListBoxItem defaultItem = new MyTerminalControlListBoxItem(MyStringId.GetOrCompute("Default"), MyStringId.NullOrEmpty, null);
			items.Add(defaultItem);
			if (!m_selectedDirection.HasValue)
				selected.Add(defaultItem);
			foreach (Base6Directions.Direction direction in Base6Directions.EnumDirections)
			{
				MyTerminalControlListBoxItem item = new MyTerminalControlListBoxItem(MyStringId.GetOrCompute(direction.ToString()), MyStringId.NullOrEmpty, (Base6Directions.Direction?)direction);
				items.Add(item);
				if (m_selectedDirection == direction)
					selected.Add(item);
			}
		}

		private void ItemSelected(IMyTerminalBlock autopilot, List<MyTerminalControlListBoxItem> selected)
		{
			if (selected.Count == 0)
				m_selectedDirection = null;
			else
				m_selectedDirection = (Base6Directions.Direction?)selected[0].UserData;
		}

	}
}
