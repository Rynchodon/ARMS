using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.AntennaRelay;
using Rynchodon.Utility.Network;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Game.Components;
using VRage.ObjectBuilders;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.GUI
{
	[MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
	public class Initializer : MySessionComponentBase
	{

		private readonly MyObjectBuilderType textPanelType = typeof(MyObjectBuilder_TextPanel);
		private readonly MyObjectBuilderType programmableType = typeof(MyObjectBuilder_MyProgrammableBlock);
		private readonly Logger m_logger;
		private readonly List<byte> message = new List<byte>();

		public Initializer()
		{
			if (IsLoaded())
				return;

			// Actions & Controls need to be loaded ASAP, which means there can be no logging at this point
			MyTerminalAction<MyFunctionalBlock> textPanel_displayEntities = new MyTerminalAction<MyFunctionalBlock>("DisplayEntities", new StringBuilder("Display Entities"), "Textures\\GUI\\Icons\\Actions\\Start.dds")
			{
				Enabled = block => ((IMyCubeBlock)block).BlockDefinition.TypeId == textPanelType,
				ValidForGroups = false,
				ActionWithParameters = TextPanel_DisplayEntities
			};
			MyTerminalControlFactory.AddAction(textPanel_displayEntities);

			MyTerminalAction<MyFunctionalBlock> programmable_sendMessage = new MyTerminalAction<MyFunctionalBlock>("SendMessage", new StringBuilder("Send Message"), "Textures\\GUI\\Icons\\Actions\\Start.dds")
			{
				Enabled = block => ((IMyCubeBlock)block).BlockDefinition.TypeId == programmableType,
				ValidForGroups = false,
				ActionWithParameters = ProgrammableBlock_SendMessage
			};
			MyTerminalControlFactory.AddAction(programmable_sendMessage);

			m_logger = new Logger("GUI." + GetType().Name);
		}

		private bool IsLoaded()
		{
			List<ITerminalAction> actions = new List<ITerminalAction>();
			MyTerminalControlFactory.GetActions(typeof(IMyFunctionalBlock), actions);

			foreach (var act in actions)
				if (act.Id == "DisplayEntities")
					return true;

			return false;
		}

		/// <param name="args">EntityIds as long</param>
		private void TextPanel_DisplayEntities(MyFunctionalBlock block, ListReader<Ingame.TerminalActionParameter> args)
		{
			ByteConverter.AppendBytes(message, (byte)MessageHandler.SubMod.TP_DisplayEntities);
			ByteConverter.AppendBytes(message, block.EntityId);
			foreach (Ingame.TerminalActionParameter arg in args)
			{
				if (arg.TypeCode != TypeCode.Int64)
				{
					m_logger.debugLog("TerminalActionParameter is of wrong type, expected Int64, got " + arg.TypeCode, "DisplayEntities()", Logger.severity.WARNING);
					return;
				}
				ByteConverter.AppendBytes(message, (long)arg.Value);
			}

			if (MyAPIGateway.Multiplayer.SendMessageToSelf(message.ToArray()))
				m_logger.debugLog("sent message", "DisplayEntities()", Logger.severity.DEBUG);
			else
				m_logger.alwaysLog("failed to send message", "DisplayEntities()", Logger.severity.WARNING);
			message.Clear();
		}

		/// <param name="args">Recipient grid, recipient block, message</param>
		private void ProgrammableBlock_SendMessage(MyFunctionalBlock block, ListReader<Ingame.TerminalActionParameter> args)
		{
			if (args.Count != 3)
			{
				m_logger.debugLog("Wrong number of arguments, expected 3, got " + args.Count, "ProgrammableBlock_SendMessage()", Logger.severity.WARNING);
				if (MyAPIGateway.Session.Player != null)
					block.AppendCustomInfo("Failed to send message:\nWrong number of arguments, expected 3, got " + args.Count + '\n');
				return;
			}

			if (MyAPIGateway.Multiplayer.IsServer)
				ByteConverter.AppendBytes(message, (byte)MessageHandler.SubMod.PB_SendMessage_Server);
			else
				ByteConverter.AppendBytes(message, (byte)MessageHandler.SubMod.PB_SendMessage_Client);
			ByteConverter.AppendBytes(message, block.EntityId);

			for (int i = 0; i < 3; i++)
			{
				if (args[i].TypeCode != TypeCode.String)
				{
					m_logger.debugLog("TerminalActionParameter #" + i + " is of wrong type, expected String, got " + args[i].TypeCode, "DisplayEntities()", Logger.severity.WARNING);
					if (MyAPIGateway.Session.Player != null)
						block.AppendCustomInfo("Failed to send message:\nTerminalActionParameter #" + i + " is of wrong type, expected String, got " + args[i].TypeCode + '\n');
					return;
				}
				ByteConverter.AppendBytes(message, (string)args[i].Value);
				if (i != 2)
					ByteConverter.AppendBytes(message, ProgrammableBlock.messageSeparator);
			}

			byte[] toArray = message.ToArray();

			bool success;

			if (MyAPIGateway.Multiplayer.IsServer)
				success = MyAPIGateway.Multiplayer.SendMessageToSelf(toArray);
			else
			{
				success = MyAPIGateway.Multiplayer.SendMessageToSelf(toArray);
				success = success || MyAPIGateway.Multiplayer.SendMessageToServer(toArray);
			}

			if (success)
				m_logger.debugLog("Sent message", "ProgrammableBlock_SendMessage()", Logger.severity.DEBUG);
			else
			{
				m_logger.alwaysLog("Message too long", "ProgrammableBlock_SendMessage()", Logger.severity.WARNING);
				if (MyAPIGateway.Session.Player != null)
					block.AppendCustomInfo("Failed to send message:\nMessage too long (" + message.Count + " > 4096 bytes)\n");
			}
			message.Clear();
		}

	}
}
