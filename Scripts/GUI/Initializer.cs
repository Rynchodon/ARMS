using System;
using System.Collections.Generic;
using System.Text;
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
		private readonly Logger m_logger;
		private readonly List<byte> message = new List<byte>();

		public Initializer()
		{
			if (IsLoaded())
				return;

			// Actions & Controls need to be loaded ASAP, which means there can be no logging at this point
			MyTerminalAction<MyTerminalBlock> textPanel_displayEntities = new MyTerminalAction<MyTerminalBlock>("DisplayEntities", new StringBuilder("Display Entities"), "Textures\\GUI\\Icons\\Actions\\Start.dds")
			{
				Enabled = block => ((IMyCubeBlock)block).BlockDefinition.TypeId == textPanelType,
				ValidForGroups = false,
				ActionWithParameters = DisplayEntities
			};
			MyTerminalControlFactory.AddAction(textPanel_displayEntities);

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

		private void DisplayEntities(MyTerminalBlock block, ListReader<Ingame.TerminalActionParameter> args)
		{
			ByteConverter.AppendBytes(message, (byte)MessageHandler.SubMod.DisplayEntities);
			ByteConverter.AppendBytes(message, block.EntityId);
			foreach (Ingame.TerminalActionParameter arg in args)
			{
				if (arg.TypeCode != TypeCode.Int64)
				{
					m_logger.debugLog("TerminalActionParameter is of wrong type: " + arg.TypeCode + ", expected " + TypeCode.Int64, "DisplayEntities()", Logger.severity.WARNING);
					return;
				}
				ByteConverter.AppendBytes(message, (long)arg.Value);
			}

			if (MyAPIGateway.Multiplayer.SendMessageToServer(MessageHandler.ModId, message.ToArray()))
				m_logger.debugLog("sent message", "DisplayEntities()", Logger.severity.DEBUG);
			else
				m_logger.alwaysLog("failed to send message", "DisplayEntities()", Logger.severity.WARNING);
			message.Clear();
		}

	}
}
