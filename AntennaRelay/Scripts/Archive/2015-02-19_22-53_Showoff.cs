using System;
using System.Collections.Generic;
//using System.Linq;
using System.Text;

using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRageMath;

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

		//private struct DistLastSeen
		//{
		//	public double distance;
		//	public LastSeen seen;

		//	public DistLastSeen(Vector3D me, Vector3D target, LastSeen seen)
		//	{
		//		this.distance = (me - target).Length();
		//		this.seen = seen;
		//	}
		//}

		public static void doShowoff(IMyCubeBlock showoff, IEnumerator<LastSeen> toDisplay)
		{
			LinkedList<Ingame.IMyTextPanel> textPanels = findTextPanel(showoff);
			if (textPanels == null)
				return;

			// sort by type[, distance]
			// display: Relations : Distance : GPS
			log("building toDisplay", "findTextPanel()", Logger.severity.TRACE);
			StringBuilder displayText = new StringBuilder();
			StringBuilder neutral = new StringBuilder();
			StringBuilder friendly = new StringBuilder();
			Vector3D myPos = showoff.GetPosition();
			int count = 0;
			while(toDisplay.MoveNext())
			{
				IMyCubeGrid grid = toDisplay.Current.Entity as IMyCubeGrid;
				if (grid == null)
					continue;

				string distance = ((int)grid.WorldAABB.Distance(myPos)).ToString();
				string GPS = "GPS:";
				// GPS:[Name]:[x]:[y]:[z]:

				IMyCubeBlockExtensions.Relations relations = showoff.getRelationsTo(grid, IMyCubeBlockExtensions.Relations.Enemies);
				if (relations.HasFlagFast(IMyCubeBlockExtensions.Relations.Enemies))
				{
					displayText.Append("Hostile : ");
					displayText.Append(distance);
					displayText.Append("m          ");
					displayText.Append("GPS:");
					displayText.Append("Hostile_"); 
					displayText.Append("AP#");
					displayText.Append(count++);
					Vector3D gridPosition = grid.GetPosition();
					displayText.Append(':');
					displayText.Append(Math.Round(gridPosition.X,2));
					displayText.Append(':');
					displayText.Append(Math.Round(gridPosition.Y, 2));
					displayText.Append(':');
					displayText.Append(Math.Round(gridPosition.Z, 2));
					displayText.Append(':');
					displayText.Append('\n');
				}
				else if (relations.HasFlagFast(IMyCubeBlockExtensions.Relations.Neutral))
				{
					neutral.Append("Neutral : ");
					neutral.Append(distance);
					neutral.Append("m          ");
					neutral.Append("GPS:");
					neutral.Append("Neutral_");
					neutral.Append("AP#");
					neutral.Append(count++);
					Vector3D gridPosition = grid.GetPosition();
					neutral.Append(':');
					neutral.Append(Math.Round(gridPosition.X, 2));
					neutral.Append(':');
					neutral.Append(Math.Round(gridPosition.Y, 2));
					neutral.Append(':');
					neutral.Append(Math.Round(gridPosition.Z, 2));
					neutral.Append(':');
					neutral.Append('\n');
				}
				else if (relations.HasFlagFast(IMyCubeBlockExtensions.Relations.Faction) || relations.HasFlagFast(IMyCubeBlockExtensions.Relations.Owner))
				{
					friendly.Append("Friendly : ");
					friendly.Append(grid.DisplayName);
					friendly.Append(" : ");
					friendly.Append(distance);
					friendly.Append("m          ");
					friendly.Append("GPS:");
					friendly.Append("Friendly_");
					friendly.Append("AP#");
					friendly.Append(count++);
					Vector3D gridPosition = grid.GetPosition();
					friendly.Append(':');
					friendly.Append(Math.Round(gridPosition.X, 2));
					friendly.Append(':');
					friendly.Append(Math.Round(gridPosition.Y, 2));
					friendly.Append(':');
					friendly.Append(Math.Round(gridPosition.Z, 2));
					friendly.Append(':');
					friendly.Append('\n');
				}
			}
			displayText.Append(neutral);
			displayText.Append(friendly);
			string displayString = displayText.ToString();

			foreach (Ingame.IMyTextPanel panel in textPanels)
			{
				log("writing to panel: " + panel.DisplayNameText, "findTextPanel()", Logger.severity.TRACE);
				panel.WritePublicText(displayString);
				panel.WritePublicTitle("Grids found by Autopilot");
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
