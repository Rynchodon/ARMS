#define LOG_ENABLED // remove on build

using System;
using System.Collections.Generic;
//using System.Linq;
using System.Text;

using Sandbox.Common;
using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;

using VRage;
using VRageMath;

namespace Rynchodon.AntennaRelay
{
	/// <summary>
	/// TextPanel will fetch instructions from Antenna and write them either for players or for programmable blocks.
	/// </summary>
	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_TextPanel))]
	public class TextPanel : UpdateEnforcer
	{
		private const string command_forPlayer = "Display Detected";
		private const string command_forProgram = "Transmit Detected to ";

		private const string publicTitle_forPlayer = "Detected Grids";
		private const string publicTitle_forProgram = "Transmission to Programmable block";
		private const string publicTitle_fromProgramParsed = "Transmission from Programmable block";
		private const string blockName_fromProgram = "from Program";

		private const string radarIconId = "Radar";
		private const string messageToProgram = "Fetch Detected from Text Panel";

		private const char separator = ':';

		private IMyCubeBlock myCubeBlock;
		private Ingame.IMyTextPanel myTextPanel;
		private Logger myLogger = new Logger(null, "TextPanel");

		private Receiver myAntenna;
		private ProgrammableBlock myProgBlock;
		private IMyTerminalBlock myTermBlock;

		private bool sentToProgram = false;

		protected override void DelayedInit()
		{
			myCubeBlock = Entity as IMyCubeBlock;
			myTextPanel = Entity as Ingame.IMyTextPanel;
			myTermBlock = Entity as IMyTerminalBlock;
			myLogger = new Logger("TextPanel", () => myCubeBlock.CubeGrid.DisplayName, () => myCubeBlock.getNameOnly());
			EnforcedUpdate = MyEntityUpdateEnum.EACH_100TH_FRAME;
			myLogger.debugLog("init: " + myCubeBlock.DisplayNameText, "DelayedInit()");

			myTermBlock.CustomNameChanged += TextPanel_CustomNameChanged;
		}

		private string previousName;

		/// <summary>
		/// Checks for a change in the name and responds to added commmands.
		/// </summary>
		/// <param name="obj">not used</param>
		private void TextPanel_CustomNameChanged(IMyTerminalBlock obj)
		{
			if (!IsInitialized || Closed)
				return;
			try
			{
				if (myCubeBlock.DisplayNameText == previousName)
					return;

				string instructions = myCubeBlock.getInstructions();
				if (instructions != null)
				{
					string[] splitInstructions = instructions.Split(separator);
					if (splitInstructions[0] == blockName_fromProgram)
					{
						myLogger.debugLog("replacing entity Ids", "TextPanel_CustomNameChanged()");
						myTextPanel.SetCustomName(myCubeBlock.getNameOnly());
						replaceEntityIdsWithLastSeen(splitInstructions);
					}
				}

				myProgBlock = null;
			}
			catch (Exception ex)
			{ myLogger.log("Exception: " + ex, "TextPanel_CustomNameChanged()", Logger.severity.ERROR); }
		}

		public override void Close()
		{
			base.Close();

			if (myCubeBlock != null)
			{
				myTermBlock.CustomNameChanged -= TextPanel_CustomNameChanged;
				myCubeBlock = null;
			}
		}

		public override void UpdateAfterSimulation100()
		{
			if (!IsInitialized || Closed)
				return;

			try
			{
				displayLastSeen();
				TextPanel_CustomNameChanged(null);
			}
			catch (Exception ex) { myLogger.log("Exception: " + ex, "UpdateAfterSimulation100()", Logger.severity.ERROR); }
		}

		/// <summary>
		/// Search for an attached antenna, if we do not have one.
		/// </summary>
		/// <returns>true iff current antenna is valid or one was found</returns>
		private bool findAntenna()
		{
			if (myAntenna != null && !myAntenna.Closed) // already have one
				return true;

			foreach (Receiver antenna in RadioAntenna.registry)
				if (antenna.CubeBlock.canSendTo(myCubeBlock, true))
				{
					myLogger.debugLog("found antenna: " + antenna.CubeBlock.DisplayNameText, "searchForAntenna()", Logger.severity.INFO);
					myAntenna = antenna;
					return true;
				}
			foreach (Receiver antenna in LaserAntenna.registry)
				if (antenna.CubeBlock.canSendTo(myCubeBlock, true))
				{
					myLogger.debugLog("found antenna: " + antenna.CubeBlock.DisplayNameText, "searchForAntenna()", Logger.severity.INFO);
					myAntenna = antenna;
					return true;
				}
			return false;
		}

		private static readonly MyObjectBuilderType ProgOBtype = typeof(MyObjectBuilder_MyProgrammableBlock);

		private bool findProgBlock()
		{
			if (myProgBlock != null && !myProgBlock.Closed) // already have one
				return true;

			string instruction = myCubeBlock.getInstructions().RemoveWhitespace().ToLower();
			string command = command_forProgram .RemoveWhitespace().ToLower();

			int destNameIndex = instruction.IndexOf(command) + command.Length;
			if (destNameIndex >= instruction.Length)
			{
				myLogger.debugLog("destNameIndex = " + destNameIndex + ", instruction.Length = " + instruction.Length, "searchForAntenna()", Logger.severity.TRACE);
				return false;
			}
			string destName = instruction.Substring(destNameIndex);

			myLogger.debugLog("searching for a programmable block: " + destName, "searchForAntenna()", Logger.severity.TRACE);

			IReadOnlyCollection<Ingame.IMyTerminalBlock> progBlocks = CubeGridCache.GetFor(myCubeBlock.CubeGrid).GetBlocksOfType(ProgOBtype);
			if (progBlocks == null)
			{
				myLogger.debugLog("no programmable blocks", "searchForAntenna()", Logger.severity.TRACE);
				return false;
			}

			foreach (Ingame.IMyTerminalBlock block in progBlocks)
				if (block.DisplayNameText.looseContains(destName))
					if (ProgrammableBlock.TryGet(block as IMyCubeBlock, out myProgBlock))
					{
						myLogger.debugLog("found programmable block: " + block.DisplayNameText, "searchForAntenna()", Logger.severity.INFO);
						return true;
					}
					else
					{
						myLogger.debugLog("failed to get receiver for: " + block.DisplayNameText, "searchForAntenna()", Logger.severity.WARNING);
						return false;
					}

			return false;
		}

		/// <summary>
		/// Display text either for player or for program
		/// </summary>
		private void displayLastSeen()
		{
			bool forProgram;
			string instruction = myCubeBlock.getInstructions();

			if (instruction == null)
				return;

			if (instruction.looseContains(command_forProgram))
			{
				if (sentToProgram && myTextPanel.GetPublicTitle() == publicTitle_forProgram && !string.IsNullOrWhiteSpace(myTextPanel.GetPublicText()))
				{
					myLogger.debugLog("public text is not clear", "displayLastSeen()");
					return;
				}
				forProgram = true;
			}
			else if (instruction.looseContains(command_forPlayer))
				forProgram = false;
			else
				return;

			if (!findAntenna())
				return;

			IEnumerator<LastSeen> toDisplay = myAntenna.getLastSeenEnum();

			if (forProgram)
				myLogger.debugLog("building display list for program", "informPlayer()", Logger.severity.TRACE);
			else
				myLogger.debugLog("building display list for player", "informPlayer()", Logger.severity.TRACE);
			Vector3D myPos = myCubeBlock.GetPosition();
			List<sortableLastSeen> sortableSeen = new List<sortableLastSeen>();
			while (toDisplay.MoveNext())
			{
				IMyCubeGrid grid = toDisplay.Current.Entity as IMyCubeGrid;
				if (grid == null || AttachedGrids.isGridAttached(grid, myCubeBlock.CubeGrid))
					continue;

				IMyCubeBlockExtensions.Relations relations = myCubeBlock.getRelationsTo(grid, IMyCubeBlockExtensions.Relations.Enemy).mostHostile();
				sortableSeen.Add(new sortableLastSeen(myPos, toDisplay.Current, relations));
			}
			sortableSeen.Sort();

			int count = 0;
			StringBuilder displayText = new StringBuilder();
			if (forProgram)
				foreach (sortableLastSeen sortable in sortableSeen)
					displayText.Append(sortable.TextForProgram());
			else
				foreach (sortableLastSeen sortable in sortableSeen)
				{
					displayText.Append(sortable.TextForPlayer(count++));
					if (count >= 50)
						break;
				}

			string displayString = displayText.ToString();

			myLogger.debugLog("writing to panel " + myTextPanel.DisplayNameText, "findTextPanel()", Logger.severity.TRACE);
			myTextPanel.WritePublicText(displayText.ToString());

			// set public title
			if (forProgram)
			{
				if (myTextPanel.GetPublicTitle() != publicTitle_forProgram)
				{
					myTextPanel.WritePublicTitle(publicTitle_forProgram);
					myTextPanel.AddImageToSelection(radarIconId);
					//myTextPanel.ShowTextureOnScreen();
				}

				runProgram();
			}
			else
				if (myTextPanel.GetPublicTitle() != publicTitle_forPlayer)
				{
					myTextPanel.WritePublicTitle(publicTitle_forPlayer);
					myTextPanel.AddImageToSelection(radarIconId);
					//myTextPanel.ShowTextureOnScreen();
				}
		}

		/// <summary>
		/// Create a message and send to programmable block
		/// </summary>
		private void runProgram()
		{
			if (findProgBlock())
			{
				myLogger.debugLog("sending message to " + myProgBlock.CubeBlock.DisplayNameText, "runProgram()");
				Message toSend = new Message(messageToProgram, myProgBlock.CubeBlock, myCubeBlock);
				myProgBlock.receive(toSend);
				sentToProgram = true;
			}
			else
				sentToProgram = false;
		}

		private void replaceEntityIdsWithLastSeen(string[] instructions)
		{
			if (!findAntenna())
				return;

			Vector3D myPos = myCubeBlock.GetPosition();
			int count = 0;
			StringBuilder newText = new StringBuilder();
			for (int d = 1; d < instructions.Length; d++) // skip first
			{
				if (string.IsNullOrWhiteSpace(instructions[d]))
					continue;

				myLogger.debugLog("checking id: " + instructions[d], "replaceEntityIdsWithLastSeen()");
				long entityId = long.Parse(instructions[d]);
				myLogger.debugLog("got long: " + entityId, "replaceEntityIdsWithLastSeen()");
				LastSeen seen;
				if (myAntenna.tryGetLastSeen(entityId, out seen))
				{
					myLogger.debugLog("got last seen: " + seen, "replaceEntityIdsWithLastSeen()");
					IMyCubeGrid cubeGrid = seen.Entity as IMyCubeGrid;
					if (cubeGrid == null)
					{
						myLogger.log("cubeGrid from LastSeen is null", "replaceEntityIdsWithLastSeen()", Logger.severity.WARNING);
						continue;
					}
					IMyCubeBlockExtensions.Relations relations = myCubeBlock.getRelationsTo(cubeGrid, IMyCubeBlockExtensions.Relations.Enemy).mostHostile();
					newText.Append((new sortableLastSeen(myPos, seen, relations)).TextForPlayer(count++));
					myLogger.debugLog("append OK", "replaceEntityIdsWithLastSeen()");
				}
			}
			myTextPanel.WritePublicText(newText.ToString());
			myTextPanel.WritePublicTitle(publicTitle_fromProgramParsed);
		}

		private class sortableLastSeen : IComparable<sortableLastSeen>
		{
			private readonly IMyCubeBlockExtensions.Relations relations;
			private readonly double distance;
			private readonly int seconds;
			private readonly LastSeen seen;
			private readonly Vector3D predictedPos;

			private const string tab = "    ";
			private readonly string GPStag1 = '\n' + tab + tab + "GPS";
			private readonly string GPStag2 = "Detected_";

			private sortableLastSeen() { }

			public sortableLastSeen(Vector3D myPos, LastSeen seen, IMyCubeBlockExtensions.Relations relations)
			{
				this.seen = seen;
				this.relations = relations;
				TimeSpan sinceLastSeen;
				predictedPos = seen.predictPosition(out sinceLastSeen);
				distance = (predictedPos - myPos).Length();
				seconds = (int)sinceLastSeen.TotalSeconds;
			}

			public StringBuilder TextForPlayer(int count)
			{
				string time = (seconds / 60).ToString("00") + separator + (seconds % 60).ToString("00");
				bool friendly = relations.HasFlagFast(IMyCubeBlockExtensions.Relations.Faction) || relations.HasFlagFast(IMyCubeBlockExtensions.Relations.Owner);
				string bestName = seen.Entity.getBestName();

				StringBuilder builder = new StringBuilder();
				builder.Append(relations);
				builder.Append(tab);
				if (friendly)
				{
					builder.Append(bestName);
					builder.Append(tab);
				}
				else
					if (seen.EntityHasRadar)
					{
						builder.Append("Has Radar");
						builder.Append(tab);
					}
				builder.Append(PrettySI.makePretty(distance));
				builder.Append('m');
				builder.Append(tab);
				builder.Append(time);
				if (seen.Info != null)
				{
					builder.Append(tab);
					builder.Append(seen.Info.Pretty_Volume());
				}

				// GPS tag
				builder.Append(GPStag1);
				if (friendly)
					builder.Append(bestName);
				else
				{
					builder.Append(GPStag2);
					builder.Append(relations);
					builder.Append('#');
					builder.Append(count);
				}
				builder.Append(separator);
				builder.Append((int)predictedPos.X);
				builder.Append(separator);
				builder.Append((int)predictedPos.Y);
				builder.Append(separator);
				builder.Append((int)predictedPos.Z);
				builder.Append(separator + "\n");

				return builder;
			}

			public StringBuilder TextForProgram()
			{
				bool friendly = relations.HasFlagFast(IMyCubeBlockExtensions.Relations.Faction) || relations.HasFlagFast(IMyCubeBlockExtensions.Relations.Owner);
				string bestName;
				if (friendly)
					bestName = seen.Entity.getBestName();
				else
					bestName = "Unknown";

				StringBuilder builder = new StringBuilder();
				builder.Append(seen.Entity.EntityId); builder.Append(separator);
				builder.Append(relations); builder.Append(separator);
				builder.Append(bestName); builder.Append(separator);
				builder.Append(seen.EntityHasRadar); builder.Append(separator);
				builder.Append(distance); builder.Append(separator);
				builder.Append(seconds); builder.Append(separator);
				if (seen.Info != null)
					builder.Append(seen.Info.Volume);
				builder.AppendLine();

				return builder;
			}

			/// <summary>
			/// sort by relations, then by distance
			/// </summary>
			public int CompareTo(sortableLastSeen other)
			{
				if (this.relations != other.relations)
					return this.relations.CompareTo(other.relations);
				return this.distance.CompareTo(other.distance);
			}
		}
	}
}
