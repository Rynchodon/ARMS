using System;
using System.Runtime.InteropServices;
using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Instruction;
using Rynchodon.Autopilot.Pathfinding;
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
			public Logger s_logger = new Logger();
			public MyTerminalControlCheckbox<MyShipController> autopilotControl;
			public MyTerminalControlTextbox<MyShipController> autopilotCommands;

			public StaticVariables()
			{
			Logger.DebugLog("entered", Logger.severity.TRACE);
				AddControl(new MyTerminalControlSeparator<MyShipController>() { Enabled = ShipAutopilot.IsAutopilotBlock, Visible = ShipAutopilot.IsAutopilotBlock });

				autopilotControl = new MyTerminalControlCheckbox<MyShipController>("ArmsAp_OnOff", MyStringId.GetOrCompute("ARMS Autopilot"), MyStringId.GetOrCompute("Enable ARMS Autopilot"));
				IMyTerminalValueControl<bool> valueControl = autopilotControl;
				valueControl.Getter = GetAutopilotControl;
				valueControl.Setter = SetAutopilotControl;
				AddControl(autopilotControl);
				AddAction(new MyTerminalAction<MyShipController>("ArmsAp_OnOff", new StringBuilder("ARMS Autopilot On/Off"), @"Textures\GUI\Icons\Actions\Toggle.dds") { Action = ToggleAutopilotControl });
				AddAction(new MyTerminalAction<MyShipController>("ArmsAp_On", new StringBuilder("ARMS Autopilot On"), @"Textures\GUI\Icons\Actions\SwitchOn.dds") { Action = block => SetAutopilotControl(block, true) });
				AddAction(new MyTerminalAction<MyShipController>("ArmsAp_Off", new StringBuilder("ARMS Autopilot Off"), @"Textures\GUI\Icons\Actions\SwitchOff.dds") { Action = block => SetAutopilotControl(block, false) });

				autopilotCommands = new MyTerminalControlTextbox<MyShipController>("ArmsAp_Commands", MyStringId.GetOrCompute("Autopilot Commands"), MyStringId.NullOrEmpty);
				autopilotCommands.Getter = GetAutopilotCommands;
				autopilotCommands.Setter = SetAutopilotCommands;
				AddControl(autopilotCommands);

				MyTerminalControlButton<MyShipController> gooeyProgram = new MyTerminalControlButton<MyShipController>("ArmsAp_GuiProgram", MyStringId.GetOrCompute("Program Autopilot"), MyStringId.GetOrCompute("Interactive programming for autopilot"), GooeyProgram);
				gooeyProgram.Enabled = ShipAutopilot.IsAutopilotBlock;
				AddControl(gooeyProgram);

				AddProperty<Enum>("ArmsAp_Status", autopilot => autopilot.m_autopilotStatus.Value);
				foreach (AutopilotFlags flag in Enum.GetValues(typeof(AutopilotFlags)))
					if (flag != 0)
						AddProperty(flag);
				AddProperty<Enum>("ArmsAp_PathStatus", autopilot => autopilot.m_pathfinderState.Value);
				AddProperty<Enum>("ArmsAp_ReasonCannotTarget", autopilot => autopilot.m_reasonCannotTarget.Value);
				AddProperty<Enum>("ArmsAp_Complaint", autopilot => autopilot.m_complaint.Value);
				AddProperty("ArmsAp_WaitUntil", autopilot => new DateTime(autopilot.m_waitUntil.Value));
				AddProperty("ArmsAp_BlockedBy", autopilot => GetNameForDisplay(autopilot, autopilot.m_blockedBy.Value));
				AddProperty("ArmsAp_LinearDistance", autopilot => autopilot.LinearDistance);
				AddProperty("ArmsAp_AngularDistance", autopilot => autopilot.AngularDistance);
				AddProperty("ArmsAp_EnemyFinderBestTarget", autopilot => GetNameForDisplay(autopilot, autopilot.m_enemyFinderBestTarget.Value));
				AddProperty("ArmsAp_WelderUnfinishedBlocks", autopilot => autopilot.m_welderUnfinishedBlocks.Value);
				AddProperty("ArmsAp_NavigatorMover", autopilot => autopilot.m_prevNavMover.Value);
				AddProperty("ArmsAp_NavigatorRotator", autopilot => autopilot.m_prevNavRotator.Value);
				AddProperty("ArmsAp_NavigatorMoverInfo", autopilot => autopilot.m_prevNavMoverInfo.Value);
				AddProperty("ArmsAp_NavigatorRotatorInfo", autopilot => autopilot.m_prevNavRotatorInfo.Value);
			}
		}

		private static StaticVariables value_static;
		private static StaticVariables Static
		{
			get
			{
				if (Globals.WorldClosed)
					throw new Exception("World closed");
				if (value_static == null)
					value_static = new StaticVariables();
				return value_static;
			}
			set { value_static = value; }
		}

		[OnWorldClose]
		private static void Unload()
		{
			Static = null;
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

		private static void AddProperty<T>(string id, Func<AutopilotTerminal, T> function)
		{
			IMyTerminalControlProperty<T> property = MyAPIGateway.TerminalControls.CreateProperty<T, Sandbox.ModAPI.Ingame.IMyShipController>(id);
			property.Enabled = ShipAutopilot.IsAutopilotBlock;
			property.Visible = block => false;

			property.Getter = block => {
				AutopilotTerminal autopilot;
				if (!Registrar.TryGetValue(block, out autopilot))
				{
					Static.s_logger.alwaysLog("Failed lookup of block: " + block.getBestName(), Logger.severity.WARNING);
					return default(T);
				}
				return function.Invoke(autopilot);
			};

			MyTerminalControlFactory.AddControl<MyShipController, MyCockpit>((MyTerminalControl<MyShipController>)property);
			if (ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bUseRemoteControl))
				MyTerminalControlFactory.AddControl<MyShipController, MyRemoteControl>((MyTerminalControl<MyShipController>)property);
		}

		private static void AddProperty(AutopilotFlags flag)
		{
			AddProperty("ArmsAp_" + flag, autopilot => (autopilot.m_autopilotFlags.Value & flag) != 0);
		}

		private static bool GetAutopilotControl(IMyTerminalBlock block)
		{
			AutopilotTerminal autopilot;
			if (!Registrar.TryGetValue(block, out autopilot))
			{
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
				return "Unknown Entity";
			return entity.GetNameForDisplay(autopilot.m_block.OwnerId);
		}

		private readonly Logger m_logger;
		public readonly IMyTerminalBlock m_block;

		private bool m_waitUpdate;

		private bool WaitingNeedsUpdate
		{
			get { return m_waitUpdate; }
			set
			{
				if (value == m_waitUpdate)
					return;
				m_waitUpdate = value;
				if (m_waitUpdate)
					UpdateManager.Register(10u, RefreshWhileWaiting);
				else
					UpdateManager.Unregister(10u, RefreshWhileWaiting);
			}
		}

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
			//MovementBlocked = 2,
			RotationBlocked = 4,
			EnemyFinderIssue = 8,
			HasNavigatorMover = 16,
			HasNavigatorRotator = 32,
		}

		public EntityValue<ShipAutopilot.State> m_autopilotStatus;
		public EntityValue<AutopilotFlags> m_autopilotFlags;
		public EntityValue<Pathfinder.State> m_pathfinderState;
		public EntityValue<GridFinder.ReasonCannotTarget> m_reasonCannotTarget;
		public EntityValue<InfoString.StringId> m_complaint;
		public EntityValue<long> m_blockedBy;
		public EntityValue<long> m_enemyFinderBestTarget;
		public EntityValue<int> m_welderUnfinishedBlocks;
		public EntityValue<string> m_prevNavMover, m_prevNavRotator;
		public EntityStringBuilder m_prevNavMoverInfo, m_prevNavRotatorInfo;

		/// <summary>Use DateTime as ElapsedTime is not network friendly</summary>
		private EntityValue<long> m_waitUntil;
		private EntityValue<long> m_distance;

		public float LinearDistance { get { return new DistanceValues() { PackedValue = m_distance.Value }.LinearDistance; } }

		public float AngularDistance { get { return new DistanceValues() { PackedValue = m_distance.Value }.AngularDistance; } }

		public void SetDistance(float linear, float angular)
		{
			DistanceValues dv = new DistanceValues() { PackedValue = m_distance.Value };
			if (Math.Abs(linear - dv.LinearDistance) > 0.1f || Math.Abs(angular - dv.AngularDistance) > 0.01f)
				m_distance.Value = new DistanceValues() { LinearDistance = linear, AngularDistance = angular }.PackedValue;
		}

		public void SetWaitUntil(TimeSpan waitUntil)
		{
			TimeSpan elapsed = Globals.ElapsedTime;
			long newValue = (DateTime.UtcNow + waitUntil - elapsed).Ticks;
			long difference = Math.Abs((new DateTime(m_waitUntil.Value) - new DateTime(newValue)).Ticks);
			if (difference > TimeSpan.TicksPerSecond)
				m_waitUntil.Value = newValue;
		}

		#endregion Terminal Properties

		public AutopilotTerminal(IMyCubeBlock block)
		{
			this.m_logger = new Logger(block);
			this.m_block = block as IMyTerminalBlock;

			byte index = 0;
			this.m_autopilotControl = new EntityValue<bool>(block, index++, Static.autopilotControl.UpdateVisual, false);
			this.m_autopilotCommands = new EntityStringBuilder(block, index++, () => {
				Static.autopilotCommands.UpdateVisual();
				AutopilotCommands cmds = AutopilotCommands.GetOrCreate((IMyTerminalBlock)block);
				if (cmds != null)
					cmds.OnCommandsChanged();
			});

			this.m_autopilotStatus = new EntityValue<ShipAutopilot.State>(block, index++, m_block.RefreshCustomInfo, save: false);
			this.m_autopilotFlags = new EntityValue<AutopilotFlags>(block, index++, m_block.RefreshCustomInfo, save: false);
			this.m_pathfinderState = new EntityValue<Pathfinder.State>(block, index++, m_block.RefreshCustomInfo, save: false);
			this.m_reasonCannotTarget = new EntityValue<GridFinder.ReasonCannotTarget>(block, index++, m_block.RefreshCustomInfo, save: false);
			this.m_complaint = new EntityValue<InfoString.StringId>(block, index++, m_block.RefreshCustomInfo, save: false);
			this.m_waitUntil = new EntityValue<long>(block, index++, m_block.RefreshCustomInfo, save: false);
			this.m_blockedBy = new EntityValue<long>(block, index++, m_block.RefreshCustomInfo, save: false);
			this.m_distance = new EntityValue<long>(block, index++, m_block.RefreshCustomInfo, save: false);
			this.m_enemyFinderBestTarget = new EntityValue<long>(block, index++, m_block.RefreshCustomInfo, save: false);
			this.m_welderUnfinishedBlocks = new EntityValue<int>(block, index++, m_block.RefreshCustomInfo, save: false);
			this.m_prevNavMover = new EntityValue<string>(block, index++, m_block.RefreshCustomInfo, string.Empty, save: false);
			this.m_prevNavRotator = new EntityValue<string>(block, index++, m_block.RefreshCustomInfo, string.Empty, save: false);
			this.m_prevNavMoverInfo = new EntityStringBuilder(block, index++, m_block.RefreshCustomInfo, save: false);
			this.m_prevNavRotatorInfo = new EntityStringBuilder(block, index++, m_block.RefreshCustomInfo, save: false);

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
				if (MyAPIGateway.Multiplayer.IsServer)
					customInfo.AppendLine("Autopilot crashed, please upload log files and report on steam page");
				else
					customInfo.AppendLine("Autopilot crashed, please upload server's log files and report on steam page");
			if (m_pathfinderState.Value == Pathfinder.State.Crashed)
				if (MyAPIGateway.Multiplayer.IsServer)
					customInfo.AppendLine("Pathfinder crashed, please upload log files and report on steam page");
				else
					customInfo.AppendLine("Pathfinder crashed, please upload server's log files and report on steam page");

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
						customInfo.AppendLine("Does not have control of ship");
				}
			}

			bool waiting = false;

			DateTime waitUntil = new DateTime(m_waitUntil.Value);
			if (waitUntil > DateTime.UtcNow)
			{
				waiting = true;
				WaitingNeedsUpdate = true;
				customInfo.Append("Waiting for ");
				customInfo.AppendLine(PrettySI.makePretty(waitUntil - DateTime.UtcNow));
			}
			else
				WaitingNeedsUpdate = false;

			IMyPlayer controlling = MyAPIGateway.Players.GetPlayerControllingEntity(m_block.CubeGrid);
			if (controlling != null)
			{
				waiting = true;
				customInfo.Append("Player controlling: ");
				customInfo.AppendLine(controlling.DisplayName);
			}

			if (waiting)
			{
				AppendingCustomInfo_EnemyFinder(customInfo);
				return;
			}

			// pathfinder
			switch (m_pathfinderState.Value)
			{
				case Pathfinder.State.SearchingForPath:
					customInfo.Append("Searching for path around ");
					customInfo.AppendLine(GetNameForDisplay(this, m_blockedBy.Value));
					break;
				case Pathfinder.State.FollowingPath:
					customInfo.Append("Following path around ");
					customInfo.AppendLine(GetNameForDisplay(this, m_blockedBy.Value));
					break;
				case Pathfinder.State.FailedToFindPath:
					customInfo.Append("No path around ");
					customInfo.AppendLine(GetNameForDisplay(this, m_blockedBy.Value));
					break;
				default:
					if (HasFlag(AutopilotFlags.RotationBlocked))
					{
						customInfo.Append("Rotation blocked by ");
						customInfo.AppendLine(GetNameForDisplay(this, m_blockedBy.Value));
					}
					break;
			}

			// nav mover info
			if (HasFlag(AutopilotFlags.HasNavigatorMover))
			{
				customInfo.Append(m_prevNavMoverInfo.Value);
				customInfo.Append("Distance: ");
				customInfo.Append(PrettySI.makePretty(LinearDistance));
				customInfo.AppendLine("m");
			}

			// nav rotator info
			if (HasFlag(AutopilotFlags.HasNavigatorRotator))
			{
				customInfo.Append(m_prevNavRotatorInfo.Value);
				customInfo.Append("Angle: ");
				customInfo.Append(PrettySI.toSigFigs(AngularDistance));
				customInfo.AppendLine(" rad");
			}

			// enemy finder
			AppendingCustomInfo_EnemyFinder(customInfo);

			// complaint
			InfoString.StringId ids = m_complaint.Value;
			if (ids != InfoString.StringId.None)
				foreach (InfoString.StringId flag in InfoString.AllStringIds())
					if ((ids & flag) != 0)
						customInfo.AppendLine(InfoString.GetString(flag));

			// power
			customInfo.Append("Current Input: ");
			customInfo.Append(PrettySI.makePretty(((MyCubeBlock)m_block).ResourceSink.RequiredInput * 1e6f));
			customInfo.AppendLine("W");
		}

		private void AppendingCustomInfo_EnemyFinder(StringBuilder customInfo)
		{
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
		}

		private bool HasFlag(AutopilotFlags flag)
		{
			return (m_autopilotFlags.Value & flag) != 0;
		}

		private void RefreshWhileWaiting()
		{
			if (m_block.Closed)
			{
				WaitingNeedsUpdate = false;
				return;
			}
			m_block.RefreshCustomInfo();
		}

	}

}
