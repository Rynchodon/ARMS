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
		// TODO: make decisions based on text panel's name (consistency), set the public title

		private const string publicTitle_forPlayer = "Grid found by Autopilot";
		private const string publicTitle_forProgram = "Autopilot to Program";
		private const string radarIconId = "Radar";

		private IMyCubeBlock myCubeBlock;
		private Ingame.IMyTextPanel myTextPanel;
		private Logger myLogger = new Logger(null, "TextPanel");

		private Receiver myAntenna;

		protected override void DelayedInit()
		{
			myCubeBlock = Entity as IMyCubeBlock;
			myTextPanel = Entity as Ingame.IMyTextPanel;
			myLogger = new Logger(myCubeBlock.CubeGrid.DisplayName, "TextPanel", myCubeBlock.DisplayNameText);
			EnforcedUpdate = MyEntityUpdateEnum.EACH_100TH_FRAME;
		}

		public override void Close()
		{
			base.Close();
			myCubeBlock = null;
		}

		public override void UpdateAfterSimulation100()
		{
			if (informPlayer())
				return;
			// informProgBlock()
		}

		/// <summary>
		/// Search for an attached antenna, if we do not have one.
		/// </summary>
		/// <returns>true iff current antenna is valid or one was found</returns>
		private bool searchForAntenna()
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

		/// <summary>
		/// If public title matches publicTitle_forPlayer, output information to public text for player review.
		/// </summary>
		/// <returns>false if the title is not set to publicTitle_forPlayer</returns>
		private bool informPlayer()
		{
			if (!myTextPanel.GetPublicTitle().looseContains(publicTitle_forPlayer))
				return false;

			if (!searchForAntenna())
				return true;

			IEnumerator<LastSeen> toDisplay = myAntenna.getLastSeenEnum();

			myLogger.debugLog("building display list", "informPlayer()", Logger.severity.TRACE);
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
			foreach (sortableLastSeen sortable in sortableSeen)
			{
				displayText.Append(sortable.ToStringBuilder(count++));
				if (count >= 50)
					break;
			}

			string displayString = displayText.ToString();

			myLogger.debugLog("writing to panel " + myTextPanel.DisplayNameText, "findTextPanel()", Logger.severity.TRACE);
			myTextPanel.WritePublicText(displayText.ToString());

			return true;
		}

		private class sortableLastSeen : IComparable<sortableLastSeen>
		{
			private readonly IMyCubeBlockExtensions.Relations relations;
			private readonly int distance;
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
				distance = (int)(predictedPos - myPos).Length();
				seconds = (int)sinceLastSeen.TotalSeconds;
			}

			public StringBuilder ToStringBuilder(int count)
			{
				string time = (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
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
				builder.Append(distance);
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
				builder.Append(':');
				builder.Append((int)predictedPos.X);
				builder.Append(':');
				builder.Append((int)predictedPos.Y);
				builder.Append(':');
				builder.Append((int)predictedPos.Z);
				builder.Append(":\n");

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
