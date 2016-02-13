using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Attached;
using Rynchodon.Instructions;
using Sandbox.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.AntennaRelay
{
	public class TextPanel : BlockInstructions
	{
		private const string command_forPlayer = "Display Detected";

		private const string publicTitle_forPlayer = "Detected Grids";
		private const string publicTitle_fromProgramParsed = "Transmission from Programmable block";

		private const string radarIconId = "Radar";
		private const string messageToProgram = "Fetch Detected from Text Panel";

		private const string timeString = "Current as of: ";

		private readonly string[] newLine = { "\n", "\r", "\r\n" };

		private const char separator = ':';

		private Ingame.IMyTextPanel m_textPanel;
		private Logger myLogger = new Logger(null, "TextPanel");

		private IMyTerminalBlock myTermBlock;

		private bool m_commandSet_forPlayer;

		private NetworkNode m_node;

		public TextPanel(IMyCubeBlock block, NetworkNode node)
			: base(block)
		{
			myLogger = new Logger(GetType().Name, block);
			m_textPanel = block as Ingame.IMyTextPanel;
			myTermBlock = block as IMyTerminalBlock;
			m_node = node;
			myLogger.debugLog("init: " + m_block.DisplayNameText, "DelayedInit()");
		}

		protected override bool ParseAll(string instructions)
		{
			m_commandSet_forPlayer = instructions.looseContains(command_forPlayer);
			return m_commandSet_forPlayer;
		}

		public void Update100()
		{
			UpdateInstructions();

			if (m_commandSet_forPlayer)
				Display_ForPlayer();
		}

		private void Display_ForPlayer()
		{
			myLogger.debugLog("Building display list for player", "Display_ForPlayer()", Logger.severity.TRACE);
			Vector3D myPos = m_block.GetPosition();
			List<sortableLastSeen> sortableSeen = new List<sortableLastSeen>();
			m_node.Storage.ForEachLastSeen((LastSeen seen) => {
				IMyCubeGrid grid = seen.Entity as IMyCubeGrid;
				if (grid != null && AttachedGrid.IsGridAttached(m_block.CubeGrid, grid, AttachedGrid.AttachmentKind.Physics))
					return;

				ExtensionsRelations.Relations relations = m_block.getRelationsTo(seen.Entity, ExtensionsRelations.Relations.Enemy).highestPriority();
				sortableSeen.Add(new sortableLastSeen(myPos, seen, relations));
				myLogger.debugLog("item: " + seen.Entity.getBestName() + ", relations: " + relations, "Display_ForPlayer()");
			});

			sortableSeen.Sort();

			StringBuilder displayText = new StringBuilder();
			displayText.Append(timeString);
			displayText.Append(DateTime.Now.ToLongTimeString());
			displayText.AppendLine();
			int count = 0;
			foreach (sortableLastSeen sortable in sortableSeen)
			{
				displayText.Append(sortable.TextForPlayer(count++));
				if (count >= 20)
					break;
			}

			string displayString = displayText.ToString();

			myLogger.debugLog("Writing to panel: " + m_textPanel.DisplayNameText, "Display_ForPlayer()", Logger.severity.TRACE);
			m_textPanel.WritePublicText(displayText.ToString());

			if (m_textPanel.GetPublicTitle() != publicTitle_forPlayer)
			{
				m_textPanel.WritePublicTitle(publicTitle_forPlayer);
				m_textPanel.AddImageToSelection(radarIconId);
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
				string bestName = friendly ? seen.Entity.getBestName() : null;

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
					if (seen.Entity is IMyCharacter)
					{
						builder.Append("Meatbag");
						builder.Append(tab);
					}
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
					if (seen.Entity is IMyCharacter)
						builder.Append("Meatbag");
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
