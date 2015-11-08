using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Attached;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.AntennaRelay
{
	/// <summary>
	/// TextPanel will fetch instructions from Antenna and write them either for players or for programmable blocks.
	/// </summary>
	public class TextPanel
	{
		private const string command_forPlayer = "Display Detected";
		private const string command_forProgram = "Transmit Detected to ";

		private const string publicTitle_forPlayer = "Detected Grids";
		private const string publicTitle_forProgram = "Transmission to Programmable block";
		private const string publicTitle_fromProgramParsed = "Transmission from Programmable block";
		private const string blockName_fromProgram = "from Program";

		private const string radarIconId = "Radar";
		private const string messageToProgram = "Fetch Detected from Text Panel";

		private const string timeString = "Current as of: ";

		private readonly string[] newLine = { "\n", "\r", "\r\n" };

		private const char separator = ':';

		private IMyCubeBlock myCubeBlock;
		private Ingame.IMyTextPanel myTextPanel;
		private Logger myLogger = new Logger(null, "TextPanel");

		private ReceiverBlock myAntenna;
		private ProgrammableBlock myProgBlock;
		private IMyTerminalBlock myTermBlock;

		private bool sentToProgram = false;

		public TextPanel(IMyCubeBlock block)
		{
			myCubeBlock = block;
			myTextPanel = block as Ingame.IMyTextPanel;
			myTermBlock = block as IMyTerminalBlock;
			myLogger = new Logger("TextPanel", () => myCubeBlock.CubeGrid.DisplayName, () => myCubeBlock.getNameOnly());
			myLogger.debugLog("init: " + myCubeBlock.DisplayNameText, "DelayedInit()");
			myTermBlock.CustomNameChanged += TextPanel_CustomNameChanged;
			myTermBlock.OnClosing += Close;
		}

		private void Close(IMyEntity entity)
		{
			if (myCubeBlock != null)
			{
				myTermBlock.CustomNameChanged -= TextPanel_CustomNameChanged;
				myCubeBlock = null;
			}
		}

		private string previousName;

		/// <summary>
		/// Checks for a change in the name and responds to added commmands.
		/// </summary>
		/// <param name="obj">not used</param>
		private void TextPanel_CustomNameChanged(IMyTerminalBlock obj)
		{
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
			{ myLogger.alwaysLog("Exception: " + ex, "TextPanel_CustomNameChanged()", Logger.severity.ERROR); }
		}

		public void UpdateAfterSimulation100()
		{
			try
			{
				string publicTitle = myTextPanel.GetPublicTitle();
				if (publicTitle == publicTitle_forPlayer || publicTitle == publicTitle_fromProgramParsed)
					checkAge();

				displayLastSeen();
				TextPanel_CustomNameChanged(null);
			}
			catch (Exception ex) { myLogger.alwaysLog("Exception: " + ex, "UpdateAfterSimulation100()", Logger.severity.ERROR); }
		}

		/// <summary>
		/// Search for an attached antenna, if we do not have one.
		/// </summary>
		/// <returns>true iff current antenna is valid or one was found</returns>
		private bool findAntenna()
		{
			if (myAntenna.IsOpen()) // already have one
				return true;

			myAntenna = null;
			Registrar.ForEach((RadioAntenna antenna) => {
				if (antenna.CubeBlock.canSendTo(myCubeBlock, true))
				{
					myLogger.debugLog("found antenna: " + antenna.CubeBlock.DisplayNameText, "searchForAntenna()", Logger.severity.INFO);
					myAntenna = antenna;
					return true;
				}
				return false;
			});

			if (myAntenna != null)
				return true;

			Registrar.ForEach((LaserAntenna antenna) => {
				if (antenna.CubeBlock.canSendTo(myCubeBlock, true))
				{
					myLogger.debugLog("found antenna: " + antenna.CubeBlock.DisplayNameText, "searchForAntenna()", Logger.severity.INFO);
					myAntenna = antenna;
					return true;
				}
				return false;
			});

			return myAntenna != null;
		}

		private static readonly MyObjectBuilderType ProgOBtype = typeof(MyObjectBuilder_MyProgrammableBlock);

		private bool findProgBlock()
		{
			if (myProgBlock.IsOpen()) // already have one
				return true;

			string instruction = myCubeBlock.getInstructions().RemoveWhitespace().ToLower();
			string command = command_forProgram.RemoveWhitespace().ToLower();

			int destNameIndex = instruction.IndexOf(command) + command.Length;
			if (destNameIndex >= instruction.Length)
			{
				myLogger.debugLog("destNameIndex = " + destNameIndex + ", instruction.Length = " + instruction.Length, "searchForAntenna()", Logger.severity.TRACE);
				return false;
			}
			string destName = instruction.Substring(destNameIndex);

			myLogger.debugLog("searching for a programmable block: " + destName, "searchForAntenna()", Logger.severity.TRACE);

			ReadOnlyList<IMyCubeBlock> progBlocks = CubeGridCache.GetFor(myCubeBlock.CubeGrid).GetBlocksOfType(ProgOBtype);
			if (progBlocks == null)
			{
				myLogger.debugLog("no programmable blocks", "searchForAntenna()", Logger.severity.TRACE);
				return false;
			}

			foreach (IMyTerminalBlock block in progBlocks)
				if (block.DisplayNameText.looseContains(destName))
					if (Registrar.TryGetValue(block.EntityId, out myProgBlock))
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
					//myLogger.debugLog("public text is not clear", "displayLastSeen()");
					runProgram();
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

			if (forProgram)
				myLogger.debugLog("building display list for program", "informPlayer()", Logger.severity.TRACE);
			else
				myLogger.debugLog("building display list for player", "informPlayer()", Logger.severity.TRACE);
			Vector3D myPos = myCubeBlock.GetPosition();
			List<sortableLastSeen> sortableSeen = new List<sortableLastSeen>();

			myAntenna.ForEachLastSeen(seen => {
				IMyCubeGrid grid = seen.Entity as IMyCubeGrid;
				if (grid == null || AttachedGrid.IsGridAttached(grid, myCubeBlock.CubeGrid, AttachedGrid.AttachmentKind.Physics))
					return false;

				ExtensionsRelations.Relations relations = myCubeBlock.getRelationsTo(grid, ExtensionsRelations.Relations.Enemy).highestPriority();
				sortableSeen.Add(new sortableLastSeen(myPos, seen, relations));
				return false;
			});

			sortableSeen.Sort();

			StringBuilder displayText = new StringBuilder();
			if (forProgram)
				foreach (sortableLastSeen sortable in sortableSeen)
					displayText.Append(sortable.TextForProgram());
			else
			{
				displayText.Append(timeString);
				displayText.Append(DateTime.Now.ToLongTimeString());
				writeTime = DateTime.Now;
				displayText.Append('\n');
				int count = 0;
				foreach (sortableLastSeen sortable in sortableSeen)
				{
					displayText.Append(sortable.TextForPlayer(count++));
					if (count >= 20)
						break;
				}
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
				if (myProgBlock.messageCount > 0)
				{
					myLogger.debugLog("cannot send message to " + myProgBlock.CubeBlock.DisplayNameText, "runProgram()");
					return;
				}
				myLogger.debugLog("sending message to " + myProgBlock.CubeBlock.DisplayNameText, "runProgram()");
				Message toSend = new Message(messageToProgram, myProgBlock.CubeBlock, myCubeBlock);
				myProgBlock.Receive(toSend);
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
			newText.Append(timeString);
			newText.Append(DateTime.Now.ToLongTimeString());
			writeTime = DateTime.Now;
			newText.Append('\n');
			for (int d = 1; d < instructions.Length; d++) // skip first
			{
				if (string.IsNullOrWhiteSpace(instructions[d]))
					continue;

				myLogger.debugLog("checking id: " + instructions[d], "replaceEntityIdsWithLastSeen()");
				long entityId = long.Parse(instructions[d]);
				//myLogger.debugLog("got long: " + entityId, "replaceEntityIdsWithLastSeen()");
				LastSeen seen;
				if (myAntenna.tryGetLastSeen(entityId, out seen))
				{
					myLogger.debugLog("got last seen: " + seen, "replaceEntityIdsWithLastSeen()");
					IMyCubeGrid cubeGrid = seen.Entity as IMyCubeGrid;
					if (cubeGrid == null)
					{
						myLogger.alwaysLog("cubeGrid from LastSeen is null", "replaceEntityIdsWithLastSeen()", Logger.severity.WARNING);
						continue;
					}
					ExtensionsRelations.Relations relations = myCubeBlock.getRelationsTo(cubeGrid, ExtensionsRelations.Relations.Enemy).highestPriority();
					newText.Append((new sortableLastSeen(myPos, seen, relations)).TextForPlayer(count++));
					//myLogger.debugLog("append OK", "replaceEntityIdsWithLastSeen()");
				}
			}
			myTextPanel.WritePublicText(newText.ToString());
			myTextPanel.WritePublicTitle(publicTitle_fromProgramParsed);
		}

		/// <summary>
		/// The time of last writing to public text
		/// </summary>
		private DateTime writeTime;

		/// <summary>
		/// display information is old
		/// </summary>
		private bool displayIsOld = false;

		/// <summary>
		/// how long until displayed information is old
		/// </summary>
		private TimeSpan displayOldAfter = new TimeSpan(0, 0, 10);

		/// <summary>
		/// background colour when display is new
		/// </summary>
		private Color youngBackgroundColour = Color.Black;

		/// <summary>
		/// background colour when display is old
		/// </summary>
		private Color oldBackgroundColour = Color.Gray;

		/// <summary>
		/// check the age of the message on the panel and change colour if it is old
		/// </summary>
		private void checkAge()
		{
			if (displayIsOld)
			{
				if (DateTime.Now - writeTime < displayOldAfter) // has just become young
				{
					displayIsOld = false;

					ITerminalProperty<Color> backgroundColourProperty = myTextPanel.GetProperty("BackgroundColor").AsColor();

					oldBackgroundColour = backgroundColourProperty.GetValue(myTextPanel);
					if (youngBackgroundColour == Color.Gray)
						youngBackgroundColour = Color.Black;

					backgroundColourProperty.SetValue(myTextPanel, youngBackgroundColour);

					myLogger.debugLog("Panel data is now young, storing " + oldBackgroundColour + ", using " + youngBackgroundColour, "checkAge()", Logger.severity.DEBUG);
				}
			}
			else
			{
				if (DateTime.Now - writeTime > displayOldAfter) // has just become old
				{
					displayIsOld = true;

					ITerminalProperty<Color> backgroundColourProperty = myTextPanel.GetProperty("BackgroundColor").AsColor();

					youngBackgroundColour = backgroundColourProperty.GetValue(myTextPanel);
					if (oldBackgroundColour == Color.Black)
						oldBackgroundColour = Color.Gray;

					backgroundColourProperty.SetValue(myTextPanel, oldBackgroundColour);

					myLogger.debugLog("Panel data is now old, storing " + youngBackgroundColour + ", using " + oldBackgroundColour, "checkAge()", Logger.severity.DEBUG);
				}
			}
		}

		private class sortableLastSeen : IComparable<sortableLastSeen>
		{
			private readonly ExtensionsRelations.Relations relations;
			private readonly double distance;
			private readonly int seconds;
			private readonly LastSeen seen;
			private readonly Vector3D predictedPos;

			private const string tab = "    ";
			private static readonly string GPStag1 = '\n' + tab + tab + "GPS";
			private const string GPStag2 = "Detected_";

			private sortableLastSeen() { }

			public sortableLastSeen(Vector3D myPos, LastSeen seen, ExtensionsRelations.Relations relations)
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
				bool friendly = relations.HasFlagFast(ExtensionsRelations.Relations.Faction) || relations.HasFlagFast(ExtensionsRelations.Relations.Owner);
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
				{
					if (seen.isRecent_Radar())
					{
						builder.Append("Has Radar");
						builder.Append(tab);
					}
					if (seen.isRecent_Jam())
					{
						builder.Append("Has Jammer");
						builder.Append(tab);
					}
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
				builder.Append(separator);
				builder.AppendLine();

				// Entity id
				builder.Append(tab);
				builder.Append(tab);
				builder.Append("ID: ");
				builder.Append(seen.Entity.EntityId);
				builder.AppendLine();

				return builder;
			}

			public StringBuilder TextForProgram()
			{
				bool friendly = relations.HasFlagFast(ExtensionsRelations.Relations.Faction) || relations.HasFlagFast(ExtensionsRelations.Relations.Owner);
				string bestName;
				if (friendly)
					bestName = seen.Entity.getBestName();
				else
					bestName = "Unknown";

				StringBuilder builder = new StringBuilder();
				builder.Append(seen.Entity.EntityId); builder.Append(separator);
				builder.Append(relations); builder.Append(separator);
				builder.Append(bestName); builder.Append(separator);
				builder.Append(seen.isRecent_Radar() || seen.isRecent_Jam()); builder.Append(separator);
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
