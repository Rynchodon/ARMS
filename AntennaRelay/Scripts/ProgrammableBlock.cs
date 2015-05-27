#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using System.Linq;
//using System.Text;

using Sandbox.Common;
using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRage;

namespace Rynchodon.AntennaRelay
{
	/// <summary>
	/// Keeps track of transmissions for a programmable block. A programmable block cannot relay, so it should only receive messages for iteself.
	/// When name changes, creates and sends a message.
	/// </summary>
	public class ProgrammableBlock : Receiver
	{
		internal static Dictionary<IMyCubeBlock, ProgrammableBlock> registry = new Dictionary<IMyCubeBlock, ProgrammableBlock>();

		private Ingame.IMyProgrammableBlock myProgBlock;
		private Logger myLogger;

		public ProgrammableBlock(IMyCubeBlock block)
			: base(block)
		{
			myLogger = new Logger("Programmable block", () => CubeBlock.CubeGrid.DisplayName);
			myProgBlock = CubeBlock as Ingame.IMyProgrammableBlock;
			registry.Add(CubeBlock, this);
		}

		protected override void Close(IMyEntity entity)
		{
			try
			{
				if (CubeBlock != null && registry.ContainsKey(CubeBlock))
					registry.Remove(CubeBlock);
			}
			catch (Exception e)
			{ myLogger.alwaysLog("exception on removing from registry: " + e, "Close()", Logger.severity.WARNING); }
			CubeBlock = null;
			myProgBlock = null;
		}

		/// <summary>
		/// Uses MessageParser to grab messages, then sends them out. Not registered for event because it fires too frequently.
		/// </summary>
		/// Not actually subscribed to an event
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
				Receiver.sendToAttached(CubeBlock, toSend);
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
			if (myMessages.Count == 0)
				return;

			IMyTerminalBlock asTerm = CubeBlock as IMyTerminalBlock;
			if (MessageParser.canWriteTo(asTerm))
			{
				Message toWrite = myMessages.First();
				myMessages.RemoveFirst();
				MessageParser.writeToName(asTerm, toWrite);
			}
			asTerm.GetActionWithName("Run").Apply(CubeBlock);
		}

		public static bool TryGet(IMyCubeBlock block, out ProgrammableBlock result)
		{ return registry.TryGetValue(block, out result); }
	}
}
