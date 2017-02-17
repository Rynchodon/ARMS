using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rynchodon.Autopilot.Harvest;
using Rynchodon.Autopilot.Navigator.Mining;
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

		private class Ore
		{
			public readonly string SubtypeName, MinedOre, Symbol;

			public Ore(string subtypeName, MyVoxelMaterialDefinition def)
			{
				SubtypeName = def.Id.SubtypeName;
				int index = SubtypeName.IndexOf('_');
				if (index >= 0)
					SubtypeName = SubtypeName.Substring(0, index);
				SubtypeName = SubtypeName.Trim();

				MinedOre = def.MinedOre.Trim();

				if (SubtypeName == MinedOre)
					MinedOre = null;

				if (OreDetector.GetChemicalSymbol(SubtypeName.ToLower(), out Symbol))
					Symbol = char.ToUpper(Symbol[0]) + Symbol.Substring(1);
			}

			public string LongName { get { return MinedOre != null ? SubtypeName + " (" + MinedOre + ')' : SubtypeName; } }
			public string ShortName { get { return Symbol ?? SubtypeName; } }
		}

		private Ore[] value_allOres;

		private List<Ore> m_activeOres = new List<Ore>();

		private IMyTerminalControlListbox m_oreListbox;
		private Ore m_selected;
		private bool m_addingOres;

		private Ore[] m_allOres
		{
			get
			{
				if (value_allOres == null)
				{
					Dictionary<string, Ore> materials = new Dictionary<string, Ore>();
					foreach (MyVoxelMaterialDefinition def in  MyDefinitionManager.Static.GetVoxelMaterialDefinitions())
					{
						if (!def.IsRare)
							continue;

						string subtypeName = def.Id.SubtypeName;
						int index = subtypeName.IndexOf('_');
						if (index >= 0)
							subtypeName = subtypeName.Substring(0, index);
						subtypeName = subtypeName.Trim();

						if (materials.ContainsKey(subtypeName))
							continue;

						Ore o = new Ore(subtypeName, def);
						materials.Add(subtypeName, o);
					}

					value_allOres = materials.Values.ToArray();
				}
				return value_allOres;
			}
		}

		public override ACommand Clone()
		{
			return new HarvestVoxel() { m_activeOres = new List<Ore>(m_activeOres), value_allOres = value_allOres };
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
				return "Harvest " + string.Join(", ", m_activeOres.Select(o => o.LongName));
			}
		}

		public override void AppendCustomInfo(StringBuilder sb)
		{
			base.AppendCustomInfo(sb);
			sb.AppendLine("All ores are treated the same; if you wish to prioritize, use multiple commands.");
			sb.AppendLine("Ore detectors must have a two-way connection to the Autopilot block.");
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

		protected override AutopilotActionList.AutopilotAction Parse(VRage.Game.ModAPI.IMyCubeBlock autopilot, string command, out string message)
		{
			byte[] oreType;

			if (command.Equals("arvest", StringComparison.InvariantCultureIgnoreCase))
				oreType = null;
			else
			{
				if (string.IsNullOrWhiteSpace(command))
				{
					message = "no ores specified";
					return null;
				}

				string[] splitComma = command.Split(',');
				List<byte> oreTypeList = new List<byte>();
				m_activeOres.Clear();

				foreach (string name in splitComma)
				{
					string trimmed = name.Trim();
					Ore ore;
					if (!TryGetOre(trimmed, out ore))
					{
						message = "Not ore: " + name;
						return null;
					}
					byte[] oreIds;
					if (!OreDetector.TryGetMaterial(trimmed, out oreIds))
					{
						message = "Failed to get material index: " + name;
						return null;
					}

					m_activeOres.Add(ore);
					oreTypeList.AddArray(oreIds);
				}
				oreType = oreTypeList.ToArray();
			}

			message = null;
			return mover => new Miner(mover, oreType);
		}

		protected override string TermToString()
		{
			if (m_activeOres.Count == 0)
				return "harvest";
			return Identifier + ' ' + string.Join(",", m_activeOres.Select(o => o.ShortName));
		}

		private void ListContent(IMyTerminalBlock autopilot, List<MyTerminalControlListBoxItem> items, List<MyTerminalControlListBoxItem> selected)
		{
			if (m_addingOres)
			{
				foreach (Ore ore in m_allOres.Except(m_activeOres))
					items.Add(GetItem(ore));
				return;
			}

			foreach (Ore ore in m_activeOres)
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
				Ore ore;
				if (!TryGetOre(selected[0], out ore))
					throw new Exception("Selected item not found in all ores. Selected item: " + selected[0].Text.ToString());
				m_activeOres.Add(ore);
				m_addingOres = false;
				autopilot.RebuildControls();
				return;
			}

			if (selected.Count == 0)
				m_selected = null;
			else
				TryGetOre(selected[0], out m_selected);
		}

		#region Button Actions

		private void AddOre(IMyTerminalBlock block)
		{
			m_addingOres = true;
			block.RebuildControls();
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

		private MyTerminalControlListBoxItem GetItem(Ore ore)
		{
			return new MyTerminalControlListBoxItem(MyStringId.GetOrCompute(ore.LongName), MyStringId.NullOrEmpty, null);
		}

		private bool TryGetOre(MyTerminalControlListBoxItem item, out Ore ore)
		{
			string longName = item.Text.ToString();
			foreach (Ore o in m_allOres)
				if (o.LongName == longName)
				{
					ore = o;
					return true;
				}
			ore = null;
			return false;
		}

		private bool TryGetOre(string name, out Ore ore)
		{
			foreach (Ore o in m_allOres)
				if (o.SubtypeName.Equals(name, StringComparison.InvariantCultureIgnoreCase) || o.MinedOre != null && o.MinedOre.Equals(name, StringComparison.InvariantCultureIgnoreCase) || o.Symbol.Equals(name, StringComparison.InvariantCultureIgnoreCase))
				{
					ore = o;
					return true;
				}
			ore = null;
			return false;
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
