using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rynchodon.Settings;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.ModAPI;
using VRage.Utils;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class Enemy : ACommand
	{

		private MyTerminalControlListBoxItem[] value_allResponses;

		private List<EnemyFinder.Response> m_activeResponses = new List<EnemyFinder.Response>();
		private StringBuilder m_enemyId = new StringBuilder();
		private float m_range;

		private IMyTerminalControlListbox m_responseListbox;
		private EnemyFinder.Response m_selected;
		private bool m_addingResponse;

		private MyTerminalControlListBoxItem[] m_allResponses
		{
			get
			{
				if (value_allResponses == null)
				{
					value_allResponses = new MyTerminalControlListBoxItem[] {
						new MyTerminalControlListBoxItem(MyStringId.GetOrCompute("Flee"), MyStringId.GetOrCompute("Flee from enemy, requires working thrusters."), EnemyFinder.Response.Flee),
						new MyTerminalControlListBoxItem(MyStringId.GetOrCompute("Land"), MyStringId.GetOrCompute("Land on enemy, requires working thrusters and landing gear."), EnemyFinder.Response.Land),
						new MyTerminalControlListBoxItem(MyStringId.GetOrCompute("Fight"), MyStringId.GetOrCompute("Attack enemy with weapons, requires working weapons."), EnemyFinder.Response.Fight),
						new MyTerminalControlListBoxItem(MyStringId.GetOrCompute("Ram"), MyStringId.GetOrCompute("Ram enemy, requires working thrusters."), EnemyFinder.Response.Ram),
						new MyTerminalControlListBoxItem(MyStringId.GetOrCompute("Self-Destruct"), MyStringId.GetOrCompute("Starts the countdown on every warhead on the ship, then go to next response."), EnemyFinder.Response.Self_Destruct)};
				}
				return value_allResponses;
			}
		}

		public override ACommand Clone()
		{
			return new Enemy() { m_activeResponses = new List<EnemyFinder.Response>(m_activeResponses), m_enemyId = new StringBuilder(m_enemyId.ToString()), m_range = m_range };
		}

		public override string Identifier
		{
			get { return "e"; }
		}

		public override string AddName
		{
			get { return "Enemy"; }
		}

		public override string AddDescription
		{
			get { return "React when an enemy is detected"; }
		}

		public override string Description
		{
			get
			{
				string descr;
				if (m_range == 0)
					descr = "When any enemy is detected ";
				else
					descr = "When an enemy is within " + PrettySI.makePretty(m_range) + "m of this ship ";
				descr += string.Join(",", m_activeResponses);
				return AddDescription;
			}
		}

		//public override void AppendCustomInfo(StringBuilder sb)
		//{
		//	sb.Append("When autopilot reaches this command it starts searching for enemies. It will continue searching ");
		//}

		public override void AddControls(List<Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl> controls)
		{
			if (!m_addingResponse)
			{
				MyTerminalControlSlider<MyShipController> range = new MyTerminalControlSlider<MyShipController>("RangeSlider", MyStringId.GetOrCompute("Range"), MyStringId.GetOrCompute("How close enemy needs to be for autopilot to respond to it."));
				range.Normalizer = Normalizer;
				range.Denormalizer = Denormalizer;
				range.Writer = (block, sb) => {
					sb.Append(PrettySI.makePretty(m_range));
					sb.Append('m');
				};
				IMyTerminalValueControl<float> valueControler = range;
				valueControler.Getter = block => m_range;
				valueControler.Setter = (block, value) => m_range = value;
				controls.Add(range);
			}

			if (m_responseListbox == null)
			{
				m_responseListbox = new MyTerminalControlListbox<MyShipController>("Responses", MyStringId.GetOrCompute("Responses"), MyStringId.NullOrEmpty);
				m_responseListbox.ListContent = ListContent;
				m_responseListbox.ItemSelected = ItemSelected;
			}
			controls.Add(m_responseListbox);

			if (!m_addingResponse)
			{
				controls.Add(new MyTerminalControlButton<MyShipController>("AddResponse", MyStringId.GetOrCompute("Add Response"), MyStringId.NullOrEmpty, AddResponse));
				controls.Add(new MyTerminalControlButton<MyShipController>("RemoveResponse", MyStringId.GetOrCompute("Remove Response"), MyStringId.NullOrEmpty, RemoveResponse));
				controls.Add(new MyTerminalControlButton<MyShipController>("MoveResponseUp", MyStringId.GetOrCompute("Move Response Up"), MyStringId.NullOrEmpty, MoveResponseUp));
				controls.Add(new MyTerminalControlButton<MyShipController>("MoveResponseDown", MyStringId.GetOrCompute("Move Response Down"), MyStringId.NullOrEmpty, MoveResponseDown));
			}
		}

		protected override Action<Movement.Mover> Parse(string command, out string message)
		{
			if (!ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bAllowWeaponControl))
			{
				message = "Weapon control is disabled in settings";
				return null;
			}

			string[] split = command.RemoveWhitespace().Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			m_range = 0f;
			long entityId = 0L;
			m_activeResponses.Clear();
			foreach (string s in split)
			{
				if (s.Equals("off", StringComparison.InvariantCultureIgnoreCase))
				{
					message = null;
					return mover => mover.m_navSet.Settings_Commands.EnemyFinder = null;
				}
				float range;
				if (PrettySI.TryParse(s, out range))
				{
					m_range = range;
					continue;
				}

				if (s.StartsWith("id"))
				{
					if (s.Length < 3)
					{
						message = "Could not get id from " + s;
						return null;
					}

					string idStr = s.Substring(2, s.Length - 2);
					if (!long.TryParse(idStr, out entityId))
					{
						message = "Not an id: " + idStr;
						return null;
					}
					else
						m_enemyId = new StringBuilder(entityId.ToString());

					continue;
				}

				string resStr = s.Replace('-', '_');

				EnemyFinder.Response r;
				if (!Enum.TryParse<EnemyFinder.Response>(resStr, true, out r))
				{
					message = "Not a response: " + resStr;
					return null;
				}
				else
					m_activeResponses.Add(r);
			}

			if (m_activeResponses.Count == 0)
			{
				message = "No responses";
				return null;
			}

			message = null;
			return mover => {
				if (mover.m_navSet.Settings_Commands.EnemyFinder == null)
					mover.m_navSet.Settings_Commands.EnemyFinder = new EnemyFinder(mover, mover.m_navSet, entityId);
				mover.m_navSet.Settings_Commands.EnemyFinder.AddResponses(m_range, m_activeResponses);
			};
		}

		protected override string TermToString()
		{
			string result = Identifier + ' ';
			if (m_range > 0f)
				result += PrettySI.makePretty(m_range) + "m,";
			result += string.Join(",", m_activeResponses);
			return result;
		}

		private void ListContent(IMyTerminalBlock autopilot, List<MyTerminalControlListBoxItem> items, List<MyTerminalControlListBoxItem> selected)
		{
			if (m_addingResponse)
			{
				foreach (EnemyFinder.Response response in  m_allResponses.Select(item => (EnemyFinder.Response)item.UserData).Except(m_activeResponses))
					items.Add(GetItem(response));
				return;
			}

			foreach (EnemyFinder.Response response in m_activeResponses)
			{
				MyTerminalControlListBoxItem item = GetItem(response);
				items.Add(item);
				if (m_selected == response)
					selected.Add(item);
			}
		}

		private void ItemSelected(IMyTerminalBlock autopilot, List<MyTerminalControlListBoxItem> selected)
		{
			if (m_addingResponse)
			{
				if (selected.Count == 0)
					return;
				m_activeResponses.Add((EnemyFinder.Response)selected[0].UserData);
				m_addingResponse = false;
				autopilot.SwitchTerminalTo();
				return;
			}

			if (selected.Count == 0)
				m_selected = EnemyFinder.Response.None;
			else
				m_selected = (EnemyFinder.Response)selected[0].UserData;
		}

		#region Button Actions

		private void AddResponse(IMyTerminalBlock block)
		{
			m_addingResponse = true;
			block.SwitchTerminalTo();
		}

		private void RemoveResponse(IMyTerminalBlock block)
		{
			if (m_selected == EnemyFinder.Response.None)
			{
				Logger.DebugLog("nothing selected");
				return;
			}

			m_activeResponses.Remove(m_selected);
			m_responseListbox.UpdateVisual();
		}

		private void MoveResponseUp(IMyTerminalBlock block)
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

			Logger.DebugLog("move up: " + m_selected + ", index: " + index + ", count: " + m_activeResponses.Count);
			m_activeResponses.Swap(index - 1, index);
			m_responseListbox.UpdateVisual();
		}

		private void MoveResponseDown(IMyTerminalBlock block)
		{
			if (m_selected == null)
			{
				Logger.DebugLog("nothing selected");
				return;
			}

			int index = GetSelectedIndex();
			if (index == m_activeResponses.Count - 1)
			{
				Logger.DebugLog("already last element: " + m_selected);
				return;
			}

			Logger.DebugLog("move down: " + m_selected + ", index: " + index + ", count: " + m_activeResponses.Count);
			m_activeResponses.Swap(index, index + 1);
			m_responseListbox.UpdateVisual();
		}

		#endregion Button Actions

		private int GetSelectedIndex()
		{
			for (int i = 0; i < m_activeResponses.Count; i++)
				if (m_activeResponses[i] == m_selected)
					return i;
			return -1;
		}

		private MyTerminalControlListBoxItem GetItem(EnemyFinder.Response response)
		{
			foreach (MyTerminalControlListBoxItem item in m_allResponses)
				if ((EnemyFinder.Response)item.UserData == response)
					return item;
			return null;
		}

		private float Normalizer(IMyTerminalBlock block, float value)
		{
			return value / 100000f;
		}

		private float Denormalizer(IMyTerminalBlock block, float norm)
		{
			return norm * 100000f;
		}

	}
}
