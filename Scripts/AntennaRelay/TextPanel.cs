using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;
using Rynchodon.Attached;
using Rynchodon.Autopilot;
using Rynchodon.Instructions;
using Rynchodon.Utility;
using Rynchodon.Utility.Network;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.AntennaRelay
{
	public class TextPanel : BlockInstructions
	{

		[Serializable]
		public class Builder_TextPanel
		{
			[XmlAttribute]
			public long BlockId;
			public byte Options;
		}

		[Flags]
		private enum Option : byte
		{
			None = 0,
			DisplayDetected = 1,
			GPS = 2,
			EntityId = 4,
			AutopilotStatus = 8,
			Refresh = 16,
		}

		private const char separator = ':';
		private const string radarIconId = "Radar";
		private const string timeString = "Current as of: ";
		private const string tab = "    ";

		private static readonly char[] OptionsSeparators = { ',', ';', ':' };
		private static readonly List<long> s_detectedIds = new List<long>();

		[OnWorldLoad]
		private static void CreateTerminal()
		{
			Logger.DebugLog("entered", Logger.severity.TRACE);
			TerminalControlHelper.EnsureTerminalControlCreated<MyTextPanel>();

			MyTerminalAction<MyTextPanel> textPanel_displayEntities = new MyTerminalAction<MyTextPanel>("DisplayEntities", new StringBuilder("Display Entities"), "Textures\\GUI\\Icons\\Actions\\Start.dds")
			{
				ValidForGroups = false,
				ActionWithParameters = TextPanel_DisplayEntities
			};
			MyTerminalControlFactory.AddAction(textPanel_displayEntities);

			MyTerminalControlFactory.AddControl(new MyTerminalControlSeparator<MyTextPanel>());

			AddCheckbox("DisplayDetected", "Display Detected", "Write detected entities to the public text of the panel", Option.DisplayDetected);
			AddCheckbox("DisplayGPS", "Display GPS", "Write gps with detected entities", Option.GPS);
			AddCheckbox("DisplayEntityId", "Display Entity ID", "Write entity ID with detected entities", Option.EntityId);
			AddCheckbox("DisplayAutopilotStatus", "Display Autopilot Status", "Write the status of nearby Autopilots to the public text of the panel", Option.AutopilotStatus);
		}

		private static void AddCheckbox(string id, string title, string toolTip, Option opt)
		{
			MyTerminalControlCheckbox<MyTextPanel> control = new MyTerminalControlCheckbox<MyTextPanel>(id, MyStringId.GetOrCompute(title), MyStringId.GetOrCompute(toolTip));
			new ValueSync<bool, TextPanel>(control, (panel) => (panel.m_optionsTerminal & opt) == opt,
				(panel, value) => {
					if (value)
						panel.m_optionsTerminal |= opt;
					else
						panel.m_optionsTerminal &= ~opt;
				});
			MyTerminalControlFactory.AddControl(control);
		}
		
		/// <param name="args">EntityIds as long</param>
		private static void TextPanel_DisplayEntities(MyFunctionalBlock block, ListReader<Ingame.TerminalActionParameter> args)
		{
			s_detectedIds.Clear();

			for (int i = 0; i < args.Count; i++)
			{
				if (args[i].TypeCode != TypeCode.Int64)
				{
					Logger.DebugLog("TerminalActionParameter # " + i + " is of wrong type, expected Int64, got " + args[i].TypeCode, Logger.severity.WARNING);
					if (MyAPIGateway.Session.Player != null)
						block.AppendCustomInfo("Failed to display entities:\nTerminalActionParameter #" + i + " is of wrong type, expected Int64 (AKA long), got " + args[i].TypeCode + '\n');
					return;
				}
				s_detectedIds.Add((long)args[i].Value);
			}

			TextPanel panel;
			if (!Registrar.TryGetValue(block.EntityId, out panel))
			{
				Logger.AlwaysLog("Text panel not found in registrar: " + block.EntityId, Logger.severity.ERROR);
				return;
			}

			Logger.DebugLog("Found text panel with id: " + block.EntityId);

			panel.Display(s_detectedIds);
		}

		private readonly Ingame.IMyTextPanel m_textPanel;

		private Option m_optionsTerminal;

		private IMyTerminalBlock myTermBlock;
		private RelayClient m_networkClient;
		private Option m_options;
		private List<sortableLastSeen> m_sortableList;
		private TimeSpan m_lastDisplay;

		private Logable Log { get { return new Logable(myTermBlock);  } }

		public TextPanel(IMyCubeBlock block)
			: base(block)
		{
			m_textPanel = block as Ingame.IMyTextPanel;
			myTermBlock = block as IMyTerminalBlock;
			m_networkClient = new RelayClient(block);
			Log.DebugLog("init: " + m_block.DisplayNameText);

			Registrar.Add(block, this);
		}

		public void ResumeFromSave(Builder_TextPanel builder)
		{
			if (this.m_block.EntityId != builder.BlockId)
				throw new ArgumentException("Serialized block id " + builder.BlockId + " does not match block id " + this.m_block.EntityId);

			this.m_optionsTerminal = (Option)builder.Options;
		}

		protected override bool ParseAll(string instructions)
		{
			string[] opts = instructions.RemoveWhitespace().Split(OptionsSeparators);
			m_options = Option.None;
			foreach (string opt in opts)
			{
				float dontCare;
				if (float.TryParse(opt, out dontCare))
					return false;

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

			if (m_optionsTerminal != Option.None)
				m_options = m_optionsTerminal;
			else if (!HasInstructions)
				return;

			if ((m_options & Option.DisplayDetected) != 0)
				Display();
			else if ((m_options & Option.AutopilotStatus) != 0)
				DisplyAutopilotStatus();

			if ((m_options & Option.Refresh) != 0)
			{
				m_textPanel.ShowTextureOnScreen();
				m_textPanel.ShowPublicTextOnScreen();
			}
		}

		private void Display(List<long> entityIds = null)
		{
			if (entityIds != null)
				UpdateInstructions();

			//Log.DebugLog("Building display list", "Display()", Logger.severity.TRACE);

			RelayStorage store = m_networkClient.GetStorage();
			if (store == null)
			{
				m_textPanel.WritePublicText("No network connection");
				return;
			}

			m_sortableList = ResourcePool<List<sortableLastSeen>>.Get();

			if (entityIds == null)
				AllLastSeen(store);
			else
				SelectLastSeen(store, entityIds);

			if (m_sortableList.Count == 0)
			{
				m_textPanel.WritePublicText("No entities detected");
				m_sortableList.Clear();
				ResourcePool<List<sortableLastSeen>>.Return(m_sortableList);
				return;
			}

			m_lastDisplay = Globals.ElapsedTime;
			m_sortableList.Sort();

			StringBuilder displayText = new StringBuilder();
			displayText.Append(timeString);
			displayText.Append(DateTime.Now.ToLongTimeString());
			displayText.AppendLine();
			int count = 0;
			foreach (sortableLastSeen sortable in m_sortableList)
			{
				sortable.TextForPlayer(displayText, count++);
				if (count >= 20)
					break;
			}

			m_sortableList.Clear();
			ResourcePool<List<sortableLastSeen>>.Return(m_sortableList);

			string displayString = displayText.ToString();

			//Log.DebugLog("Writing to panel: " + m_textPanel.DisplayNameText + ":\n\t" + displayString, "Display()", Logger.severity.TRACE);
			m_textPanel.WritePublicText(displayText.ToString());
		}

		private void AllLastSeen(RelayStorage store)
		{
			Vector3D myPos = m_block.GetPosition();

			store.ForEachLastSeen((LastSeen seen) => {
				IMyCubeGrid grid = seen.Entity as IMyCubeGrid;
				if (grid != null && AttachedGrid.IsGridAttached(m_block.CubeGrid, grid, AttachedGrid.AttachmentKind.Physics))
					return;

				ExtensionsRelations.Relations relations = m_block.getRelationsTo(seen.Entity, ExtensionsRelations.Relations.Enemy).highestPriority();
				m_sortableList.Add(new sortableLastSeen(myPos, seen, relations, m_options));
				//Log.DebugLog("item: " + seen.Entity.getBestName() + ", relations: " + relations, "Display()");
			});
		}

		private void SelectLastSeen(RelayStorage store, List<long> entityIds)
		{
			Vector3D myPos = m_block.GetPosition();

			foreach (long id in entityIds)
			{
				LastSeen seen;
				if (store.TryGetLastSeen(id, out seen))
				{
					ExtensionsRelations.Relations relations = m_block.getRelationsTo(seen.Entity, ExtensionsRelations.Relations.Enemy).highestPriority();
					m_sortableList.Add(new sortableLastSeen(myPos, seen, relations, m_options));
					//Log.DebugLog("item: " + seen.Entity.getBestName() + ", relations: " + relations, "Display_FromProgram()");
				}
			}
		}

		private void DisplyAutopilotStatus()
		{
			//Log.DebugLog("Building autopilot list", "DisplyAutopilotStatus()", Logger.severity.TRACE);

			RelayStorage store = m_networkClient.GetStorage();
			if (store == null)
			{
				m_textPanel.WritePublicText("No network connection");
				return;
			}

			List<SortableAutopilot> autopilots = ResourcePool<List<SortableAutopilot>>.Get();
			Vector3D mypos = m_block.GetPosition();

			Registrar.ForEach<AutopilotTerminal>(ap => {
				IRelayPart relayPart;
				if (RelayClient.TryGetRelayPart((IMyCubeBlock)ap.m_block, out relayPart) && relayPart.GetStorage() == store && m_block.canControlBlock(ap.m_block))
					autopilots.Add(new SortableAutopilot(ap, mypos));
			});

			autopilots.Sort();

			StringBuilder displayText = new StringBuilder();
			displayText.Append(timeString);
			displayText.Append(DateTime.Now.ToLongTimeString());
			displayText.AppendLine();
			int count = 0;
			foreach (SortableAutopilot ap in autopilots)
			{
				displayText.Append(tab);
				displayText.Append(ap.Autopilot.m_block.CubeGrid.DisplayName);
				displayText.Append(tab);
				displayText.Append(PrettySI.makePretty(ap.Distance));
				displayText.AppendLine("m");

				ap.Autopilot.AppendingCustomInfo(displayText);

				displayText.AppendLine();

				count++;
				if (count >= 5)
					break;
			}

			autopilots.Clear();
			ResourcePool<List<SortableAutopilot>>.Return(autopilots);

			string displayString = displayText.ToString();

			//Log.DebugLog("Writing to panel: " + m_textPanel.DisplayNameText + ":\n\t" + displayString, "DisplyAutopilotStatus()", Logger.severity.TRACE);
			m_textPanel.WritePublicText(displayText.ToString());
		}

		private class sortableLastSeen : IComparable<sortableLastSeen>
		{
			private readonly ExtensionsRelations.Relations relations;
			private readonly double distance;
			private readonly int seconds;
			private readonly LastSeen seen;
			private readonly Vector3D predictedPos;
			private readonly Option options;

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
				string bestName = friendly ? seen.Entity.getBestName() : seen.HostileName();

				builder.Append(relations);
				builder.Append(tab);
				builder.Append(seen.Type);
				builder.Append(tab);
				//if (friendly)
				//{
				builder.Append(bestName);
				builder.Append(tab);
				//}
				if (!friendly)
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
				if (seen.RadarInfoIsRecent())
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

		private class SortableAutopilot : IComparable<SortableAutopilot>
		{

			private float? distance;

			public readonly AutopilotTerminal Autopilot;
			public readonly float DistanceSquared;

			public float Distance
			{
				get
				{
					if (!distance.HasValue)
						distance = (float)Math.Sqrt(DistanceSquared);
					return distance.Value;
				}
			}

			public SortableAutopilot(AutopilotTerminal autopilot, Vector3D mypos)
			{
				this.distance = null;
				this.Autopilot = autopilot;
				this.DistanceSquared = (float)Vector3D.DistanceSquared(autopilot.m_block.GetPosition(), mypos);
			}

			public int CompareTo(SortableAutopilot other)
			{
				if (this.Autopilot.m_autopilotStatus == other.Autopilot.m_autopilotStatus)
				{
					Logger.DebugLog("same status: " + this.Autopilot.m_autopilotStatus);
					return this.DistanceSquared.CompareTo(other.DistanceSquared);
				}

				Logger.DebugLog("diff status: " + this.Autopilot.m_autopilotStatus + " vs " + other.Autopilot.m_autopilotStatus + ", diff: " + ((int)other.Autopilot.m_autopilotStatus - (int)this.Autopilot.m_autopilotStatus));
				return (int)other.Autopilot.m_autopilotStatus - (int)this.Autopilot.m_autopilotStatus;
			}

		}

	}
}
