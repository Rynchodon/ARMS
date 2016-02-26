using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Settings;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.AntennaRelay
{
	public class Player
	{

		private struct DistanceSeen : IComparable<DistanceSeen>
		{
			public readonly float Distance;
			public readonly LastSeen Seen;

			public DistanceSeen(float distance, LastSeen seen)
			{
				this.Distance = distance;
				this.Seen = seen;
			}

			public int CompareTo(DistanceSeen other)
			{
				return Math.Sign(this.Distance - other.Distance);
			}
		}

		private class ForRelations
		{
			/// <summary>LastSeen sorted by distance squared</summary>
			public readonly List<DistanceSeen> distanceSeen = new List<DistanceSeen>();
			/// <summary>GPS entries that are currently being used</summary>
			public readonly List<IMyGps> activeGPS = new List<IMyGps>();
			/// <summary>The maximum GPS entries that are allowed</summary>
			public byte MaxOnHUD;

			public void Prepare()
			{
				distanceSeen.Clear();
				while (activeGPS.Count < MaxOnHUD)
					activeGPS.Add(null);
			}
		}

		private const string descrEnd = "Autopilot Detected";

		private IMyPlayer myPlayer { get { return MyAPIGateway.Session.Player; } }

		private readonly Logger myLogger;

		private readonly Dictionary<ExtensionsRelations.Relations, ForRelations> Data = new Dictionary<ExtensionsRelations.Relations, ForRelations>();

		private NetworkNode m_node;

		public Player()
		{
			myLogger = new Logger(GetType().Name, () => myPlayer.DisplayName);

			Data.Add(ExtensionsRelations.Relations.Enemy, new ForRelations());
			Data.Add(ExtensionsRelations.Relations.Neutral, new ForRelations());
			Data.Add(ExtensionsRelations.Relations.Faction, new ForRelations());
			Data.Add(ExtensionsRelations.Relations.Owner, new ForRelations());

			// cleanup old GPS
			List<IMyGps> list = MyAPIGateway.Session.GPS.GetGpsList(myPlayer.IdentityId);
			if (list != null)
			{
				myLogger.debugLog("# of gps: " + list.Count, "Player()");
				foreach (IMyGps gps in list)
					if (gps.Description != null && gps.Description.EndsWith(descrEnd))
					{
						myLogger.debugLog("old gps: " + gps.Name + ", " + gps.Coords, "player()");
						MyAPIGateway.Session.GPS.RemoveLocalGps(gps);
					}
			}

			myLogger.debugLog("initialized, player id: " + myPlayer.PlayerID + ", identity id: " + myPlayer.IdentityId, "Player()", Logger.severity.DEBUG);
		}

		public void Update100()
		{
			UpdateGPS();
		}

		private void UpdateGPS()
		{
			IMyEntity character = myPlayer.Controller.ControlledEntity as IMyEntity;
			if (!(character is IMyCharacter))
			{
				myLogger.debugLog("Not controlling a character", "UpdateGPS()");
				return;
			}
			if (m_node == null)
			{
				if (!Registrar.TryGetValue(character.EntityId, out m_node))
				{
					myLogger.debugLog("Failed to get node", "UpdateGPS()", Logger.severity.WARNING);
					return;
				}
			}

			if (m_node.Storage == null || m_node.Storage.LastSeenCount == 0)
			{
				myLogger.debugLog("No LastSeen", "UpdateGPS()");
				return;
			}

			Vector3D myPosition = character.GetPosition();

			Data[ExtensionsRelations.Relations.Enemy].MaxOnHUD = UserSettings.GetSetting(UserSettings.ByteSettingName.EnemiesOnHUD);
			Data[ExtensionsRelations.Relations.Neutral].MaxOnHUD = UserSettings.GetSetting(UserSettings.ByteSettingName.NeutralOnHUD);
			Data[ExtensionsRelations.Relations.Faction].MaxOnHUD = UserSettings.GetSetting(UserSettings.ByteSettingName.FactionOnHUD);
			Data[ExtensionsRelations.Relations.Owner].MaxOnHUD = UserSettings.GetSetting(UserSettings.ByteSettingName.OwnerOnHUD);

			foreach (var value in Data.Values)
				value.Prepare();

			m_node.Storage.ForEachLastSeen((LastSeen seen) => {
				if (!seen.isRecent())
					return;

				if (seen.isRecent_Broadcast())
				{
					//myLogger.debugLog("already visible: " + seen.Entity.getBestName(), "UpdateGPS()");
					return;
				}

				if (!(seen.Entity is IMyCharacter || seen.Entity is IMyCubeGrid))
					return;

				ExtensionsRelations.Relations relate = myPlayer.PlayerID.getRelationsTo(seen.Entity, ExtensionsRelations.Relations.Enemy);

				ForRelations relateData;
				if (!Data.TryGetValue(relate, out relateData))
				{
					myLogger.debugLog("failed to get relate data, relations: " + relate, "UpdateGPS()", Logger.severity.WARNING);
					return;
				}

				if (relateData.MaxOnHUD == 0)
					return;

				float distance = Vector3.DistanceSquared(myPosition, seen.GetPosition());
				relateData.distanceSeen.Add(new DistanceSeen(distance, seen));

				myLogger.debugLog("added to distanceSeen[" + relate + "]: " + distance + ", " + seen.Entity.getBestName(), "UpdateGPS()", Logger.severity.DEBUG);
			});

			foreach (var pair in Data)
				UpdateGPS(pair.Key, pair.Value);
		}

		private void UpdateGPS(ExtensionsRelations.Relations relate, ForRelations relateData)
		{
			//myLogger.debugLog("entered UpdateGPS(" + relate + ", " + relateData + ")", "UpdateGPS()");

			//myLogger.debugLog("relate: " + relate + ", count: " + relateData.distanceSeen.Count, "UpdateGPS()");

			relateData.distanceSeen.Sort();

			int index;
			for (index = 0; index < relateData.distanceSeen.Count && index < relateData.activeGPS.Count; index++)
			{
				//myLogger.debugLog("getting seen...", "UpdateGPS()");
				myLogger.debugLog(index >= relateData.distanceSeen.Count, "index(" + index + ") >= relateData.distanceSeen.Count(" + relateData.distanceSeen.Count + ")", "UpdateGPS()", Logger.severity.FATAL);
				LastSeen seen = relateData.distanceSeen[index].Seen;

				//myLogger.debugLog("relate: " + relate + ", index: " + index + ", entity: " + seen.Entity.getBestName(), "UpdateGPS()");

				string name;
				switch (relate)
				{
					case ExtensionsRelations.Relations.Faction:
					case ExtensionsRelations.Relations.Owner:
						name = seen.Entity.DisplayName;
						break;
					default:
						name = relate.ToString() + ' ' + index;
						break;
				}

				string description = GetDescription(seen);
				Vector3D coords = seen.GetPosition();

				// cheat the position a little to avoid clashes
				double cheat = 0.001 / (double)(index+1);
				coords.ApplyOperation((d) => { return d + cheat; }, out coords);

				//myLogger.debugLog("checking gps is null", "UpdateGPS()");
				//myLogger.debugLog("index >= relateData.activeGPS.Count: " + (index >= relateData.activeGPS.Count), "UpdateGPS()");
				myLogger.debugLog(index >= relateData.activeGPS.Count, "index(" + index + ") >= relateData.activeGPS.Count(" + relateData.activeGPS.Count + ")", "UpdateGPS()", Logger.severity.FATAL);
				if (relateData.activeGPS[index] == null)
				{
					relateData.activeGPS[index] = MyAPIGateway.Session.GPS.Create(name, description, coords, true, true);
					myLogger.debugLog("adding new GPS " + index + ", entity: " + seen.Entity.getBestName() + ", hash: " + relateData.activeGPS[index].Hash, "UpdateGPS()");
					MyAPIGateway.Session.GPS.AddLocalGps(relateData.activeGPS[index]);
				}
				else if (Update(relateData.activeGPS[index], name, description, coords))
				{
					myLogger.debugLog("updating GPS " + index + ", entity: " + seen.Entity.getBestName() + ", hash: " + relateData.activeGPS[index].Hash, "UpdateGPS()");
					MyAPIGateway.Session.GPS.RemoveLocalGps(relateData.activeGPS[index]);
					MyAPIGateway.Session.GPS.AddLocalGps(relateData.activeGPS[index]);
					relateData.activeGPS[index].UpdateHash(); // necessary if there are to be further modifications
				}
				//else
				//	myLogger.debugLog("no need to update GPS " + index + ", entity: " + seen.Entity.getBestName() + ", hash: " + relateData.activeGPS[index].Hash, "UpdateGPS()");
			}

			// for remaining GPS, remove
			while (index < relateData.activeGPS.Count)
			{
				if (relateData.activeGPS[index] != null)
				{
					myLogger.debugLog("removing GPS " + index + ", name: " + relateData.activeGPS[index].Name + ", coords: " + relateData.activeGPS[index].Coords, "UpdateGPS()");
					MyAPIGateway.Session.GPS.RemoveLocalGps(relateData.activeGPS[index]);
					relateData.activeGPS[index] = null;
				}

				index++;
			}
		}

		private StringBuilder m_descrParts = new StringBuilder();

		private string GetDescription(LastSeen seen)
		{
			m_descrParts.Clear();

			if (seen.isRecent_Radar())
				m_descrParts.Append("Has Radar, ");

			if (seen.isRecent_Jam())
				m_descrParts.Append("Has Jammer, ");

			m_descrParts.Append("ID:");
			m_descrParts.Append(seen.Entity.EntityId);
			m_descrParts.Append(", ");

			if (seen.Info != null)
			{
				m_descrParts.Append(seen.Info.Pretty_Volume());
				m_descrParts.Append(", ");
			}

			m_descrParts.Append(descrEnd);

			return m_descrParts.ToString();
		}

		private bool Update(IMyGps gps, string name, string description, Vector3D coords)
		{
			bool updated = false;

			if (gps.Name != name && name != null)
			{
				gps.Name = name;
				updated = true;
			}
			if (gps.Description != description)
			{
				gps.Description = description;
				updated = true;
			}
			if (gps.Coords != coords)
			{
				gps.Coords = coords;
				updated = true;
			}
			if (!gps.ShowOnHud)
			{
				gps.ShowOnHud = true;
				updated = true;
			}

			return updated;
		}

	}
}
