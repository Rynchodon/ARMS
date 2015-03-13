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
	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_MyProgrammableBlock))]
	public class ProgrammableBlock : Receiver
	{
		internal static Dictionary<IMyCubeBlock, ProgrammableBlock> registry = new Dictionary<IMyCubeBlock, ProgrammableBlock>();

		private Ingame.IMyProgrammableBlock myProgBlock;

		protected override void DelayedInit()
		{
			base.DelayedInit();
			myProgBlock = CubeBlock as Ingame.IMyProgrammableBlock;
			EnforcedUpdate = MyEntityUpdateEnum.EACH_100TH_FRAME;
		}

		public override void Close()
		{
			try
			{
				if (CubeBlock != null && registry.ContainsKey(CubeBlock))
					registry.Remove(CubeBlock);
			}
			catch (Exception e)
			{ alwaysLog("exception on removing from registry: " + e, "Close()", Logger.severity.WARNING); }
			CubeBlock = null;
			myProgBlock = null;
			MyObjectBuilder = null;
		}

		private void myProgBlock_CustomNameChanged()
		{
			if (Closed)
				return;
			try
			{
				log("name changed to: " + myProgBlock.DisplayNameText, "ProgBlock_CustomNameChanged()", Logger.severity.TRACE);
				List<Message> toSend = MessageParser.getFromName(myProgBlock as IMyTerminalBlock); // get messages from name
				//Message toSend = MessageParser.getFromName(myProgBlock as IMyTerminalBlock); // get message from name
				if (toSend == null)
				{
					log("could not get message from parser", "ProgBlock_CustomNameChanged()", Logger.severity.TRACE);
					return;
				}
				foreach (Message mes in toSend)
				{
					log("testing "+Antenna.registry.Count+" antenna", "myProgBlock_CustomNameChanged()", Logger.severity.TRACE);
					foreach (Antenna ant in Antenna.registry)
						if (AttachedGrids.isGridAttached(CubeBlock.CubeGrid, ant.CubeBlock.CubeGrid))
						{
							log("sending message to attached antenna", "myProgBlock_CustomNameChanged()", Logger.severity.TRACE);
							ant.receive(mes);
						}
						else
							log("antenna not attached: " + ant.CubeBlock.CubeGrid.DisplayName + " - " + ant.CubeBlock.DisplayNameText, "myProgBlock_CustomNameChanged()", Logger.severity.TRACE);
				}
				log("finished sending message", "myProgBlock_CustomNameChanged()", Logger.severity.TRACE);
			}
			catch (Exception e)
			{
				alwaysLog("Exception: " + e, "ProgBlock_CustomNameChanged()", Logger.severity.ERROR);
			}
		}

		/// <summary>
		/// does not check mes for received, or correct destination. Does set mes.isValid = false
		/// </summary>
		/// <param name="mes"></param>
		public override void receive(Message mes)
		{
			mes.isValid = false;
			if (myMessages.Contains(mes))
				return;
			myMessages.Enqueue(mes);
			log("got a new message: " + mes.Content + ", count is now " + myMessages.Count, "receive()", Logger.severity.TRACE);
		}

		private string previousName;

		public override void UpdateAfterSimulation100()
		{
			if (!IsInitialized || Closed)
				return;

			// check name for message from ingame
			if (myProgBlock.DisplayNameText != previousName)
			{
				myProgBlock_CustomNameChanged();
				previousName = myProgBlock.DisplayNameText;
			}

			// handle received message
			if (myMessages.Count == 0)
				return;
			if (MessageParser.canWriteTo(myProgBlock as IMyTerminalBlock))
			{
				Message toWrite = myMessages.Dequeue();

			}
		}

		public static bool TryGet(IMyCubeBlock block, out ProgrammableBlock result)
		{ return registry.TryGetValue(block, out result); }


		public override string ToString()
		{ return CubeBlock.CubeGrid.DisplayName + "-" + myProgBlock.DisplayNameText; }

		private Logger myLogger;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ alwaysLog(toLog, method, level); }
		protected override void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null)
				myLogger = new Logger(CubeBlock.CubeGrid.DisplayName, "ProgrammableBlock");
			myLogger.log(level, method, toLog, CubeBlock.DisplayNameText);
		}
	}
}
