#define LOG_ENABLED // remove on build

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Sandbox.Common;
using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Engine;
using Sandbox.Game;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRageMath;

namespace Rynchodon.BlockCommunication
{
	/// <summary>
	/// keeps track of programmable blocks. when name changes creates and sends a Message
	/// </summary>
	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_MyProgrammableBlock))]
	public class ProgBlockMonitor : MyGameLogicComponent
	{
		//private static List<ProgBlockMonitor> allMonitors = new List<ProgBlockMonitor>();
		private static Dictionary<IMyCubeBlock, ProgBlockMonitor> registry = new Dictionary<IMyCubeBlock, ProgBlockMonitor>();

		public static bool looseContains(string bigString, string subStr)
		{
			bigString = bigString.ToUpper().Replace(" ", "");
			subStr = subStr.ToUpper().Replace(" ", "");
			return bigString.Contains(subStr);
		}
	
		private IMyCubeBlock ProgBlock;

		public override void Init(MyObjectBuilder_EntityBase objectBuilder)
		{
			builder = objectBuilder;
			//allMonitors.Add(this);
			ProgBlock = Entity as IMyCubeBlock;
			registry.Add(ProgBlock, this);
			(Entity as IMyTerminalBlock).CustomNameChanged += ProgBlock_CustomNameChanged;
			Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
		}

		private bool isClosed = false;
		public override void Close()
		{
			if (isClosed)
				return;
			isClosed = true;
			try
			{ /*allMonitors.Remove(this);*/ registry.Remove(ProgBlock); }
			catch (Exception)
			{ log("could not remove from registry: " + this, "Close()", Logger.severity.WARNING); }
		}

		//private bool ignoreNameChanged = false;
		private void ProgBlock_CustomNameChanged(IMyTerminalBlock tBlock)
		{
			if (isClosed)
				return;
			try
			{
				//ignoreNameChanged = true;
				//log("set ignore", "ProgBlock_CustomNameChanged()", Logger.severity.TRACE);
				Message toSend = MessageParser.getFromName(ProgBlock as IMyTerminalBlock); // get message from name
				if (toSend == null)
				{
					log("could not get message from parser", "ProgBlock_CustomNameChanged()", Logger.severity.TRACE);
					return;
				}
				sendMessage(toSend); // send message
			}
			catch (Exception e)
			{
				alwaysLog("Exception: " + e, "ProgBlock_CustomNameChanged()", Logger.severity.ERROR);
			}
			//log("cleared ignore", "ProgBlock_CustomNameChanged()", Logger.severity.TRACE);
			//ignoreNameChanged = false;
		}

		private void sendMessage(Message inTransit)
		{
			bool messageSent = false;
			//foreach (ProgBlockMonitor monitor in registry.Keys)
			foreach (KeyValuePair<IMyCubeBlock, ProgBlockMonitor> pair in registry)
			{
				if (!looseContains(pair.Key.CubeGrid.DisplayName, inTransit.DestinationGrid))
					continue;
				if (!looseContains(pair.Key.DisplayNameText, inTransit.DestinationBlock))
					continue;

				pair.Value.receivedMessages.Enqueue(inTransit);
				//MessageParser.writeToName(monitor.ProgBlock as IMyTerminalBlock, inTransit);
				messageSent = true;
			}
			if (messageSent)
				log("sent message successfully: " + inTransit.Transmission + ", from " + inTransit.SourceGrid + " : " + inTransit.SourceBlock
					+ ", to " + inTransit.DestinationGrid + " : " + inTransit.DestinationBlock, "sendMessage()", Logger.severity.INFO);
			else
				log("failed to send message: " + inTransit.Transmission + ", from " + inTransit.SourceGrid + " : " + inTransit.SourceBlock
					+ ", to " + inTransit.DestinationGrid + " : " + inTransit.DestinationBlock, "sendMessage()", Logger.severity.INFO);
		}

		private Queue<Message> receivedMessages = new Queue<Message>();

		public override void UpdateAfterSimulation100()
		{
			if (receivedMessages.Count == 0)
				return;
			if (MessageParser.canWriteTo(ProgBlock as IMyTerminalBlock))
			{
				Message toWrite = receivedMessages.Dequeue();
				MessageParser.writeToName(ProgBlock as IMyTerminalBlock, toWrite);
				log("wrote message successfully: " + toWrite.Transmission + ", from " + toWrite.SourceGrid + " : " + toWrite.SourceBlock
					+ ", to " + ProgBlock.CubeGrid.DisplayName + " : " + ProgBlock.DisplayNameText, "UpdateAfterSimulation100()", Logger.severity.INFO);
			}
		}


		public override string ToString()
		{ return "ProgBlockMonitor(" + ProgBlock.DisplayNameText + ")"; }

		private MyObjectBuilder_EntityBase builder;
		public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
		{ return builder; }

		private Logger myLogger;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ alwaysLog(toLog, method, level); }
		private void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null)
				myLogger = new Logger(ProgBlock.CubeGrid.DisplayName, "ProgBlockMonitor");
			myLogger.log(level, method, toLog);
		}
	}
}
