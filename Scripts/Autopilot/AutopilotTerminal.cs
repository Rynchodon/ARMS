using System.Text;
using Rynchodon.Autopilot.Instruction;
using Rynchodon.Settings;
using Rynchodon.Update;
using Rynchodon.Utility.Network;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace Rynchodon.Autopilot
{

	public class AutopilotTerminal
	{

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
			MyAPIGateway.Multiplayer.RegisterMessageHandler(ShipAutopilot.ModId_CustomInfo, MessageHandler);
			Static.s_logger.debugLog("Registerd for messages", Logger.severity.DEBUG);

			AddControl(new MyTerminalControlSeparator<MyShipController>() { Visible = ShipAutopilot.IsAutopilotBlock });

			Static.autopilotControl = new MyTerminalControlCheckbox<MyShipController>("ArmsAutopilot", MyStringId.GetOrCompute("ARMS Autopilot"), MyStringId.NullOrEmpty);
			Static.autopilotControl.Enabled = ShipAutopilot.IsAutopilotBlock;
			Static.autopilotControl.Visible = ShipAutopilot.IsAutopilotBlock;
			Static.autopilotControl.EnableAction();
			IMyTerminalValueControl<bool> valueControl = Static.autopilotControl;
			valueControl.Getter = GetAutopilotControl;
			valueControl.Setter = SetAutopilotControl;
			AddControl(Static.autopilotControl);

			Static.autopilotCommands = new MyTerminalControlTextbox<MyShipController>("AutopilotCommands", MyStringId.GetOrCompute("Autopilot Commands"), MyStringId.NullOrEmpty);
			Static.autopilotCommands.Enabled = ShipAutopilot.IsAutopilotBlock;
			Static.autopilotCommands.Visible = ShipAutopilot.IsAutopilotBlock;
			Static.autopilotCommands.Getter = GetAutopilotCommands;
			Static.autopilotCommands.Setter = SetAutopilotCommands;
			AddControl(Static.autopilotCommands);

			MyTerminalControlButton<MyShipController> gooeyProgram = new MyTerminalControlButton<MyShipController>("GooeyProgram", MyStringId.GetOrCompute("Program Autopilot"), MyStringId.GetOrCompute("Interactive programming for autopilot"), GooeyProgram);
			gooeyProgram.Enabled = ShipAutopilot.IsAutopilotBlock;
			AddControl(gooeyProgram);
		}

		private static void AddControl(MyTerminalControl<MyShipController> control)
		{
			MyTerminalControlFactory.AddControl<MyShipController, MyCockpit>(control);
			if (ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bUseRemoteControl))
				MyTerminalControlFactory.AddControl<MyShipController, MyRemoteControl>(control);
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			MyAPIGateway.Multiplayer.UnregisterMessageHandler(ShipAutopilot.ModId_CustomInfo, MessageHandler);
			Static.s_logger.debugLog("Unregisterd for messages", Logger.severity.DEBUG);
			Static = null;
		}

		private static void MessageHandler(byte[] message)
		{
			//s_logger.debugLog("Received a message, length: " + message.Length, "MessageHandler()", Logger.severity.DEBUG);

			int pos = 0;
			long entityId = ByteConverter.GetLong(message, ref pos);
			AutopilotTerminal recipient;
			if (!Registrar.TryGetValue(entityId, out recipient))
			{
				if (Static != null)
					Static.s_logger.debugLog("Recipient block is closed: " + entityId, Logger.severity.INFO);
				return;
			}
			recipient.MessageHandler(message, ref pos);
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

		public static StringBuilder GetAutopilotCommands(IMyTerminalBlock block)
		{
			AutopilotTerminal autopilot;
			if (!Registrar.TryGetValue(block, out autopilot))
			{
				if (Static != null)
					Static.s_logger.alwaysLog("Failed lookup of block: " + block.getBestName(), Logger.severity.WARNING);
				return new StringBuilder(); ;
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
			if (cmds == null)
				return;
			cmds.StartGooeyProgramming(null);
		}

		private readonly Logger m_logger;
		private readonly IMyTerminalBlock m_block;

		private StringBuilder m_customInfo = new StringBuilder();
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

		public AutopilotTerminal(IMyCubeBlock block)
		{
			this.m_logger = new Logger("AutopilotTerminal", block);
			this.m_block = block as IMyTerminalBlock;

			byte index = 0;
			this.m_autopilotControl = new EntityValue<bool>(block, index++, Static.autopilotControl.UpdateVisual, Saver.Instance.LoadOldVersion(69) && ((MyShipController)block).ControlThrusters);
			this.m_autopilotCommands = new EntityStringBuilder(block, index++, Static.autopilotCommands.UpdateVisual);

			m_block.AppendingCustomInfo += AppendingCustomInfo;

			Registrar.Add(block, this);
			m_logger.debugLog("Initialized", Logger.severity.INFO);
		}

		private void MessageHandler(byte[] bytes, ref int pos)
		{
			//m_logger.debugLog("Received a message, length: " + message.Length, "MessageHandler()", Logger.severity.DEBUG);

			m_customInfo.Clear();
			m_customInfo.Append(ByteConverter.GetString(bytes, ref pos));

			//m_logger.debugLog("Message:\n" + m_customInfo, "MessageHandler()");

			m_block.RefreshCustomInfo();
		}

		private void AppendingCustomInfo(IMyTerminalBlock arg1, StringBuilder arg2)
		{
			arg2.Append(m_customInfo);
		}

	}

}
