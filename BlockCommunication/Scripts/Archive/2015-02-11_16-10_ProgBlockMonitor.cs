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
	/// keeps track of programmable blocks, when name changes creates a message
	/// </summary>
	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_MyProgrammableBlock))]
	public class ProgBlockMonitor : MyGameLogicComponent
	{
		private static List<ProgBlockMonitor> allMonitors = new List<ProgBlockMonitor>();

		public static bool looseContains(string container, string subStr)
		{
			container = container.Replace(" ", "");
			subStr = subStr.Replace(" ", "");
			return container.Contains(subStr, StringComparison.OrdinalIgnoreCase);
		}
	
		private IMyCubeBlock ProgBlock;

		public override void Init(MyObjectBuilder_EntityBase objectBuilder)
		{
			builder = objectBuilder;
			allMonitors.Add(this);
			ProgBlock = Entity as IMyCubeBlock;
			(Entity as IMyTerminalBlock).CustomNameChanged += ProgBlock_CustomNameChanged;
		}

		private bool isClosed = false;
		public override void Close()
		{
			isClosed = true;
			try
			{ allMonitors.Remove(this); }
			catch (Exception)
			{ log("could not remove from allMonitors: " + this, "Close()", Logger.severity.WARNING); }
		}

		private void ProgBlock_CustomNameChanged(IMyTerminalBlock tBlock)
		{
			if (isClosed)
				return;
			try
			{
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
		}

		private void sendMessage(Message inTransit)
		{
			foreach (ProgBlockMonitor monitor in allMonitors)
			{
				if (!looseContains(monitor.ProgBlock.CubeGrid.DisplayName, inTransit.DestinationGrid))
					continue;
				if (!looseContains(monitor.ProgBlock.DisplayNameText, inTransit.DestinationBlock))
					continue;

				MessageParser.writeToName(monitor.ProgBlock as IMyTerminalBlock, inTransit);
			}
		}


		public override string ToString()
		{ return "ProgBlockMonitor(" + ProgBlock.DisplayNameText + ")"; }

		private MyObjectBuilder_EntityBase builder;
		public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
		{ return builder; }

		private BlockComLogger myLogger;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ alwaysLog(toLog, method, level); }
		private void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null)
				myLogger = new BlockComLogger(ProgBlock.CubeGrid.DisplayName, "ProgBlockMonitor");
			myLogger.log(level, method, toLog);
		}
	}
}
