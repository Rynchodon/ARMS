using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rynchodon.Autopilot.Harvest;
using Rynchodon.Autopilot.Navigator;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.ModAPI;
using VRage.Utils;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class HarvestVoxel : ACommand
	{

		private string[] value_allOres;

		private List<string> m_activeOres = new List<string>();

		private IMyTerminalControlListbox m_oreListbox;
		private string m_selected;
		private bool m_addingOres;

		private string[] m_allOres
		{
			get
			{
				if (value_allOres == null)
				{
					Dictionary<string, string> materials = new Dictionary<string, string>();
					foreach (MyVoxelMaterialDefinition def in  MyDefinitionManager.Static.GetVoxelMaterialDefinitions())
					{
						if (!def.IsRare)
							continue;

						string subtype = def.Id.SubtypeName.Split('_')[0].Trim();

						if (materials.ContainsKey(subtype))
							continue;

						string minedOre = def.MinedOre.Trim();

						if (subtype.Equals(minedOre))
							materials.Add(subtype, subtype);
						else
							materials.Add(subtype, subtype + " (" + minedOre + ')');
					}

					value_allOres = materials.Values.ToArray();
				}
				return value_allOres;
			}
		}

		public override ACommand Clone()
		{
			return new HarvestVoxel() { m_activeOres = new List<string>(m_activeOres) };
		}

		public override string Identifier
		{
			get { return "h"; }
		}

		public override string AddName
		{
			get { return "Harvest Ore"; }
		}

		public override string AddDescription
		{
			get { return "Harvest ore from an asteroid or planet"; }
		}

		public override string Description
		{
			get
			{
				if (m_activeOres.Count == 0)
					return "Harvest any detected ore";
				return "Harvest " + string.Join(",", m_activeOres);
			}
		}

		public override void AddControls(List<Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl> controls)
		{
			if (m_oreListbox == null)
			{
				m_oreListbox = new MyTerminalControlListbox<MyShipController>("Ores", MyStringId.GetOrCompute("Ores"), MyStringId.NullOrEmpty);
				m_oreListbox.ListContent = ListContent;
				m_oreListbox.ItemSelected = ItemSelected;
			}
			controls.Add(m_oreListbox);

			if (!m_addingOres)
			{
				controls.Add(new MyTerminalControlButton<MyShipController>("AddOre", MyStringId.GetOrCompute("Add Ore"), MyStringId.NullOrEmpty, AddOre));
				controls.Add(new MyTerminalControlButton<MyShipController>("RemoveOre", MyStringId.GetOrCompute("Remove Ore"), MyStringId.NullOrEmpty, RemoveOre));
				controls.Add(new MyTerminalControlButton<MyShipController>("MoveOreUp", MyStringId.GetOrCompute("Move Ore Up"), MyStringId.NullOrEmpty, MoveOreUp));
				controls.Add(new MyTerminalControlButton<MyShipController>("MoveOreDown", MyStringId.GetOrCompute("Move Ore Down"), MyStringId.NullOrEmpty, MoveOreDown));
			}
		}

		protected override Action<Movement.Mover> Parse(VRage.Game.ModAPI.IMyCubeBlock autopilot, string command, out string message)
		{
			byte[] oreType;

			if (command.Equals("arvest", StringComparison.InvariantCultureIgnoreCase))
				oreType = null;
			else
			{
				string[] splitComma = command.Split(',');
				List<byte> oreTypeList = new List<byte>();

				for (int i = 0; i < splitComma.Length; i++)
				{
					string oreName = splitComma[i];

					byte[] oreIds;
					if (!OreDetector.TryGetMaterial(splitComma[i], out oreIds))
					{
						message = "Not ore: " + oreName;
						return null;
					}

					oreTypeList.AddArray(oreIds);
				}

				oreType = oreTypeList.ToArray();
			}

			message = null;
			return mover => new MinerVoxel(mover, oreType);
		}

		protected override string TermToString()
		{
			if (m_activeOres.Count == 0)
				return "harvest";
			return Identifier + ' ' + string.Join(",", m_activeOres);
		}

		private void ListContent(IMyTerminalBlock autopilot, List<MyTerminalControlListBoxItem> items, List<MyTerminalControlListBoxItem> selected)
		{
			if (m_addingOres)
			{
				foreach (string ore in m_allOres.Except(m_activeOres))
					items.Add(GetItem(ore));
				return;
			}

			foreach (string ore in m_activeOres)
			{
				MyTerminalControlListBoxItem item = GetItem(ore);
				if (m_selected == ore)
					selected.Add(item);
				items.Add(item);
			}
		}

		private void ItemSelected(IMyTerminalBlock autopilot, List<MyTerminalControlListBoxItem> selected)
		{
			if (m_addingOres)
			{
				if (selected.Count == 0)
					return;
				m_activeOres.Add(selected[0].Text.ToString());
				m_addingOres = false;
				autopilot.SwitchTerminalTo();
				return;
			}

			if (selected.Count == 0)
				m_selected = null;
			else
				m_selected = selected[0].Text.ToString();
		}

		#region Button Actions

		private void AddOre(IMyTerminalBlock block)
		{
			m_addingOres = true;
			block.SwitchTerminalTo();
		}

		private void RemoveOre(IMyTerminalBlock block)
		{
			if (m_selected == null)
			{
				Logger.DebugLog("nothing selected");
				return;
			}

			m_activeOres.Remove(m_selected);
			m_oreListbox.UpdateVisual();
		}

		private void MoveOreUp(IMyTerminalBlock block)
		{
			if (m_selected == null)
			{
				Logger.DebugLog("nothing selected");
				return;
			}

			int index = GetSelectedIndex();
			if (index == 0)
			{
				Logger.DebugLog("already first element: " + m_selected);
				return;
			}

			Logger.DebugLog("move up: " + m_selected + ", index: " + index + ", count: " + m_activeOres.Count);
			m_activeOres.Swap(index - 1, index);
			m_oreListbox.UpdateVisual();
		}

		private void MoveOreDown(IMyTerminalBlock block)
		{
			if (m_selected == null)
			{
				Logger.DebugLog("nothing selected");
				return;
			}

			int index = GetSelectedIndex();
			if (index == m_activeOres.Count - 1)
			{
				Logger.DebugLog("already last element: " + m_selected);
				return;
			}

			Logger.DebugLog("move down: " + m_selected + ", index: " + index + ", count: " + m_activeOres.Count);
			m_activeOres.Swap(index, index + 1);
			m_oreListbox.UpdateVisual();
		}

		#endregion Button Actions

		private MyTerminalControlListBoxItem GetItem(string ore)
		{
			return new MyTerminalControlListBoxItem(MyStringId.GetOrCompute(ore), MyStringId.NullOrEmpty, null);
		}

		private int GetSelectedIndex()
		{
			for (int i = 0; i < m_activeOres.Count; i++)
				if (m_activeOres[i] == m_selected)
					return i;
			return -1;
		}

	}
}
