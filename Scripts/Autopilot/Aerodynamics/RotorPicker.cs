using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Rynchodon.Autopilot.Aerodynamics
{
	class RotorPicker
	{
		public delegate void ControlRotorParams(out IEnumerable<IMyMotorStator> rotors, out float sensitivity, out float trim);
		public delegate void SetControlRotors(IEnumerable<IMyMotorStator> rotors, float sensitivity, float trim);

		private IMyCubeBlock m_block;
		private SetControlRotors m_onComplete;

		private MyTerminalControlListbox<MyCockpit> m_listbox;
		private List<MyGuiControlListbox.Item> m_allItems = new List<MyGuiControlListbox.Item>();
		private List<MyGuiControlListbox.Item> m_selected = new List<MyGuiControlListbox.Item>();

		private MyTerminalControlSlider<MyCockpit> m_sensitivitySlider;
		private float m_sensitivity = 1f;

		private MyTerminalControlSlider<MyCockpit> m_trimSlider;
		private float m_trim = 0f;

		private MyTerminalControlButton<MyCockpit> m_save;

		public RotorPicker(IMyTerminalBlock cockpit, string rotorName, ControlRotorParams rotorParams, SetControlRotors onComplete)
		{
			m_block = cockpit;
			m_onComplete = onComplete;

			IEnumerable<IMyMotorStator> selected;
			rotorParams(out selected, out m_sensitivity, out m_trim);
			m_trim = MathHelper.ToDegrees(m_trim);

			m_listbox = new MyTerminalControlListbox<MyCockpit>("Arms_RotorPicker", MyStringId.GetOrCompute(rotorName + " Rotors"), MyStringId.NullOrEmpty, true, 14);
			m_listbox.ListContent = ListContent;
			m_listbox.ItemSelected = ItemSelected;

			m_sensitivitySlider = new MyTerminalControlSlider<MyCockpit>("Arms_RotorPickerSensitivity", MyStringId.GetOrCompute("Control Sensitivity"), MyStringId.GetOrCompute("How sensitive the ship will be to input"));
			m_sensitivitySlider.DefaultValue = 1f;
			m_sensitivitySlider.Getter = b => m_sensitivity;
			m_sensitivitySlider.Setter = (b, value) => m_sensitivity = value;
			m_sensitivitySlider.SetLogLimits(0.01f, 100f);
			m_sensitivitySlider.Writer = (b, sb) => sb.Append(m_sensitivity);

			m_trimSlider = new MyTerminalControlSlider<MyCockpit>("Arms_RotorPickerTrim", MyStringId.GetOrCompute("Trim"), MyStringId.GetOrCompute("Default angle of rotors"));
			m_trimSlider.DefaultValue = 0f;
			m_trimSlider.Getter = b => m_trim;
			m_trimSlider.Setter = (b, value) => m_trim = value;
			m_trimSlider.SetLimits(-45f, 45f);
			m_trimSlider.Writer = (b, sb) => {
				sb.Append(m_trim);
				sb.Append('°');
			};

			m_save = new MyTerminalControlButton<MyCockpit>("Arms_RotorPickerSave", MyStringId.GetOrCompute("Save & Exit"), MyStringId.NullOrEmpty, SaveAndExit);

			CubeGridCache cache = CubeGridCache.GetFor(m_block.CubeGrid);

			if (cache == null)
				return;

			foreach (IMyMotorStator stator in cache.BlocksOfType(typeof(MyObjectBuilder_MotorStator)))
			{
				MyGuiControlListbox.Item item = new MyGuiControlListbox.Item(new StringBuilder(stator.DisplayNameText), userData: stator);
				m_allItems.Add(item);
				if (selected.Contains(stator))
					m_selected.Add(item);
			}

			MyTerminalControls.Static.CustomControlGetter += CustomControlGetter;
			cockpit.RebuildControls();
		}

		private void CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
		{
			if (block != m_block)
				return;

			controls.Clear();
			controls.Add(m_listbox);
			controls.Add(m_sensitivitySlider);
			controls.Add(m_trimSlider);
			controls.Add(m_save);
		}

		private void ListContent(MyCockpit cockpit, ICollection<MyGuiControlListbox.Item> items, ICollection<MyGuiControlListbox.Item> selected)
		{
			foreach (MyGuiControlListbox.Item item in m_allItems)
				items.Add(item);
			foreach (MyGuiControlListbox.Item item in m_selected)
				selected.Add(item);
		}

		private void ItemSelected(MyCockpit cockpit, List<MyGuiControlListbox.Item> selected)
		{
			m_selected.Clear();
			foreach (MyGuiControlListbox.Item item in selected)
				m_selected.Add(item);
		}

		private void SaveAndExit(MyCockpit cockpit)
		{
			MyTerminalControls.Static.CustomControlGetter -= CustomControlGetter;
			cockpit.RebuildControls();

			m_onComplete(m_selected.Select(item => (IMyMotorStator)item.UserData), m_sensitivity, MathHelper.ToRadians(m_trim));
		}

	}
}
