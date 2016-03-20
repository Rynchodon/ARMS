using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Settings;
using Rynchodon.Update;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
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

		private class GpsData
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

		private readonly Dictionary<UserSettings.ByteSettingName, GpsData> Data = new Dictionary<UserSettings.ByteSettingName, GpsData>();

		private NetworkNode m_node;
		private byte m_updateInterval;

		public Player()
		{
			myLogger = new Logger(GetType().Name, () => myPlayer.DisplayName) { MinimumLevel = Logger.severity.INFO };

			Data.Add(UserSettings.ByteSettingName.EnemiesOnHUD, new GpsData());
			Data.Add(UserSettings.ByteSettingName.NeutralOnHUD, new GpsData());
			Data.Add(UserSettings.ByteSettingName.FactionOnHUD, new GpsData());
			Data.Add(UserSettings.ByteSettingName.OwnerOnHUD, new GpsData());
			Data.Add(UserSettings.ByteSettingName.MissileOnHUD, new GpsData());

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

			m_updateInterval = UserSettings.GetSetting(UserSettings.ByteSettingName.UpdateIntervalHUD);
			UpdateManager.Register(m_updateInterval, UpdateGPS);

			myLogger.debugLog("initialized, player id: " + myPlayer.PlayerID + ", identity id: " + myPlayer.IdentityId, "Player()", Logger.severity.DEBUG);
		}

		private void UpdateGPS()
		{
			byte newInterval = UserSettings.GetSetting(UserSettings.ByteSettingName.UpdateIntervalHUD);
			if (newInterval != m_updateInterval)
			{
				myLogger.debugLog("Update interval changed from " + m_updateInterval + " to " + newInterval, "UpdateGPS()", Logger.severity.DEBUG);

				UpdateManager.Unregister(m_updateInterval, UpdateGPS);
				UpdateManager.Register(newInterval, UpdateGPS);
				m_updateInterval = newInterval;
			}

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

			foreach (var pair in Data)
			{
				pair.Value.MaxOnHUD = UserSettings.GetSetting(pair.Key);
				pair.Value.Prepare();
			}

			m_node.Storage.ForEachLastSeen((LastSeen seen) => {
				if (!seen.isRecent())
					return;

				if (seen.isRecent_Broadcast())
				{
					//myLogger.debugLog("already visible: " + seen.Entity.getBestName(), "UpdateGPS()");
					return;
				}

				UserSettings.ByteSettingName setting;
				if (!CanDisplay(seen, out setting))
					return;

				GpsData relateData;
				if (!Data.TryGetValue(setting, out relateData))
				{
					myLogger.debugLog("failed to get setting data, setting: " + setting, "UpdateGPS()", Logger.severity.WARNING);
					return;
				}

				if (relateData.MaxOnHUD == 0)
					return;

				float distance = Vector3.DistanceSquared(myPosition, seen.GetPosition());
				relateData.distanceSeen.Add(new DistanceSeen(distance, seen));
			});

			foreach (var pair in Data)
				UpdateGPS(pair.Key, pair.Value);
		}

		private void UpdateGPS(UserSettings.ByteSettingName setting, GpsData relateData)
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

				// we do not display if it is broadcasting so there is no reason to use LastSeen.HostileName()
				string name;
				switch (setting)
				{
					case UserSettings.ByteSettingName.FactionOnHUD:
					case UserSettings.ByteSettingName.OwnerOnHUD:
						name = seen.Entity.DisplayName;
						break;
					case UserSettings.ByteSettingName.MissileOnHUD:
						name = "Missile " + index;
						break;
					case UserSettings.ByteSettingName.NeutralOnHUD:
						name = "Neutral " + index;
						break;
					case UserSettings.ByteSettingName.EnemiesOnHUD:
						name = "Enemy " + index;
						break;
					default:
						myLogger.alwaysLog("case not implemented: " + setting, "UpdateGPS()", Logger.severity.ERROR);
						continue;
				}

				string description = GetDescription(seen);
				Vector3D coords = seen.GetPosition();

				// cheat the position a little to avoid clashes
				double cheat = 0.001 / (double)(index + 1);
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
					relateData.activeGPS[index].UpdateHash(); // necessary if there are to be further modifications
					MyAPIGateway.Session.GPS.AddLocalGps(relateData.activeGPS[index]);
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

		private bool CanDisplay(LastSeen seen, out UserSettings.ByteSettingName settingName)
		{
			switch (seen.Type)
			{
				case LastSeen.EntityType.Character:
				case LastSeen.EntityType.Grid:
					switch (myPlayer.PlayerID.getRelationsTo(seen.Entity))
					{
						case ExtensionsRelations.Relations.Enemy:
							settingName = UserSettings.ByteSettingName.EnemiesOnHUD;
							return true;
						case ExtensionsRelations.Relations.Neutral:
							settingName = UserSettings.ByteSettingName.NeutralOnHUD;
							return true;
						case ExtensionsRelations.Relations.Faction:
							settingName = UserSettings.ByteSettingName.FactionOnHUD;
							return true;
						case ExtensionsRelations.Relations.Owner:
							settingName = UserSettings.ByteSettingName.OwnerOnHUD;
							return true;
						default:
							settingName = UserSettings.ByteSettingName.EnemiesOnHUD;
							return false;
					}
				case LastSeen.EntityType.Missile:
					settingName = UserSettings.ByteSettingName.MissileOnHUD;
					return true;
				default:
					settingName = UserSettings.ByteSettingName.EnemiesOnHUD;
					return false;
			}
		}

	}
}
