using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Instruction;
using Rynchodon.Autopilot.Pathfinding;
using Rynchodon.Settings;
using Rynchodon.Update;
using Rynchodon.Utility;
using Rynchodon.Utility.Network;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens.Terminal.Controls;
using Sandbox.ModAPI;
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

		public class StaticVariables
		{
			public readonly MyTerminalControlCheckbox<MyShipController> autopilotControl;
			public readonly MyTerminalControlTextbox<MyShipController> autopilotCommands;

			public readonly ValueSync<ShipAutopilot.State, AutopilotTerminal> autopilotStatus;
			public readonly ValueSync<AutopilotFlags, AutopilotTerminal> autopilotFlags;
			public readonly ValueSync<Pathfinder.State, AutopilotTerminal> pathfinderState;
			public readonly ValueSync<GridFinder.ReasonCannotTarget, AutopilotTerminal> reasonCannotTarget;
			public readonly ValueSync<InfoString.StringId, AutopilotTerminal> complaint;
			public readonly ValueSync<InfoString.StringId_Jump, AutopilotTerminal> jumpComplaint;
			public readonly ValueSync<long, AutopilotTerminal> blockedBy, enemyFinderBestTarget, distance;
			public readonly ValueSync<int, AutopilotTerminal> welderUnfinishedBlocks;
			public readonly ValueSync<string, AutopilotTerminal> prevNavMover, prevNavRotator;
			public readonly StringBuilderSync<AutopilotTerminal> prevNavMoverInfo, prevNavRotatorInfo;
			public readonly ValueSync<DateTime, AutopilotTerminal> waitUntil;

			public StaticVariables()
			{
				Logger.DebugLog("entered", Logger.severity.TRACE);
				TerminalControlHelper.EnsureTerminalControlCreated<MyCockpit>();
				TerminalControlHelper.EnsureTerminalControlCreated<MyRemoteControl>();

				AddControl(new MyTerminalControlSeparator<MyShipController>() { Enabled = ShipAutopilot.IsAutopilotBlock, Visible = ShipAutopilot.IsAutopilotBlock });

				autopilotControl = new MyTerminalControlCheckbox<MyShipController>("ArmsAp_OnOff", MyStringId.GetOrCompute("ARMS Autopilot"), MyStringId.GetOrCompute("Enable ARMS Autopilot"));
				new ValueSync<bool, AutopilotTerminal>(autopilotControl, "value_autopilotControl");
				AddControl(autopilotControl);
				AddAction(new MyTerminalAction<MyShipController>("ArmsAp_OnOff", new StringBuilder("ARMS Autopilot On/Off"), @"Textures\GUI\Icons\Actions\Toggle.dds") { Action = ToggleAutopilotControl });
				AddAction(new MyTerminalAction<MyShipController>("ArmsAp_On", new StringBuilder("ARMS Autopilot On"), @"Textures\GUI\Icons\Actions\SwitchOn.dds") { Action = block => SetAutopilotControl(block, true) });
				AddAction(new MyTerminalAction<MyShipController>("ArmsAp_Off", new StringBuilder("ARMS Autopilot Off"), @"Textures\GUI\Icons\Actions\SwitchOff.dds") { Action = block => SetAutopilotControl(block, false) });

				autopilotCommands = new MyTerminalControlTextbox<MyShipController>("ArmsAp_Commands", MyStringId.GetOrCompute("Autopilot Commands"), MyStringId.NullOrEmpty);
				new StringBuilderSync<AutopilotTerminal>(autopilotCommands, (autopilot) => autopilot.value_autopilotCommands, (autopilot, value) => {
					autopilot.value_autopilotCommands = value;
					AutopilotCommands.GetOrCreate(autopilot.m_block)?.OnCommandsChanged();
				});
				AddControl(autopilotCommands);

				MyTerminalControlButton<MyShipController> gooeyProgram = new MyTerminalControlButton<MyShipController>("ArmsAp_GuiProgram", MyStringId.GetOrCompute("Program Autopilot"), MyStringId.GetOrCompute("Interactive programming for autopilot"), GooeyProgram);
				gooeyProgram.Enabled = ShipAutopilot.IsAutopilotBlock;
				AddControl(gooeyProgram);

				AddPropertyAndSync<ShipAutopilot.State, Enum>("ArmsAp_Status", out autopilotStatus, "value_autopilotStatus");

				FieldInfo value_autopilotFlags = typeof(AutopilotTerminal).GetField("value_autopilotFlags", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				autopilotFlags = new ValueSync<AutopilotFlags, AutopilotTerminal>("ArmsAp_AutopilotFlags", GenerateGetter<AutopilotFlags>(value_autopilotFlags), GenerateSetter<AutopilotFlags>(value_autopilotFlags), false);
				foreach (AutopilotFlags flag in Enum.GetValues(typeof(AutopilotFlags)))
					if (flag != 0)
						AddPropertyAndSync(flag);

				AddPropertyAndSync<Pathfinder.State, Enum>("ArmsAp_PathStatus", out pathfinderState, "value_pathfinderState");
				AddPropertyAndSync<GridFinder.ReasonCannotTarget, Enum>("ArmsAp_ReasonCannotTarget", out reasonCannotTarget, "value_reasonCannotTarget");
				AddPropertyAndSync<InfoString.StringId, Enum>("ArmsAp_Complaint", out complaint, "value_complaint");
				AddPropertyAndSync<InfoString.StringId_Jump, Enum>("ArmsAp_JumpComplaint", out jumpComplaint, "value_jumpComplaint");
				AddPropertyAndSync("ArmsAp_WaitUntil", out waitUntil, "value_waitUntil");
				AddPropertyAndSyncEntityId("ArmsAp_BlockedBy", out blockedBy, "value_blockedBy");

				FieldInfo value_distance = typeof(AutopilotTerminal).GetField("value_distance", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				distance = new ValueSync<long, AutopilotTerminal>("ArmsAp_Distance", GenerateGetter<long>(value_distance), GenerateSetter<long>(value_distance), false);
				MyTerminalControlProperty<MyShipController, float> linearDistance = new MyTerminalControlProperty<MyShipController, float>("ArmsAp_LinearDistance") { Getter = GetLinearDistance };
				AddControl(linearDistance, false);
				MyTerminalControlProperty<MyShipController, float> angularDistance = new MyTerminalControlProperty<MyShipController, float>("ArmsAp_AngularDistance") { Getter = GetAngularDistance };
				AddControl(angularDistance, false);

				AddPropertyAndSyncEntityId("ArmsAp_EnemyFinderBestTarget", out enemyFinderBestTarget, "value_enemyFinderBestTarget");
				AddPropertyAndSync("ArmsAp_WelderUnfinishedBlocks", out welderUnfinishedBlocks, "value_welderUnfinishedBlocks");
				AddPropertyAndSync("ArmsAp_NavigatorMover", out prevNavMover, "value_prevNavMover");
				AddPropertyAndSync("ArmsAp_NavigatorRotator", out prevNavRotator, "value_prevNavRotator");
				AddPropertyAndSync("ArmsAp_NavigatorMoverInfo", out prevNavMoverInfo, "value_prevNavMoverInfo");
				AddPropertyAndSync("ArmsAp_NavigatorRotatorInfo", out prevNavRotatorInfo, "value_prevNavRotatorInfo");
			}
		}

		public static StaticVariables Static { get; private set; }

		[OnWorldLoad]
		private static void Load()
		{
			Static = new StaticVariables();
		}

		[OnWorldClose]
		private static void Unload()
		{
			Static = null;
		}

		private static void AddControl(MyTerminalControl<MyShipController> control, bool visible = true)
		{
			control.Enabled = ShipAutopilot.IsAutopilotBlock;
			if (visible)
				control.Visible = ShipAutopilot.IsAutopilotBlock;
			else
				control.Visible = False;
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

		private static AValueSync<T, AutopilotTerminal>.GetterDelegate GenerateGetter<T>(FieldInfo field)
		{
			DynamicMethod getter = new DynamicMethod(field.DeclaringType.Name + ".get_" + field.Name, field.FieldType, new Type[] { typeof(AutopilotTerminal) }, true);
			ILGenerator il = getter.GetILGenerator();
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldfld, field);
			il.Emit(OpCodes.Ret);
			return (AValueSync<T, AutopilotTerminal>.GetterDelegate)getter.CreateDelegate(typeof(AValueSync<T, AutopilotTerminal>.GetterDelegate));
		}

		private static AValueSync<T, AutopilotTerminal>.SetterDelegate GenerateSetter<T>(FieldInfo field)
		{
			FieldInfo m_block = typeof(AutopilotTerminal).GetField("m_block", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (m_block == null)
				throw new NullReferenceException("m_block");
			MethodInfo UpdateCustomInfo = typeof(IMyTerminalBlockExtensions).GetMethod("UpdateCustomInfo");
			if (UpdateCustomInfo == null)
				throw new NullReferenceException("UpdateCustomInfo");

			DynamicMethod setter = new DynamicMethod(field.DeclaringType.Name + ".set_" + field.Name, null, new Type[] { typeof(AutopilotTerminal), typeof(T) }, true);
			ILGenerator il = setter.GetILGenerator();
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldarg_1);
			il.Emit(OpCodes.Stfld, field);
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldfld, m_block);
			il.Emit(OpCodes.Call, UpdateCustomInfo);
			il.Emit(OpCodes.Ret);
			return (AValueSync<T, AutopilotTerminal>.SetterDelegate)setter.CreateDelegate(typeof(AValueSync<T, AutopilotTerminal>.SetterDelegate));
		}

		private static void AddPropertyAndSync(AutopilotFlags flag)
		{
			MyTerminalControlProperty<MyShipController, bool> property = new MyTerminalControlProperty<MyShipController, bool>("ArmsAp_" + flag);

			property.Getter = (block) => {
				AutopilotTerminal autopilot;
				if (!Registrar.TryGetValue(block, out autopilot))
				{
					if (!Globals.WorldClosed)
						Logger.AlwaysLog("failed lookup of block: " + block.getBestName(), Logger.severity.WARNING);
					return default(bool);
				}
				return (autopilot.value_autopilotFlags & flag) != 0;
			};

			AddControl(property, false);
		}

		private static void AddPropertyAndSync<T>(string id, out ValueSync<T, AutopilotTerminal> sync, string fieldName)
		{
			MyTerminalControlProperty<MyShipController, T> property = new MyTerminalControlProperty<MyShipController, T>(id);

			FieldInfo field = typeof(AutopilotTerminal).GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			sync = new ValueSync<T, AutopilotTerminal>(id, GenerateGetter<T>(field), GenerateSetter<T>(field), false);
			ValueSync<T, AutopilotTerminal> syncRef = sync;

			property.Getter = syncRef.GetValue;

			AddControl(property, false);
		}

		private static void AddPropertyAndSync(string id, out StringBuilderSync<AutopilotTerminal> sync, string fieldName)
		{
			MyTerminalControlProperty<MyShipController, StringBuilder> property = new MyTerminalControlProperty<MyShipController, StringBuilder>(id);

			FieldInfo field = typeof(AutopilotTerminal).GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			sync = new StringBuilderSync<AutopilotTerminal>(id, GenerateGetter<StringBuilder>(field), GenerateSetter<StringBuilder>(field), false);
			StringBuilderSync<AutopilotTerminal> syncRef = sync;

			property.Getter = syncRef.GetValue;

			AddControl(property, false);
		}

		private static void AddPropertyAndSync<TSync, TYield>(string id, out ValueSync<TSync, AutopilotTerminal> sync, string fieldName)
			where TSync : TYield
		{
			MyTerminalControlProperty<MyShipController, TYield> property = new MyTerminalControlProperty<MyShipController, TYield>(id);

			FieldInfo field = typeof(AutopilotTerminal).GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			sync = new ValueSync<TSync, AutopilotTerminal>(id, GenerateGetter<TSync>(field), GenerateSetter<TSync>(field), false);
			ValueSync<TSync, AutopilotTerminal> syncRef = sync;

			property.Getter = (block) => syncRef.GetValue(block);

			AddControl(property, false);
		}

		private static void AddPropertyAndSyncEntityId(string id, out ValueSync<long, AutopilotTerminal> sync, string fieldName)
		{
			MyTerminalControlProperty<MyShipController, string> property = new MyTerminalControlProperty<MyShipController, string>(id);

			FieldInfo field = typeof(AutopilotTerminal).GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			sync = new ValueSync<long, AutopilotTerminal>(id, GenerateGetter<long>(field), GenerateSetter<long>(field), false);
			ValueSync<long, AutopilotTerminal> syncRef = sync;

			property.Getter = (block) => {
				AutopilotTerminal autopilot;
				if (!Registrar.TryGetValue(block, out autopilot))
				{
					if (!Globals.WorldClosed)
						Logger.AlwaysLog("failed lookup of block: " + block.getBestName(), Logger.severity.WARNING);
					return default(string);
				}

				long entityId = syncRef.GetValue(autopilot);
				IMyEntity entity;
				if (!MyAPIGateway.Entities.TryGetEntityById(entityId, out entity))
				{
					Logger.DebugLog("Failed to get entity for " + entityId, Logger.severity.WARNING);
					return "Unknown Entity";
				}
				return entity.GetNameForDisplay(autopilot.m_block.OwnerId);
			};

			AddControl(property, false);
		}

		private static bool False(IMyCubeBlock block)
		{
			return false;
		}

		private static float GetLinearDistance(IMyTerminalBlock block)
		{
			AutopilotTerminal autopilot;
			if (!Registrar.TryGetValue(block, out autopilot))
			{
				Logger.AlwaysLog("Failed lookup of block: " + block.getBestName(), Logger.severity.WARNING);
				return 0f;
			}

			return autopilot.LinearDistance;
		}

		private static float GetAngularDistance(IMyTerminalBlock block)
		{
			AutopilotTerminal autopilot;
			if (!Registrar.TryGetValue(block, out autopilot))
			{
				Logger.AlwaysLog("Failed lookup of block: " + block.getBestName(), Logger.severity.WARNING);
				return 0f;
			}

			return autopilot.AngularDistance;
		}

		private static void SetAutopilotControl(IMyTerminalBlock block, bool value)
		{
			AutopilotTerminal autopilot;
			if (!Registrar.TryGetValue(block, out autopilot))
			{
				Logger.AlwaysLog("Failed lookup of block: " + block.getBestName(), Logger.severity.WARNING);
				return;
			}

			autopilot.AutopilotControlSwitch = value;
		}

		private static void ToggleAutopilotControl(IMyTerminalBlock block)
		{
			AutopilotTerminal autopilot;
			if (!Registrar.TryGetValue(block, out autopilot))
			{
				Logger.AlwaysLog("Failed lookup of block: " + block.getBestName(), Logger.severity.WARNING);
				return;
			}

			autopilot.AutopilotControlSwitch = !autopilot.AutopilotControlSwitch;
		}

		public static StringBuilder GetAutopilotCommands(IMyTerminalBlock block)
		{
			AutopilotTerminal autopilot;
			if (!Registrar.TryGetValue(block, out autopilot))
			{
				Logger.AlwaysLog("Failed lookup of block: " + block.getBestName(), Logger.severity.WARNING);
				return new StringBuilder();
			}

			return autopilot.AutopilotCommandsText;
		}

		public static void SetAutopilotCommands(IMyTerminalBlock block, StringBuilder value)
		{
			AutopilotTerminal autopilot;
			if (!Registrar.TryGetValue(block, out autopilot))
			{
				Logger.AlwaysLog("Failed lookup of block: " + block.getBestName(), Logger.severity.WARNING);
				return;
			}

			autopilot.AutopilotCommandsText = value;
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
			{
				Logger.DebugLog("Failed to get entity for " + entityId, Logger.severity.WARNING);
				return "Unknown Entity";
			}
			return entity.GetNameForDisplay(autopilot.m_block.OwnerId);
		}

		public readonly IMyTerminalBlock m_block;

		private bool value_waitUpdate;
		private bool WaitingNeedsUpdate
		{
			get { return value_waitUpdate; }
			set
			{
				if (value == value_waitUpdate)
					return;
				value_waitUpdate = value;
				if (value_waitUpdate)
					UpdateManager.Register(10u, RefreshWhileWaiting);
				else
					UpdateManager.Unregister(10u, RefreshWhileWaiting);
			}
		}

		private Sandbox.Game.EntityComponents.MyResourceSinkComponent ResourceSink { get { return ((MyCubeBlock)m_block).ResourceSink; } }

		private Logable Log { get { return new Logable(m_block); } }


		#region Terminal Controls
#pragma warning disable CS0649

		private bool value_autopilotControl;
		public bool AutopilotControlSwitch
		{
			get { return value_autopilotControl; }
			set { Static.autopilotControl.SetValue(m_block, value); }
		}

		private StringBuilder value_autopilotCommands;
		public StringBuilder AutopilotCommandsText
		{
			get { return value_autopilotCommands; }
			set { Static.autopilotCommands.SetValue(m_block, value); }
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

		private ShipAutopilot.State value_autopilotStatus;
		public ShipAutopilot.State m_autopilotStatus
		{
			get { return value_autopilotStatus; }
			set { Static.autopilotStatus.SetValue(m_block, value); }
		}

		private AutopilotFlags value_autopilotFlags;
		public AutopilotFlags m_autopilotFlags
		{
			get { return value_autopilotFlags; }
			set { Static.autopilotFlags.SetValue(m_block, value); }
		}

		private Pathfinder.State value_pathfinderState;
		public Pathfinder.State m_pathfinderState
		{
			get { return value_pathfinderState; }
			set { Static.pathfinderState.SetValue(m_block, value); }
		}

		private GridFinder.ReasonCannotTarget value_reasonCannotTarget;
		public GridFinder.ReasonCannotTarget m_reasonCannotTarget
		{
			get { return value_reasonCannotTarget; }
			set { Static.reasonCannotTarget.SetValue(m_block, value); }
		}

		private InfoString.StringId value_complaint;
		public InfoString.StringId m_complaint
		{
			get { return value_complaint; }
			set { Static.complaint.SetValue(m_block, value); }
		}

		private InfoString.StringId_Jump value_jumpComplaint;
		public InfoString.StringId_Jump m_jumpComplaint
		{
			get { return value_jumpComplaint; }
			set { Static.jumpComplaint.SetValue(m_block, value); }
		}

		private long value_blockedBy;
		public long m_blockedBy
		{
			get { return value_blockedBy; }
			set { Static.blockedBy.SetValue(m_block, value); }
		}

		private long value_enemyFinderBestTarget;
		public long m_enemyFinderBestTarget
		{
			get { return value_enemyFinderBestTarget; }
			set { Static.enemyFinderBestTarget.SetValue(m_block, value); }
		}

		private int value_welderUnfinishedBlocks;
		public int m_welderUnfinishedBlocks
		{
			get { return value_welderUnfinishedBlocks; }
			set { Static.welderUnfinishedBlocks.SetValue(m_block, value); }
		}

		private string value_prevNavMover;
		public string m_prevNavMover
		{
			get { return value_prevNavMover; }
			set { Static.prevNavMover.SetValue(m_block, value); }
		}

		private string value_prevNavRotator;
		public string m_prevNavRotator
		{
			get { return value_prevNavRotator; }
			set { Static.prevNavRotator.SetValue(m_block, value); }
		}

		private StringBuilder value_prevNavMoverInfo;
		public StringBuilder m_prevNavMoverInfo
		{
			get { return value_prevNavMoverInfo; }
			set { Static.prevNavMoverInfo.SetValue(m_block, value); }
		}

		private StringBuilder value_prevNavRotatorInfo;
		public StringBuilder m_prevNavRotatorInfo
		{
			get { return value_prevNavRotatorInfo; }
			set { Static.prevNavRotatorInfo.SetValue(m_block, value); }
		}

		private DateTime value_waitUntil;
		private DateTime m_waitUntil
		{
			get { return value_waitUntil; }
			set { Static.waitUntil.SetValue(m_block, value); }
		}

		private long value_distance;
		private long m_distance
		{
			get { return value_distance; }
			set { Static.distance.SetValue(m_block, value); }
		}

		public float LinearDistance { get { return new DistanceValues() { PackedValue = m_distance }.LinearDistance; } }

		public float AngularDistance { get { return new DistanceValues() { PackedValue = m_distance }.AngularDistance; } }

		public void SetDistance(float linear, float angular)
		{
			DistanceValues dv = new DistanceValues() { PackedValue = m_distance };
			if (Math.Abs(linear / dv.LinearDistance - 1f) > 0.01f || Math.Abs(angular / dv.AngularDistance - 1f) > 0.01f)
				m_distance = new DistanceValues() { LinearDistance = linear, AngularDistance = angular }.PackedValue;
		}

		public void SetWaitUntil(TimeSpan waitUntil)
		{
			DateTime newValue = DateTime.UtcNow + waitUntil - Globals.ElapsedTime;
			if (Math.Abs((m_waitUntil - newValue).Ticks) > TimeSpan.TicksPerSecond)
				m_waitUntil = newValue;
		}

#pragma warning restore CS0649
		#endregion Terminal Properties

		public AutopilotTerminal(IMyCubeBlock block)
		{
			this.m_block = (IMyTerminalBlock)block;

			m_block.AppendingCustomInfo += AppendingCustomInfo;

			Registrar.Add(block, this);
			Log.DebugLog("Initialized", Logger.severity.INFO);
		}

		public void AppendingCustomInfo(IMyTerminalBlock block, StringBuilder customInfo)
		{
			ResourceSink.SetRequiredInputByType(Globals.Electricity, m_autopilotStatus == ShipAutopilot.State.Enabled ? m_waitUntil < DateTime.UtcNow ? 0.1f : 0.01f : 0.001f);
			AppendingCustomInfo(customInfo);
		}

		public void AppendingCustomInfo(StringBuilder customInfo)
		{
			AppendMain(customInfo);

			// power
			customInfo.Append("Current Input: ");
			customInfo.Append(PrettySI.makePretty(ResourceSink.RequiredInputByType(Globals.Electricity) * 1e6f));
			customInfo.AppendLine("W");
		}

		private void AppendMain(StringBuilder customInfo)
		{
			if (m_autopilotStatus == ShipAutopilot.State.Halted)
			{
				if (MyAPIGateway.Multiplayer.IsServer)
					customInfo.AppendLine("Autopilot crashed, please upload log files and report on steam page");
				else
					customInfo.AppendLine("Autopilot crashed, please upload server's log files and report on steam page");
				return;
			}
			if (m_pathfinderState == Pathfinder.State.Crashed)
			{
				if (MyAPIGateway.Multiplayer.IsServer)
					customInfo.AppendLine("Pathfinder crashed, please upload log files and report on steam page");
				else
					customInfo.AppendLine("Pathfinder crashed, please upload server's log files and report on steam page");
				return;
			}

			if (!HasFlag(AutopilotFlags.HasControl))
			{
				if (m_autopilotStatus == ShipAutopilot.State.Disabled)
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
				return;
			}

			if (m_waitUntil > DateTime.UtcNow)
			{
				WaitingNeedsUpdate = true;
				customInfo.Append("Waiting for ");
				customInfo.AppendLine(PrettySI.makePretty(m_waitUntil - DateTime.UtcNow));
				return;
			}
			else
				WaitingNeedsUpdate = false;

			IMyPlayer controlling = MyAPIGateway.Players.GetPlayerControllingEntity(m_block.CubeGrid);
			if (controlling != null)
			{
				customInfo.Append("Player controlling: ");
				customInfo.AppendLine(controlling.DisplayName);
				AppendingCustomInfo_EnemyFinder(customInfo);
				return;
			}

			// pathfinder
			switch (m_pathfinderState)
			{
				case Pathfinder.State.SearchingForPath:
					customInfo.Append("Searching for path around ");
					customInfo.AppendLine(GetNameForDisplay(this, m_blockedBy));
					break;
				case Pathfinder.State.FollowingPath:
					customInfo.Append("Following path around ");
					customInfo.AppendLine(GetNameForDisplay(this, m_blockedBy));
					break;
				case Pathfinder.State.FailedToFindPath:
					customInfo.Append("No path around ");
					customInfo.AppendLine(GetNameForDisplay(this, m_blockedBy));
					break;
				default:
					if (HasFlag(AutopilotFlags.RotationBlocked))
					{
						customInfo.Append("Rotation blocked by ");
						customInfo.AppendLine(GetNameForDisplay(this, m_blockedBy));
					}
					break;
			}

			// nav mover info
			if (HasFlag(AutopilotFlags.HasNavigatorMover))
			{
				float linear = LinearDistance;
				if (linear.ValidNonZero())
				{
					customInfo.Append(m_prevNavMoverInfo);
					customInfo.Append("Distance: ");
					customInfo.Append(PrettySI.makePretty(LinearDistance));
					customInfo.AppendLine("m");
				}
			}

			// nav rotator info
			if (HasFlag(AutopilotFlags.HasNavigatorRotator))
			{
				float angular = AngularDistance;
				if (angular.ValidNonZero())
				{
					customInfo.Append(m_prevNavRotatorInfo);
					customInfo.Append("Angle: ");
					customInfo.Append(PrettySI.toSigFigs(AngularDistance));
					customInfo.AppendLine(" rad");
				}
			}

			// enemy finder
			AppendingCustomInfo_EnemyFinder(customInfo);

			// complaint
			InfoString.StringId ids = m_complaint;
			if (ids != InfoString.StringId.None)
				foreach (InfoString.StringId flag in InfoString.AllStringIds())
					if ((ids & flag) != 0)
						customInfo.AppendLine(InfoString.GetString(flag));
			InfoString.StringId_Jump jc = m_jumpComplaint;
			if (jc != InfoString.StringId_Jump.None)
				customInfo.AppendLine(InfoString.GetString(jc));

			return;
		}

		private void AppendingCustomInfo_EnemyFinder(StringBuilder customInfo)
		{
			if (HasFlag(AutopilotFlags.EnemyFinderIssue))
			{
				switch (m_reasonCannotTarget)
				{
					case GridFinder.ReasonCannotTarget.None:
						customInfo.AppendLine("No enemy detected");
						break;
					case GridFinder.ReasonCannotTarget.Too_Far:
						customInfo.Append(GetNameForDisplay(this, m_enemyFinderBestTarget));
						customInfo.AppendLine(" is too far");
						break;
					case GridFinder.ReasonCannotTarget.Too_Fast:
						customInfo.Append(GetNameForDisplay(this, m_enemyFinderBestTarget));
						customInfo.AppendLine(" is too fast");
						break;
					case GridFinder.ReasonCannotTarget.Grid_Condition:
						customInfo.Append(GetNameForDisplay(this, m_enemyFinderBestTarget));
						customInfo.AppendLine(" cannot be targeted");
						break;
				}
			}
		}

		private bool HasFlag(AutopilotFlags flag)
		{
			return (m_autopilotFlags & flag) != 0;
		}

		private void RefreshWhileWaiting()
		{
			if (m_block.Closed)
			{
				WaitingNeedsUpdate = false;
				return;
			}
			m_block.UpdateCustomInfo();
		}

	}

}
