using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Attached;
using Rynchodon.Instructions;
using Rynchodon.Utility.Network;
using Sandbox.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.AntennaRelay
{
	public class TextPanel : BlockInstructions
	{

		private const string radarIconId = "Radar";

		private const string timeString = "Current as of: ";

		private const char separator = ':';

		private static readonly char[] OptionsSeparators = { ',', ';', ':' };

		private static readonly Logger s_logger = new Logger("TextPanel");

		[Flags]
		private enum Option : byte
		{
			None = 0,
			DisplayDetected = 1,
			GPS = 2,
			EntityId = 4
		}

		static TextPanel()
		{
			MessageHandler.Handlers.Add(MessageHandler.SubMod.TP_DisplayEntities, Handler_DisplayEntities);
		}

		private static void Handler_DisplayEntities(byte[] message, int pos)
		{
			long panelId = ByteConverter.GetLong(message, ref pos);

			TextPanel panel;
			if (!Registrar.TryGetValue(panelId, out panel))
			{
				s_logger.alwaysLog("Text panel not found in registrar: " + panelId, "Handler_DisplayEntities()", Logger.severity.ERROR);
				return;
			}

			s_logger.debugLog("Found text panel with id: " + panelId, "Handler_DisplayEntities()");

			List<long> detectedIds = new List<long>();
			while (pos < message.Length)
				detectedIds.Add(ByteConverter.GetLong(message, ref pos));

			panel.Display(detectedIds);
		}

		private Ingame.IMyTextPanel m_textPanel;
		private Logger myLogger = new Logger(null, "TextPanel");

		private IMyTerminalBlock myTermBlock;

		private NetworkClient m_networkClient;

		private Option m_options;

		public TextPanel(IMyCubeBlock block)
			: base(block)
		{
			myLogger = new Logger(GetType().Name, block);
			m_textPanel = block as Ingame.IMyTextPanel;
			myTermBlock = block as IMyTerminalBlock;
			m_networkClient = new NetworkClient(block);
			myLogger.debugLog("init: " + m_block.DisplayNameText, "TextPanel()");

			Registrar.Add(block, this);
		}

		protected override bool ParseAll(string instructions)
		{
			string[] opts = instructions.RemoveWhitespace().Split(OptionsSeparators);
			m_options = Option.None;
			foreach (string opt in opts)
			{
				Option option;
				if (Enum.TryParse(opt, true, out option))
					m_options |= option;
				else
					return false;
			}
			return m_options != Option.None;
		}

		public void Update100()
		{
			UpdateInstructions();

			if (!HasInstructions)
				return;

			if ((m_options & Option.DisplayDetected) != 0)
				Display();
		}

		private void Display(List<long> entityIds = null)
		{
			myLogger.debugLog("Building display list", "Display()", Logger.severity.TRACE);

			NetworkStorage store = m_networkClient.GetStorage();
			if (store == null)
			{
				m_textPanel.WritePublicText("No network connection");
				return;
			}

			List<sortableLastSeen> sortableSeen = entityIds == null ? AllLastSeen(store) : SelectLastSeen(store, entityIds);

			if (sortableSeen.Count == 0)
			{
				m_textPanel.WritePublicText("No entities detected");
				return;
			}

			sortableSeen.Sort();

			StringBuilder displayText = new StringBuilder();
			displayText.Append(timeString);
			displayText.Append(DateTime.Now.ToLongTimeString());
			displayText.AppendLine();
			int count = 0;
			foreach (sortableLastSeen sortable in sortableSeen)
			{
				sortable.TextForPlayer(displayText, count++);
				if (count >= 20)
					break;
			}

			string displayString = displayText.ToString();

			myLogger.debugLog("Writing to panel: " + m_textPanel.DisplayNameText, "Display()", Logger.severity.TRACE);
			m_textPanel.WritePublicText(displayText.ToString());
		}

		private List<sortableLastSeen> AllLastSeen(NetworkStorage store)
		{
			Vector3D myPos = m_block.GetPosition();
			List<sortableLastSeen> sortableSeen = new List<sortableLastSeen>();

			store.ForEachLastSeen((LastSeen seen) => {
				IMyCubeGrid grid = seen.Entity as IMyCubeGrid;
				if (grid != null && AttachedGrid.IsGridAttached(m_block.CubeGrid, grid, AttachedGrid.AttachmentKind.Physics))
					return;

				ExtensionsRelations.Relations relations = m_block.getRelationsTo(seen.Entity, ExtensionsRelations.Relations.Enemy).highestPriority();
				sortableSeen.Add(new sortableLastSeen(myPos, seen, relations, m_options));
				myLogger.debugLog("item: " + seen.Entity.getBestName() + ", relations: " + relations, "Display()");
			});

			return sortableSeen;
		}

		private List<sortableLastSeen> SelectLastSeen(NetworkStorage store, List<long> entityIds)
		{
			Vector3D myPos = m_block.GetPosition();
			List<sortableLastSeen> sortableSeen = new List<sortableLastSeen>();
			
			foreach (long id in entityIds)
			{
				LastSeen seen;
				if (store.TryGetLastSeen(id, out seen))
				{
					ExtensionsRelations.Relations relations = m_block.getRelationsTo(seen.Entity, ExtensionsRelations.Relations.Enemy).highestPriority();
					sortableSeen.Add(new sortableLastSeen(myPos, seen, relations, m_options));
					myLogger.debugLog("item: " + seen.Entity.getBestName() + ", relations: " + relations, "Display_FromProgram()");
				}
			}

			return sortableSeen;
		}

		private class sortableLastSeen : IComparable<sortableLastSeen>
		{
			private readonly ExtensionsRelations.Relations relations;
			private readonly double distance;
			private readonly int seconds;
			private readonly LastSeen seen;
			private readonly Vector3D predictedPos;
			private readonly Option options;

			private const string tab = "    ";
			private static readonly string GPStag1 = tab + tab + "GPS";

			private sortableLastSeen() { }

			public sortableLastSeen(Vector3D myPos, LastSeen seen, ExtensionsRelations.Relations relations, Option options)
			{
				this.seen = seen;
				this.relations = relations;
				TimeSpan sinceLastSeen;
				predictedPos = seen.predictPosition(out sinceLastSeen);
				distance = (predictedPos - myPos).Length();
				seconds = (int)sinceLastSeen.TotalSeconds;
				this.options = options;
			}

			public void TextForPlayer(StringBuilder builder, int count)
			{
				string time = (seconds / 60).ToString("00") + separator + (seconds % 60).ToString("00");
				bool friendly = relations.HasAnyFlag(ExtensionsRelations.Relations.Faction) || relations.HasAnyFlag(ExtensionsRelations.Relations.Owner);
				string bestName = friendly ? seen.Entity.getBestName() : null;

				builder.Append(relations);
				builder.Append(tab);
				builder.Append(seen.Type);
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
				builder.AppendLine();
				builder.Append(tab);
				builder.Append(tab);
				builder.Append(PrettySI.makePretty(distance));
				builder.Append('m');
				builder.Append(tab);
				builder.Append(time);
				if (seen.Info != null)
				{
					builder.Append(tab);
					builder.Append(seen.Info.Pretty_Volume());
				}
				builder.AppendLine();

				// GPS tag
				if ((options & Option.GPS) != 0)
				{
					builder.Append(GPStag1);
					if (friendly)
						builder.Append(bestName);
					else
					{
						builder.Append(relations);
						builder.Append(seen.Type);
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
				}

				// Entity id
				if ((options & Option.EntityId) != 0)
				{
					builder.Append(tab);
					builder.Append(tab);
					builder.Append("ID: ");
					builder.Append(seen.Entity.EntityId);
					builder.AppendLine();
				}
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
