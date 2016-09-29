using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Settings;
using Rynchodon.Update;
using Rynchodon.Weapons;
using Rynchodon.Weapons.Guided;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.Gui;
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
			public readonly List<MyEntity> entities = new List<MyEntity>();
			/// <summary>The maximum GPS entries that are allowed</summary>
			public byte MaxOnHUD;

			public void Prepare()
			{
				distanceSeen.Clear();
				while (entities.Count < MaxOnHUD)
					entities.Add(null);
			}
		}

		private const string descrEnd = "Autopilot Detected";
		private const string WarningIncomingMissile = "INCOMING MISSILE";

		private static readonly MySoundPair m_sound = new MySoundPair("MissileLockOnSound");

		private IMyPlayer myPlayer { get { return MyAPIGateway.Session.Player; } }

		private readonly Logger myLogger;

		private readonly Dictionary<UserSettings.ByteSettingName, GpsData> Data = new Dictionary<UserSettings.ByteSettingName, GpsData>();
		private readonly HashSet<MyEntity> m_updated = new HashSet<MyEntity>();

		private IMyEntity m_controlled;
		private Func<RelayStorage> m_storage;

		private byte m_updateIntervalGPS;

		private IMyHudNotification m_missileWarning = MyAPIGateway.Utilities.CreateNotification(WarningIncomingMissile, (int)(20000f * Globals.UpdateDuration), VRage.Game.MyFontEnum.Red);
		private MyEntity3DSoundEmitter m_soundEmitter;
		private GuidedMissile m_threat;
		private ulong m_nextBeep;

		public Player()
		{
			myLogger = new Logger(() => myPlayer.DisplayName) { MinimumLevel = Logger.severity.TRACE };

			Data.Add(UserSettings.ByteSettingName.EnemiesOnHUD, new GpsData());
			Data.Add(UserSettings.ByteSettingName.NeutralOnHUD, new GpsData());
			Data.Add(UserSettings.ByteSettingName.FactionOnHUD, new GpsData());
			Data.Add(UserSettings.ByteSettingName.OwnerOnHUD, new GpsData());
			Data.Add(UserSettings.ByteSettingName.MissileOnHUD, new GpsData());

			// cleanup old GPS
			List<IMyGps> list = MyAPIGateway.Session.GPS.GetGpsList(myPlayer.IdentityId);
			if (list != null)
			{
				myLogger.debugLog("# of gps: " + list.Count);
				foreach (IMyGps gps in list)
					if (gps.Description != null && gps.Description.EndsWith(descrEnd))
					{
						myLogger.debugLog("old gps: " + gps.Name + ", " + gps.Coords);
						MyAPIGateway.Session.GPS.RemoveLocalGps(gps);
					}
			}

			m_updateIntervalGPS = UserSettings.GetSetting(UserSettings.ByteSettingName.UpdateIntervalHUD);
			UpdateManager.Register(m_updateIntervalGPS, UpdateGPS);
			UpdateManager.Register(100, Update);
			UpdateManager.Register(30, MissileHudWarning);
			UpdateManager.Register(1, MissileAudioWarning);

			myLogger.debugLog("initialized, player id: " + myPlayer.PlayerID + ", identity id: " + myPlayer.IdentityId, Logger.severity.DEBUG);
		}

		private void Update()
		{
			IMyEntity controlled = myPlayer.Controller.ControlledEntity as IMyEntity;

			if (controlled != m_controlled)
			{
				m_controlled = controlled;

				if (controlled is IMyCharacter)
				{
					m_soundEmitter = null;
					m_threat = null;
					RelayNode charNode;
					if (!Registrar.TryGetValue(controlled, out charNode))
					{
						myLogger.debugLog("Failed to get node for character: " + controlled.getBestName(), Logger.severity.WARNING);
						m_storage = null;
						return;
					}
					m_storage = () => charNode.Storage;
					myLogger.debugLog("now controlling a character", Logger.severity.DEBUG);
				}
				else if (controlled is IMyCubeBlock)
				{
					IRelayPart shipClient = RelayClient.GetOrCreateRelayPart((IMyCubeBlock)controlled);
					m_storage = shipClient.GetStorage;
					m_soundEmitter = new MyEntity3DSoundEmitter((MyEntity)controlled);
					myLogger.debugLog("now controlling a ship", Logger.severity.DEBUG);
				}
				else
				{
					myLogger.debugLog("player controlling incompatible entity: " + controlled.getBestName(), Logger.severity.TRACE);
					m_storage = null;
					m_soundEmitter = null;
					m_threat = null;
					return;
				}
			}
			else if (m_storage == null || m_storage.Invoke() == null)
			{
				myLogger.debugLog("no storage", Logger.severity.TRACE);
				m_threat = null;
				return;
			}

			if (UserSettings.GetSetting(UserSettings.BoolSettingName.MissileWarning))
				UpdateMissileThreat();
			else
				m_threat = null;
		}

		#region GPS

		private void UpdateGPS()
		{
			byte newInterval = UserSettings.GetSetting(UserSettings.ByteSettingName.UpdateIntervalHUD);
			if (newInterval != m_updateIntervalGPS)
			{
				myLogger.debugLog("Update interval changed from " + m_updateIntervalGPS + " to " + newInterval, Logger.severity.DEBUG);

				UpdateManager.Unregister(m_updateIntervalGPS, UpdateGPS);
				UpdateManager.Register(newInterval, UpdateGPS);
				m_updateIntervalGPS = newInterval;
			}

			if (m_storage == null)
			{
				myLogger.debugLog("no storage getter");
				return;
			}

			RelayStorage store = m_storage.Invoke();

			if (store == null)
			{
				myLogger.debugLog("no storage");
				return;
			}

			if (store.LastSeenCount == 0)
			{
				myLogger.debugLog("No LastSeen");
				return;
			}

			Vector3D myPosition = m_controlled.GetPosition();

			foreach (var pair in Data)
			{
				pair.Value.MaxOnHUD = UserSettings.GetSetting(pair.Key);
				pair.Value.Prepare();
			}

			store.ForEachLastSeen((LastSeen seen) => {
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
					myLogger.debugLog("failed to get setting data, setting: " + setting, Logger.severity.WARNING);
					return;
				}

				if (relateData.MaxOnHUD == 0)
					return;

				float distance = Vector3.DistanceSquared(myPosition, seen.GetPosition());
				relateData.distanceSeen.Add(new DistanceSeen(distance, seen));
			});

			m_updated.Clear(); 
			
			foreach (var pair in Data)
				UpdateGPS(pair.Key, pair.Value);

			m_updated.Clear();
		}

		private void UpdateGPS(UserSettings.ByteSettingName setting, GpsData relateData)
		{
			relateData.distanceSeen.Sort();

			int index;
			for (index = 0; index < relateData.distanceSeen.Count && index < relateData.entities.Count; index++)
			{
				myLogger.debugLog("index(" + index + ") >= relateData.distanceSeen.Count(" + relateData.distanceSeen.Count + ")", Logger.severity.FATAL, condition: index >= relateData.distanceSeen.Count);
				LastSeen seen = relateData.distanceSeen[index].Seen;

				// we do not display if it is broadcasting so there is no reason to use LastSeen.HostileName()
				string name;
				MyRelationsBetweenPlayerAndBlock seRelate;
				switch (setting)
				{
					case UserSettings.ByteSettingName.FactionOnHUD:
						name = seen.Entity.DisplayName;
						seRelate = MyRelationsBetweenPlayerAndBlock.FactionShare;
						break;
					case UserSettings.ByteSettingName.OwnerOnHUD:
						name = seen.Entity.DisplayName;
						seRelate = MyRelationsBetweenPlayerAndBlock.Owner;
						break;
					case UserSettings.ByteSettingName.MissileOnHUD:
						name = "Missile " + index;
						seRelate = MyRelationsBetweenPlayerAndBlock.Enemies;
						break;
					case UserSettings.ByteSettingName.NeutralOnHUD:
						name = "Neutral " + index;
						seRelate = MyRelationsBetweenPlayerAndBlock.Neutral;
						break;
					case UserSettings.ByteSettingName.EnemiesOnHUD:
						name = "Enemy " + index;
						seRelate = MyRelationsBetweenPlayerAndBlock.Enemies;
						break;
					default:
						myLogger.alwaysLog("case not implemented: " + setting, Logger.severity.ERROR);
						continue;
				}

				MyEntity entity = relateData.entities[index];
				if (entity != null)
				{
					if (entity != seen.Entity)
					{
						if (!m_updated.Contains(entity))
							MyHud.LocationMarkers.UnregisterMarker(entity);
					}
					else
						continue;
				}

				entity = (MyEntity)seen.Entity;
				relateData.entities[index] = entity;
				m_updated.Add(entity);
				MyHud.LocationMarkers.RegisterMarker(entity, new MyHudEntityParams() { FlagsEnum = MyHudIndicatorFlagsEnum.SHOW_ALL, Text = new StringBuilder(name), OffsetText = true, TargetMode = seRelate });
			}

			// remove remaining
			while (index < relateData.entities.Count)
			{
				MyEntity entity = relateData.entities[index];
				if (entity != null)
				{
					myLogger.debugLog("detritus: " + entity.nameWithId());
					MyHud.LocationMarkers.UnregisterMarker(entity);
					relateData.entities[index] = null;
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

			if (seen.RadarInfoIsRecent())
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
						case ExtensionsRelations.Relations.NoOwner:
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

		#endregion GPS

		#region Missile Warning

		/// <summary>
		/// Looks for a missile threating the ship the player is controlling.
		/// </summary>
		private void UpdateMissileThreat()
		{
			if (!(m_controlled is IMyCubeBlock))
			{
				//myLogger.debugLog("not controlling a ship");
				return;
			}

			if (m_storage == null)
			{
				myLogger.debugLog("no storage getter");
				return;
			}

			RelayStorage store = m_storage.Invoke();

			if (store == null)
			{
				myLogger.debugLog("no storage");
				return;
			}

			byte timeToImpact = byte.MaxValue;
			m_threat = null;
			store.ForEachLastSeen((LastSeen seen) => {
				if (seen.Type == LastSeen.EntityType.Missile)
				{
					GuidedMissile guided;
					if (Registrar.TryGetValue(seen.Entity, out guided) && IsThreat(guided))
					{
						myLogger.debugLog("threat: " + guided.MyEntity, Logger.severity.TRACE);
						byte tti;
						if (GetTimeToImpact(guided, out tti) && tti < timeToImpact)
						{
							timeToImpact = tti;
							m_threat = guided;
						}
					}
				}
			});
		}

		/// <summary>
		/// Determine if the ship is going to be hit and get TTI
		/// </summary>
		/// <param name="guided">Missile targeting ship</param>
		/// <param name="tti">Time to Impact</param>
		/// <returns>True iff the missile is on course</returns>
		private bool GetTimeToImpact(GuidedMissile guided, out byte tti)
		{
			Vector3 displacement = m_controlled.GetPosition() - guided.MyEntity.GetPosition();
			Vector3 velocity = guided.MyEntity.Physics.LinearVelocity - m_controlled.GetTopMostParent().Physics.LinearVelocity;

			float velDotDisp = velocity.Dot(ref displacement);
			if (velDotDisp < 0f)
			{
				// missile has missed
				tti = byte.MaxValue;
				return false;
			}

			Vector3 tti_vector = displacement * displacement.LengthSquared() / (velDotDisp * displacement);

			float tti_squared = tti_vector.LengthSquared();
			if (tti_squared > 3600f)
			{
				tti = byte.MaxValue;
				return false;
			}
			tti = Convert.ToByte(Math.Sqrt(tti_squared));
			return true;
		}

		/// <summary>
		/// Warn the player of incoming missile by beeping incessantly.
		/// </summary>
		private void MissileAudioWarning()
		{
			if (Globals.UpdateCount < m_nextBeep || m_threat == null || m_threat.Stopped)
				return;

			byte tti;
			if (!GetTimeToImpact(m_threat, out tti))
			{
				m_threat = null;
				myLogger.debugLog("missile is no longer approaching", Logger.severity.DEBUG);
				return;
			}

			m_nextBeep = Globals.UpdateCount + tti;
			m_soundEmitter.PlaySound(m_sound, true);
		}

		/// <summary>
		/// Warn the player of incoming missile by displaying a message on the HUD.
		/// </summary>
		private void MissileHudWarning()
		{
			if (m_threat != null)
				m_missileWarning.Show();
		}

		private bool IsThreat(GuidedMissile guided)
		{
			Target guidedTarget = guided.CurrentTarget;
			return guidedTarget != null && guidedTarget.Entity != null && !guided.Stopped && guidedTarget.Entity.GetTopMostParent() == m_controlled.GetTopMostParent();
		}

		#endregion Missile Warning

	}
}
