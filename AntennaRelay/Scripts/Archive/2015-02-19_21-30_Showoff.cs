using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;

namespace Rynchodon.AntennaRelay
{
	public static class Showoff
	{
		private static string getTextPanelName(IMyCubeBlock showoff)
		{
			string displayName = showoff.DisplayNameText;
			int start = displayName.IndexOf('[') + 1;
			int end = displayName.IndexOf(']');
			if (start > 0 && end > start) // has appropriate brackets
			{
				int length = end - start;
				return displayName.Substring(start, length);
			}
			log("bad brackets", "getTextPanelName()", Logger.severity.TRACE);
			return null;
		}

		private static LinkedList<Ingame.IMyTextPanel> findTextPanel(IMyCubeBlock showoff)
		{
			string searchForName = getTextPanelName(showoff);
			if (searchForName == null)
				return null;

			List<IMySlimBlock> allBlocks = new List<IMySlimBlock>();
			showoff.CubeGrid.GetBlocks(allBlocks);

			LinkedList<Ingame.IMyTextPanel> textPanels = new LinkedList<Ingame.IMyTextPanel>();
			foreach (IMySlimBlock block in allBlocks)
			{
				IMyCubeBlock fatblock = block.FatBlock;
				if (fatblock == null)
					continue;

				Ingame.IMyTextPanel panel = fatblock as Ingame.IMyTextPanel;
				if (panel == null)
				{
					log("not a panel: " + fatblock.DisplayNameText, "findTextPanel()", Logger.severity.TRACE);
					continue;
				}

				if (fatblock.DisplayNameText.looseContains(searchForName))
				{
					log("adding panel: " + fatblock.DisplayNameText, "findTextPanel()", Logger.severity.TRACE);
					textPanels.AddLast(panel);
				}
			}
			return textPanels;
		}

		public static void doShowoff(IMyCubeBlock showoff)
		{
			LinkedList<Ingame.IMyTextPanel> textPanels = findTextPanel(showoff);
			if (textPanels == null)
				return;

			foreach (Ingame.IMyTextPanel panel in textPanels)
			{
				log("writing to panel: " + panel.DisplayNameText, "findTextPanel()", Logger.severity.TRACE);
				panel.WritePublicText("");

				List<ITerminalProperty> allProperties = new List<ITerminalProperty>();
				panel.GetProperties(allProperties);

				panel.WritePublicText("Properties:\n", true);
				foreach (ITerminalProperty prop in allProperties)
					panel.WritePublicText(prop.Id + '\n', true);

				List<ITerminalAction> allActions = new List<ITerminalAction>();
				panel.GetActions(allActions);

				panel.WritePublicText("Actions:\n", true);
				foreach (ITerminalAction action in allActions)
					panel.WritePublicText(action.Id + '\n', true);
			}
		}


		private static Logger myLogger;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private static void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ alwaysLog(toLog, method, level); }
		private static void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null)
				myLogger = new Logger(string.Empty, "Showoff");
			myLogger.log(level, method, toLog);
		}
	}
}
