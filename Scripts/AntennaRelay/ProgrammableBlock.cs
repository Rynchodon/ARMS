using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.AntennaRelay
{
	/// <summary>
	/// Keeps track of transmissions for a programmable block. A programmable block cannot relay, so it should only receive messages for iteself.
	/// When name changes, creates and sends a message.
	/// </summary>
	public class ProgrammableBlock : ReceiverBlock
	{

		private Ingame.IMyProgrammableBlock myProgBlock;
		private Logger myLogger;

		public ProgrammableBlock(IMyCubeBlock block)
			: base(block)
		{
			myLogger = new Logger("Programmable block", () => CubeBlock.CubeGrid.DisplayName);
			myProgBlock = CubeBlock as Ingame.IMyProgrammableBlock;
			Registrar.Add(CubeBlock, this);
		}

		/// <summary>
		/// Uses MessageParser to grab messages, then sends them out. Not registered for event because it fires too frequently.
		/// </summary>
		private void myProgBlock_CustomNameChanged()
		{
			try
			{
				List<Message> toSend = MessageParser.getFromName(myProgBlock as IMyTerminalBlock); // get messages from name
				if (toSend == null || toSend.Count == 0)
				{
					myLogger.debugLog("could not get message from parser", "ProgBlock_CustomNameChanged()", Logger.severity.TRACE);
					return;
				}
				ReceiverBlock.SendToAttached(CubeBlock, toSend);
				myLogger.debugLog("finished sending message", "myProgBlock_CustomNameChanged()", Logger.severity.TRACE);
			}
			catch (Exception e)
			{
				myLogger.alwaysLog("Exception: " + e, "ProgBlock_CustomNameChanged()", Logger.severity.ERROR);
			}
		}

		private string previousName;

		public void UpdateAfterSimulation100()
		{
			// check name for message from ingame
			if (myProgBlock.DisplayNameText != previousName)
			{
				myProgBlock_CustomNameChanged();
				previousName = myProgBlock.DisplayNameText;
			}

			// handle received message
			if (messageCount == 0)
				return;

			IMyTerminalBlock asTerm = CubeBlock as IMyTerminalBlock;
			if (MessageParser.canWriteTo(asTerm))
			{
				Message toWrite = RemoveOneMessage();
				MessageParser.writeToName(asTerm, toWrite);
			}
			asTerm.GetActionWithName("Run").Apply(CubeBlock);
		}

	}
}
