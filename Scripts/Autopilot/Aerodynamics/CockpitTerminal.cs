using System;
using System.Collections.Generic;
using Rynchodon.Settings;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Rynchodon.Autopilot.Aerodynamics
{
	class CockpitTerminal
	{

		private class StaticVariables
		{
			public MyTerminalControlSeparator<MyCockpit> Separator;
			public MyTerminalControlCheckbox<MyCockpit> AeroShow;
			public IMyTerminalControlCombobox DirectionSelector;
			public MyTerminalControlCheckbox<MyCockpit> EnableHelper;
			public MyTerminalControlButton<MyCockpit> AileronButton, ElevatorButton, RudderButton;

			public StaticVariables()
			{
				Separator = new MyTerminalControlSeparator<MyCockpit>();

				AeroShow = new MyTerminalControlCheckbox<MyCockpit>("Arms_AeroShow", MyStringId.GetOrCompute("Draw Air Resistance"), MyStringId.GetOrCompute("Draw ARMS calculations of air resistance."));
				AeroShow.Getter = Get_AeroTerminal;
				AeroShow.Setter = Set_AeroTerminal;

				DirectionSelector = MyTerminalControls.Static.CreateControl<IMyTerminalControlCombobox, IMyCockpit>("Arms_AeroDirection");
				DirectionSelector.ComboBoxContent = ListDirections;
				DirectionSelector.Getter = DirectionGetter;
				DirectionSelector.Setter = DirectionSetter;
				DirectionSelector.Title = MyStringId.GetOrCompute("Direction of Movement");

				EnableHelper = new MyTerminalControlCheckbox<MyCockpit>("Arms_FlightHelper", MyStringId.GetOrCompute("Flight Control Assist"), MyStringId.GetOrCompute("Enable ARMS Flight Control Assistance for atmospheric flight"));
				EnableHelper.Getter = Get_Helper;
				EnableHelper.Setter = Set_Helper;

				AileronButton = GetRotorButton("Aileron");
				AileronButton.Action = RotorButtonAileron;
				ElevatorButton = GetRotorButton("Elevator");
				ElevatorButton.Action = RotorButtonElevator;
				RudderButton = GetRotorButton("Rudder");
				RudderButton.Action = RotorButtonRudder;
			}
		}

		private static StaticVariables value_instance;
		private static StaticVariables Instance
		{
			get
			{
				if (Globals.WorldClosed)
					throw new Exception("World closed");
				if (value_instance == null)
					value_instance = new StaticVariables();
				return value_instance;
			}
			set { value_instance = value; }
		}

		[OnWorldLoad]
		private static void Load()
		{
			if (ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bAirResistanceBeta))
				MyTerminalControls.Static.CustomControlGetter += CustomControlGetter;
		}

		[OnWorldClose]
		private static void Unload()
		{
			MyTerminalControls.Static.CustomControlGetter -= CustomControlGetter;
			value_instance = null;
		}

		private static MyTerminalControlButton<MyCockpit> GetRotorButton(string name)
		{
			return new MyTerminalControlButton<MyCockpit>("Arms_FlightHelper" + name, MyStringId.GetOrCompute(name + " Rotors"), MyStringId.GetOrCompute("Choose " + name + " rotors for Flight Control Assistance"), null);
		}

		private static void CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
		{
			CockpitTerminal cockpitTerm;
			if (!Registrar.TryGetValue(block.EntityId, out cockpitTerm))
				return;

			controls.Add(Instance.Separator);
			controls.Add(Instance.AeroShow);
			if (cockpitTerm.m_aeroShow)
				controls.Add(Instance.DirectionSelector);
			controls.Add(Instance.EnableHelper);
			if (cockpitTerm.m_controlHelper != null)
			{
				controls.Add(Instance.AileronButton);
				controls.Add(Instance.ElevatorButton);
				controls.Add(Instance.RudderButton);
			}
		}

		private static bool Get_AeroTerminal(IMyTerminalBlock block)
		{
			CockpitTerminal cockpitTerminal;
			if (!Registrar.TryGetValue(block, out cockpitTerminal))
			{
				Logger.AlwaysLog("Failed lookup of block: " + block.getBestName(), Logger.severity.WARNING);
				return false;
			}

			return cockpitTerminal.m_aeroShow;
		}

		private static void Set_AeroTerminal(IMyTerminalBlock block, bool value)
		{
			CockpitTerminal cockpitTerminal;
			if (!Registrar.TryGetValue(block, out cockpitTerminal))
			{
				Logger.AlwaysLog("Failed lookup of block: " + block.getBestName(), Logger.severity.WARNING);
				return;
			}

			cockpitTerminal.m_aeroShow = value;
			block.RebuildControls();
		}

		private static bool Get_Helper(IMyTerminalBlock block)
		{
			CockpitTerminal cockpitTerminal;
			if (!Registrar.TryGetValue(block, out cockpitTerminal))
			{
				Logger.AlwaysLog("Failed lookup of block: " + block.getBestName(), Logger.severity.WARNING);
				return false;
			}

			return cockpitTerminal.m_controlHelper != null;
		}

		private static void Set_Helper(IMyTerminalBlock block, bool value)
		{
			CockpitTerminal cockpitTerminal;
			if (!Registrar.TryGetValue(block, out cockpitTerminal))
			{
				Logger.AlwaysLog("Failed lookup of block: " + block.getBestName(), Logger.severity.WARNING);
				return;
			}

			if (value)
			{
				cockpitTerminal.m_controlHelper = new FlightControlAssist(cockpitTerminal.m_cockpit);
				block.RebuildControls();
			}
			else
			{
				cockpitTerminal.m_controlHelper.Disable();
				cockpitTerminal.m_controlHelper = null;
			}
		}

		private static void ListDirections(List<MyTerminalControlComboBoxItem> list)
		{
			for (int index = 0; index < Base6Directions.EnumDirections.Length; index++)
				list.Add(new MyTerminalControlComboBoxItem() { Key = index, Value = MyStringId.GetOrCompute(Base6Directions.EnumDirections[index].ToString()) });
		}

		private static long DirectionGetter(IMyTerminalBlock cockpit)
		{
			CockpitTerminal cockpitTerm;
			if (!Registrar.TryGetValue(cockpit, out cockpitTerm))
			{
				Logger.AlwaysLog("Failed lookup of block: " + cockpit.getBestName(), Logger.severity.WARNING);
				return 0L;
			}

			for (int index = 0; index < Base6Directions.EnumDirections.Length; index++)
				if (Base6Directions.EnumDirections[index] == cockpitTerm.m_aeroShowDirection)
					return index;

			Logger.AlwaysLog("Direction out of bounds: " + cockpitTerm.m_aeroShowDirection, Logger.severity.WARNING);
			return 0L;
		}

		private static void DirectionSetter(IMyTerminalBlock cockpit, long value)
		{
			CockpitTerminal cockpitTerm;
			if (!Registrar.TryGetValue(cockpit, out cockpitTerm))
			{
				Logger.AlwaysLog("Failed lookup of block: " + cockpit.getBestName(), Logger.severity.WARNING);
				return;
			}

			cockpitTerm.m_aeroShowDirection = Base6Directions.EnumDirections[value];
		}

		private static void RotorButtonAileron(MyCockpit block)
		{
			CockpitTerminal cockpitTerminal;
			if (!Registrar.TryGetValue(block, out cockpitTerminal))
			{
				Logger.AlwaysLog("Failed lookup of block: " + block.getBestName(), Logger.severity.WARNING);
				return;
			}

			new RotorPicker(block, "Aileron", cockpitTerminal.m_controlHelper.AileronParams, cockpitTerminal.m_controlHelper.SetAilerons);
		}

		private static void RotorButtonElevator(MyCockpit block)
		{
			CockpitTerminal cockpitTerminal;
			if (!Registrar.TryGetValue(block, out cockpitTerminal))
			{
				Logger.AlwaysLog("Failed lookup of block: " + block.getBestName(), Logger.severity.WARNING);
				return;
			}

			new RotorPicker(block, "Elevator", cockpitTerminal.m_controlHelper.ElevatorParams, cockpitTerminal.m_controlHelper.SetElevators);
		}

		private static void RotorButtonRudder(MyCockpit block)
		{
			CockpitTerminal cockpitTerminal;
			if (!Registrar.TryGetValue(block, out cockpitTerminal))
			{
				Logger.AlwaysLog("Failed lookup of block: " + block.getBestName(), Logger.severity.WARNING);
				return;
			}

			new RotorPicker(block, "Rudder", cockpitTerminal.m_controlHelper.RudderParams, cockpitTerminal.m_controlHelper.SetRudders);
		}

		private readonly MyCockpit m_cockpit;

		private Base6Directions.Direction m_aeroShowDirection;
		private bool m_aeroShow;

		private AeroProfiler value_profiler;
		private AeroProfiler m_profiler
		{
			get { return value_profiler; }
			set
			{
				if (value_profiler != null)
				{
					value_profiler.DisableDraw();
					m_cockpit.CubeGrid.OnBlockAdded -= CubeGrid_OnBlockChange;
					m_cockpit.CubeGrid.OnBlockRemoved -= CubeGrid_OnBlockChange;
				}
				if (value != null)
				{
					m_cockpit.CubeGrid.OnBlockAdded += CubeGrid_OnBlockChange;
					m_cockpit.CubeGrid.OnBlockRemoved += CubeGrid_OnBlockChange;
				}
				value_profiler = value;
			}
		}

		private void CubeGrid_OnBlockChange(Sandbox.Game.Entities.Cube.MySlimBlock obj)
		{
			value_profiler = null;
		}

		private FlightControlAssist m_controlHelper;

		public CockpitTerminal(IMyCubeBlock cockpit)
		{
			this.m_cockpit = (MyCockpit)cockpit;

			Registrar.Add(this.m_cockpit, this);
		}

		public void Update1()
		{
			if (!m_aeroShow)
			{
				m_profiler = null;
				return;
			}

			Base6Directions.Direction gridDirection = m_cockpit.Orientation.TransformDirection(m_aeroShowDirection);
			if (m_profiler == null || m_profiler.m_drawDirection.Value != gridDirection)
				m_profiler = new AeroProfiler(m_cockpit.CubeGrid, gridDirection);
		}

	}
}
