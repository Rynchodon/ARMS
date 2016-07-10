using System;
using System.Runtime.InteropServices;
using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Instruction;
using Rynchodon.Settings;
using Rynchodon.Update;
using Rynchodon.Utility.Network;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

namespace Rynchodon.Autopilot
{
	/// <summary>
	/// Autopilot terminal controls when not using GUI programming.
	/// </summary>
	public class AutopilotTerminal
	{

		[StructLayout(LayoutKind.Explicit)]
		private struct DistanceValues
		{
			[FieldOffset(0)]
			public float LinearDistance;
			[FieldOffset(4)]
			public float AngularDistance;
			[FieldOffset(0)]
			public long PackedValue;
		}

		private class StaticVariables
		{
			public Logger s_logger = new Logger("AutopilotTerminal");
			public MyTerminalControlCheckbox<MyShipController> autopilotControl;
			public MyTerminalControlTextbox<MyShipController> autopilotCommands;
		}

		private static StaticVariables Static = new StaticVariables();

		static AutopilotTerminal()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;

			AddControl(new MyTerminalControlSeparator<MyShipController>() { Enabled = ShipAutopilot.IsAutopilotBlock, Visible = ShipAutopilot.IsAutopilotBlock });

			Static.autopilotControl = new MyTerminalControlCheckbox<MyShipController>("ArmsAp_OnOff", MyStringId.GetOrCompute("ARMS Autopilot"), MyStringId.GetOrCompute("Enable ARMS Autopilot"));
			IMyTerminalValueControl<bool> valueControl = Static.autopilotControl;
			valueControl.Getter = GetAutopilotControl;
			valueControl.Setter = SetAutopilotControl;
			AddControl(Static.autopilotControl);
			AddAction(new MyTerminalAction<MyShipController>("ArmsAp_OnOff", new StringBuilder("ARMS Autopilot On/Off"), @"Textures\GUI\Icons\Actions\Toggle.dds") { Action = ToggleAutopilotControl });
			AddAction(new MyTerminalAction<MyShipController>("ArmsAp_On", new StringBuilder("ARMS Autopilot On"), @"Textures\GUI\Icons\Actions\SwitchOn.dds") { Action = block => SetAutopilotControl(block, true) });
			AddAction(new MyTerminalAction<MyShipController>("ArmsAp_Off", new StringBuilder("ARMS Autopilot Off"), @"Textures\GUI\Icons\Actions\SwitchOff.dds") { Action = block => SetAutopilotControl(block, false) });

			Static.autopilotCommands = new MyTerminalControlTextbox<MyShipController>("ArmsAp_Commands", MyStringId.GetOrCompute("Autopilot Commands"), MyStringId.NullOrEmpty);
			Static.autopilotCommands.Getter = GetAutopilotCommands;
			Static.autopilotCommands.Setter = SetAutopilotCommands;
			AddControl(Static.autopilotCommands);

			MyTerminalControlButton<MyShipController> gooeyProgram = new MyTerminalControlButton<MyShipController>("ArmsAp_GuiProgram", MyStringId.GetOrCompute("Program Autopilot"), MyStringId.GetOrCompute("Interactive programming for autopilot"), GooeyProgram);
			gooeyProgram.Enabled = ShipAutopilot.IsAutopilotBlock;
			AddControl(gooeyProgram);

			AddProperty(MyAPIGateway.TerminalControls.CreateProperty<Enum, Sandbox.ModAPI.Ingame.IMyShipController>("ArmsAp_Status"), autopilot => autopilot.m_autopilotStatus.Value);
			AddProperty(AutopilotFlags.HasControl);
			AddProperty(AutopilotFlags.MovementBlocked);
			AddProperty(AutopilotFlags.RotationBlocked);
			AddProperty(AutopilotFlags.EnemyFinderIssue);
			AddProperty(MyAPIGateway.TerminalControls.CreateProperty<DateTime, Sandbox.ModAPI.Ingame.IMyShipController>("ArmsAp_WaitUntil"), autopilot => new DateTime(autopilot.m_waitUntil.Value));
			AddProperty(MyAPIGateway.TerminalControls.CreateProperty<string, Sandbox.ModAPI.Ingame.IMyShipController>("ArmsAp_BlockedBy"), autopilot => GetNameForDisplay(autopilot, autopilot.m_blockedBy.Value));
			AddProperty(MyAPIGateway.TerminalControls.CreateProperty<float, Sandbox.ModAPI.Ingame.IMyShipController>("ArmsAp_LinearDistance"), autopilot => autopilot.LinearDistance);
			AddProperty(MyAPIGateway.TerminalControls.CreateProperty<float, Sandbox.ModAPI.Ingame.IMyShipController>("ArmsAp_AngularDistance"), autopilot => autopilot.AngularDistance);
			AddProperty(MyAPIGateway.TerminalControls.CreateProperty<Enum, Sandbox.ModAPI.Ingame.IMyShipController>("ArmsAp_ReasonCannotTarget"), autopilot => autopilot.m_reasonCannotTarget.Value);
			AddProperty(MyAPIGateway.TerminalControls.CreateProperty<string, Sandbox.ModAPI.Ingame.IMyShipController>("ArmsAp_EnemyFinderBestTarget"), autopilot => GetNameForDisplay(autopilot, autopilot.m_enemyFinderBestTarget.Value));
			AddProperty(MyAPIGateway.TerminalControls.CreateProperty<int, Sandbox.ModAPI.Ingame.IMyShipController>("ArmsAp_WelderUnfinishedBlocks"), autopilot => autopilot.m_welderUnfinishedBlocks.Value);
			AddProperty(MyAPIGateway.TerminalControls.CreateProperty<InfoString.StringId, Sandbox.ModAPI.Ingame.IMyShipController>("ArmsAp_Complaint"), autopilot => autopilot.m_complaint.Value);
		}

		private static void AddControl(MyTerminalControl<MyShipController> control)
		{
			control.Enabled = ShipAutopilot.IsAutopilotBlock;
			control.Visible = ShipAutopilot.IsAutopilotBlock;
			MyTerminalControlFactory.AddControl<MyShipController, MyCockpit>(control);
			if (ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bUseRemoteControl))
				MyTerminalControlFactory.AddControl<MyShipController, MyRemoteControl>(control);
		}

		private static void AddAction(MyTerminalAction<MyShipController> action)
		{
			action.Enabled = ShipAutopilot.IsAutopilotBlock;
			MyTerminalControlFactory.AddAction<MyShipController, MyCockpit>(action);
			if (ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bUseRemoteControl))
				MyTerminalControlFactory.AddAction<MyShipController, MyRemoteControl>(action);
		}

		private static void AddProperty<T>(IMyTerminalControlProperty<T> property, Func<AutopilotTerminal, T> function)
		{
			Static.s_logger.debugLog("entered");
			Static.s_logger.debugLog("property: " + property);
			property.Enabled = ShipAutopilot.IsAutopilotBlock;
			property.Visible = block => false;

			property.Getter = block => {
				AutopilotTerminal autopilot;
				if (!Registrar.TryGetValue(block, out autopilot))
				{
					if (Static != null)
						Static.s_logger.alwaysLog("Failed lookup of block: " + block.getBestName(), Logger.severity.WARNING);
					return default(T);
				}
				return function.Invoke(autopilot);
			};

			Static.s_logger.debugLog("adding property");
			MyTerminalControlFactory.AddControl<MyShipController, MyCockpit>((MyTerminalControl<MyShipController>)property);
			if (ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bUseRemoteControl))
				MyTerminalControlFactory.AddControl<MyShipController, MyRemoteControl>((MyTerminalControl<MyShipController>)property);
		}

		private static void AddProperty(AutopilotFlags flag)
		{
			AddProperty(MyAPIGateway.TerminalControls.CreateProperty<bool, Sandbox.ModAPI.Ingame.IMyShipController>("ArmsAp_" + flag), autopilot => (autopilot.m_autopilotFlags.Value & flag) != 0);
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			Static = null;
		}

		private static bool GetAutopilotControl(IMyTerminalBlock block)
		{
			AutopilotTerminal autopilot;
			if (!Registrar.TryGetValue(block, out autopilot))
			{
				if (Static != null)
					Static.s_logger.alwaysLog("Failed lookup of block: " + block.getBestName(), Logger.severity.WARNING);
				return false;
			}

			return autopilot.m_autopilotControl.Value;
		}

		private static void SetAutopilotControl(IMyTerminalBlock block, bool value)
		{
			AutopilotTerminal autopilot;
			if (!Registrar.TryGetValue(block, out autopilot))
			{
				if (Static != null)
					Static.s_logger.alwaysLog("Failed lookup of block: " + block.getBestName(), Logger.severity.WARNING);
				return;
			}

			autopilot.m_autopilotControl.Value = value;
		}

		private static void ToggleAutopilotControl(IMyTerminalBlock block)
		{
			AutopilotTerminal autopilot;
			if (!Registrar.TryGetValue(block, out autopilot))
			{
				if (Static != null)
					Static.s_logger.alwaysLog("Failed lookup of block: " + block.getBestName(), Logger.severity.WARNING);
				return;
			}

			autopilot.m_autopilotControl.Value = !autopilot.m_autopilotControl.Value;
		}

		public static StringBuilder GetAutopilotCommands(IMyTerminalBlock block)
		{
			AutopilotTerminal autopilot;
			if (!Registrar.TryGetValue(block, out autopilot))
			{
				if (Static != null)
					Static.s_logger.alwaysLog("Failed lookup of block: " + block.getBestName(), Logger.severity.WARNING);
				return new StringBuilder();
			}

			return autopilot.m_autopilotCommands.Value;
		}

		public static void SetAutopilotCommands(IMyTerminalBlock block, StringBuilder value)
		{
			AutopilotTerminal autopilot;
			if (!Registrar.TryGetValue(block, out autopilot))
			{
				if (Static != null)
					Static.s_logger.alwaysLog("Failed lookup of block: " + block.getBestName(), Logger.severity.WARNING);
				return;
			}

			autopilot.m_autopilotCommands.Value = value;
		}

		public static void GooeyProgram(MyShipController block)
		{
			AutopilotCommands cmds = AutopilotCommands.GetOrCreate(block);
			if (cmds != null)
				cmds.StartGooeyProgramming();
		}

		private static string GetNameForDisplay(AutopilotTerminal autopilot, long entityId)
		{
			IMyEntity entity;
			if (!MyAPIGateway.Entities.TryGetEntityById(entityId, out entity))
				return null;
			return entity.GetNameForDisplay(autopilot.m_block.OwnerId);
		}

		private readonly Logger m_logger;
		private readonly IMyTerminalBlock m_block;

		#region Terminal Controls

		private EntityValue<bool> m_autopilotControl;
		private EntityStringBuilder m_autopilotCommands;

		public bool AutopilotControlSwitch
		{
			get { return m_autopilotControl.Value; }
			set { m_autopilotControl.Value = value; }
		}

		public StringBuilder AutopilotCommandsText
		{
			get { return m_autopilotCommands.Value; }
			set { m_autopilotCommands.Value = value; }
		}

		#endregion Terminal Controls

		#region Terminal Properties

		[Flags]
		public enum AutopilotFlags : byte
		{
			None = 0,
			HasControl = 1,
			MovementBlocked = 2,
			RotationBlocked = 4,
			EnemyFinderIssue = 8,
		}

		public EntityValue<ShipAutopilot.State> m_autopilotStatus;
		public EntityValue<AutopilotFlags> m_autopilotFlags;
		/// <summary>Use DateTime as ElapsedTime is not network friendly</summary>
		public EntityValue<long> m_waitUntil;
		public EntityValue<long> m_blockedBy;
		public EntityValue<long> m_distance;
		public EntityValue<GridFinder.ReasonCannotTarget> m_reasonCannotTarget;
		public EntityValue<long> m_enemyFinderBestTarget;
		public EntityValue<int> m_welderUnfinishedBlocks;
		public EntityValue<InfoString.StringId> m_complaint;

		public float LinearDistance { get { return new DistanceValues() { PackedValue = m_distance.Value }.LinearDistance; } }

		public float AngularDistance { get { return new DistanceValues() { PackedValue = m_distance.Value }.AngularDistance; } }

		public void SetDistance(float linear, float angular)
		{
			m_distance.Value = new DistanceValues() { LinearDistance = linear, AngularDistance = angular }.PackedValue;
		}

		#endregion Terminal Properties

		public AutopilotTerminal(IMyCubeBlock block)
		{
			this.m_logger = new Logger("AutopilotTerminal", block);
			this.m_block = block as IMyTerminalBlock;

			byte index = 0;
			this.m_autopilotControl = new EntityValue<bool>(block, index++, Static.autopilotControl.UpdateVisual, Saver.Instance.LoadOldVersion(69) && ((MyShipController)block).ControlThrusters);
			this.m_autopilotCommands = new EntityStringBuilder(block, index++, () => {
				Static.autopilotCommands.UpdateVisual();
				AutopilotCommands cmds = AutopilotCommands.GetOrCreate((IMyTerminalBlock)block);
				if (cmds != null)
					cmds.OnCommandsChanged();
			});

			this.m_autopilotStatus = new EntityValue<ShipAutopilot.State>(block, index++, m_block.RefreshCustomInfo, save: false);
			this.m_autopilotFlags = new EntityValue<AutopilotFlags>(block, index++, m_block.RefreshCustomInfo, save: false);
			this.m_waitUntil = new EntityValue<long>(block, index++, m_block.RefreshCustomInfo, save: false);
			this.m_blockedBy = new EntityValue<long>(block, index++, m_block.RefreshCustomInfo, save: false);
			this.m_distance = new EntityValue<long>(block, index++, m_block.RefreshCustomInfo, save: false);
			this.m_reasonCannotTarget = new EntityValue<GridFinder.ReasonCannotTarget>(block, index++, m_block.RefreshCustomInfo, save: false);
			this.m_enemyFinderBestTarget = new EntityValue<long>(block, index++, m_block.RefreshCustomInfo, save: false);
			this.m_welderUnfinishedBlocks = new EntityValue<int>(block, index++, m_block.RefreshCustomInfo, save: false);
			this.m_complaint = new EntityValue<InfoString.StringId>(block, index++, m_block.RefreshCustomInfo, save: false);

			m_block.AppendingCustomInfo += AppendingCustomInfo;

			Registrar.Add(block, this);
			m_logger.debugLog("Initialized", Logger.severity.INFO);
		}

		public void AppendingCustomInfo(IMyTerminalBlock block, StringBuilder customInfo)
		{
			AppendingCustomInfo(customInfo);
		}

		public void AppendingCustomInfo(StringBuilder customInfo)
		{
			if (m_autopilotStatus.Value == ShipAutopilot.State.Halted)
				customInfo.AppendLine("Autopilot crashed, see log for details");
			if (!HasFlag(AutopilotFlags.HasControl))
			{
				if (m_autopilotStatus.Value == ShipAutopilot.State.Disabled)
					customInfo.AppendLine("Disabled");
				else if (m_block.CubeGrid.IsStatic)
					customInfo.AppendLine("Grid is a station");
				else if (!m_block.IsWorking)
					customInfo.AppendLine("Not working");
				else
				{
					MyCubeGrid mcg = (MyCubeGrid)m_block.CubeGrid;
					if (mcg.HasMainCockpit() && !((MyShipController)m_block).IsMainCockpit)
						customInfo.AppendLine("Not main cockpit");
					else
						customInfo.AppendLine("Another autopilot controlling ship");
				}
			}

			bool waiting = false;

			DateTime waitUntil = new DateTime(m_waitUntil.Value);
			if (waitUntil > DateTime.UtcNow)
			{
				waiting = true;
				customInfo.Append("Waiting for ");
				customInfo.AppendLine(PrettySI.makePretty(waitUntil - DateTime.UtcNow));
			}

			IMyPlayer controlling = MyAPIGateway.Players.GetPlayerControllingEntity(m_block.CubeGrid);
			if (controlling != null)
			{
				waiting = true;
				customInfo.Append("Player controlling: ");
				customInfo.AppendLine(controlling.DisplayName);
			}

			if (waiting)
				return;

			// pathfinder
			if (HasFlag(AutopilotFlags.MovementBlocked | AutopilotFlags.RotationBlocked))
			{
				string blockedBy = GetNameForDisplay(this, m_blockedBy.Value);
				customInfo.AppendLine("Pathfinder:");
				if (HasFlag(AutopilotFlags.MovementBlocked))
				{
					if (blockedBy != null)
					{
						customInfo.Append("Movement blocked by ");
						customInfo.AppendLine(blockedBy);
					}
					else
						customInfo.AppendLine("Cannot move");
				}
				else
				{
					if (blockedBy != null)
					{
						customInfo.Append("Rotation blocked by ");
						customInfo.AppendLine(blockedBy);
					}
					else
						customInfo.AppendLine("Cannot rotate");
				}
			}

			// nav mover info
			// if has mover
			customInfo.Append("Distance: ");
			customInfo.Append(PrettySI.makePretty(LinearDistance));
			customInfo.AppendLine("m");

			// nav rotator info

			// enemy finder
			if (HasFlag(AutopilotFlags.EnemyFinderIssue))
			{
				switch (m_reasonCannotTarget.Value)
				{
					case GridFinder.ReasonCannotTarget.None:
						customInfo.AppendLine("No enemy detected");
						break;
					case GridFinder.ReasonCannotTarget.Too_Far:
						customInfo.Append(GetNameForDisplay(this, m_enemyFinderBestTarget.Value));
						customInfo.AppendLine(" is too far");
						break;
					case GridFinder.ReasonCannotTarget.Too_Fast:
						customInfo.Append(GetNameForDisplay(this, m_enemyFinderBestTarget.Value));
						customInfo.AppendLine(" is too fast");
						break;
					case GridFinder.ReasonCannotTarget.Grid_Condition:
						customInfo.Append(GetNameForDisplay(this, m_enemyFinderBestTarget.Value));
						customInfo.AppendLine(" cannot be targeted");
						break;
				}
			}

			// complaint
			InfoString.StringId ids  = m_complaint.Value;
			if (ids != InfoString.StringId.None)
				foreach (InfoString.StringId flag in InfoString.AllStringIds())
					if ((ids & flag) != 0)
						customInfo.AppendLine(InfoString.GetString(flag));
		}

		private bool HasFlag(AutopilotFlags flag)
		{
			return (m_autopilotFlags.Value & flag) != 0;
		}

	}

}
