using System; // partial
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot;
using Rynchodon.Utility;
using Rynchodon.Utility.Network;
using Rynchodon.Weapons;
using Rynchodon.Weapons.SystemDisruption;
using Sandbox.ModAPI;
using VRage.Collections;

namespace Rynchodon.Update
{
	/// <summary>
	/// Saves/loads persistent data to/from a save file.
	/// </summary>
	/// TODO: client saving
	public class Saver
	{

		[Serializable]
		public class Builder_ArmsData
		{
			[XmlAttribute]
			public long SaveTime;
			public Version ArmsVersion;
			public RelayStorage.Builder_NetworkStorage[] AntennaStorage;
			public Disruption.Builder_Disruption[] SystemDisruption;
			public ShipAutopilot.Builder_Autopilot[] Autopilot;
			public ASync.SyncBuilder Sync;

#pragma warning disable CS0649
			[XmlAttribute]
			public int ModVersion;
			public ProgrammableBlock.Builder_ProgrammableBlock[] ProgrammableBlock;
			public TextPanel.Builder_TextPanel[] TextPanel;
			public WeaponTargeting.Builder_WeaponTargeting[] Weapon;
			public UpgradeEntityValue.Builder_EntityValues[] EntityValues;
#pragma warning restore CS0649
		}

		private const string SaveIdString = "ARMS save file id", SaveXml = "ARMS save XML data";

		private static Saver Instance { get; set; }

		[AfterArmsInit]
		private static void OnLoad()
		{
			Instance = new Saver();
		}

		[OnWorldClose]
		private static void Unload()
		{
			Instance = null;
		}

		/// <summary>LastSeen that were not added immediately upon world loading, Saver will keep trying to add them.</summary>
		private CachingDictionary<long, CachingList<LastSeen.Builder_LastSeen>> m_failedLastSeen;
		private FileMaster m_fileMaster;

		private Saver()
		{
			DoLoad(GetData());
		}

		private Builder_ArmsData GetData()
		{
			m_fileMaster = new FileMaster("SaveDataMaster.txt", "SaveData - ", int.MaxValue);

			Builder_ArmsData data = null;

			string serialized;
			if (MyAPIGateway.Utilities.GetVariable(SaveXml, out serialized))
			{
				data = MyAPIGateway.Utilities.SerializeFromXML<Builder_ArmsData>(serialized);
				if (data != null)
				{
					Logger.DebugLog("ARMS data was imbeded in the save file proper", Rynchodon.Logger.severity.DEBUG);
					return data;
				}
			}

			string identifier = LegacyIdentifier(true);
			if (identifier == null)
			{
				Logger.DebugLog("no identifier");
				return data;
			}

			var reader = m_fileMaster.GetTextReader(identifier);
			if (reader != null)
			{
				Logger.DebugLog("loading from file: " + identifier);
				data = MyAPIGateway.Utilities.SerializeFromXML<Builder_ArmsData>(reader.ReadToEnd());
				reader.Close();
			}
			else
				Logger.AlwaysLog("Failed to open file reader for " + identifier);

			return data;
		}

		/// <summary>
		/// Load data from a file. Shall be called after mod is fully initialized.
		/// </summary>
		private void DoLoad(Builder_ArmsData data)
		{
			try
			{
				LoadSaveData(data);
			}
			catch (Exception ex)
			{
				Logger.AlwaysLog("Exception: " + ex, Rynchodon.Logger.severity.ERROR);
				Rynchodon.Logger.Notify("ARMS: failed to load data", 60000, Rynchodon.Logger.severity.ERROR);
			}
		}

		private void RetryLastSeen()
		{
			foreach (KeyValuePair<long, CachingList<LastSeen.Builder_LastSeen>> storageLastSeen in m_failedLastSeen)
			{
				RelayNode node;
				if (Registrar.TryGetValue(storageLastSeen.Key, out node))
				{
					RelayStorage store = node.Storage;
					foreach (LastSeen.Builder_LastSeen builder in storageLastSeen.Value)
					{
						if (MyAPIGateway.Entities.EntityExists(builder.EntityId))
						{
							LastSeen ls = new LastSeen(builder);
							if (ls.IsValid)
							{
								Logger.DebugLog("Successfully created a LastSeen. Primary node: " + storageLastSeen.Key + ", entity: " + ls.Entity.nameWithId());
								storageLastSeen.Value.Remove(builder);
							}
							else
								Logger.AlwaysLog("Unknown failure with last seen", Rynchodon.Logger.severity.ERROR);
						}
						else
							Logger.DebugLog("Not yet available: " + builder.EntityId);
					}
					storageLastSeen.Value.ApplyRemovals();
					if (storageLastSeen.Value.Count == 0)
					{
						Logger.DebugLog("Finished with: " + storageLastSeen.Key, Rynchodon.Logger.severity.DEBUG);
						m_failedLastSeen.Remove(storageLastSeen.Key);
					}
					else
						Logger.DebugLog("For " + storageLastSeen.Key + ", " + storageLastSeen.Value.Count + " builders remain");
				}
				else
					Logger.DebugLog("Failed to get node for " + storageLastSeen.Key, Rynchodon.Logger.severity.WARNING);
			}
			m_failedLastSeen.ApplyRemovals();

			if (m_failedLastSeen.Count() == 0)
			{
				Logger.DebugLog("All LastSeen have been successfully added", Rynchodon.Logger.severity.INFO);
				m_failedLastSeen = null;
				UpdateManager.Unregister(100, RetryLastSeen);
			}
			else
			{
				Logger.DebugLog(m_failedLastSeen.Count() + " primary nodes still have last seen to be added");

				if (Globals.UpdateCount >= 3600)
				{
					foreach (KeyValuePair<long, CachingList<LastSeen.Builder_LastSeen>> storageLastSeen in m_failedLastSeen)
						foreach (LastSeen.Builder_LastSeen builder in storageLastSeen.Value)
							Logger.AlwaysLog("Failed to add last seen to world. Primary node: " + storageLastSeen.Key + ", entity ID: " + builder.EntityId, Rynchodon.Logger.severity.WARNING);
					m_failedLastSeen = null;
					UpdateManager.Unregister(100, RetryLastSeen);
				}
			}
		}

		private string LegacyIdentifier(bool loading)
		{
			string path = MyAPIGateway.Session.CurrentPath;
			string saveId_fromPath = path.Substring(path.LastIndexOfAny(new char[] { '/', '\\' }) + 1) + ".xml";
			string saveId_fromWorld;

			string saveId = null;
			if (m_fileMaster.FileExists(saveId_fromPath))
				saveId = saveId_fromPath;

			// if file from path exists, it should match stored value
			if (saveId != null)
			{
				if (!MyAPIGateway.Utilities.GetVariable(SaveIdString, out saveId_fromWorld))
				{
					Logger.AlwaysLog("Save exists for path but save id could not be retrieved from world. From path: " + saveId_fromPath, Rynchodon.Logger.severity.ERROR);
				}
				else if (saveId_fromPath != saveId_fromWorld)
				{
					Logger.AlwaysLog("Save id from path does not match save id from world. From path: " + saveId_fromPath + ", from world: " + saveId_fromWorld, Rynchodon.Logger.severity.ERROR);

					// prefer from world
					if (m_fileMaster.FileExists(saveId_fromWorld))
						saveId = saveId_fromWorld;
					else
						Logger.AlwaysLog("Save id from world does not match a save. From world: " + saveId_fromWorld, Rynchodon.Logger.severity.ERROR);
				}
			}
			else
			{
				if (MyAPIGateway.Utilities.GetVariable(SaveIdString, out saveId_fromWorld))
				{
					if (m_fileMaster.FileExists(saveId_fromWorld))
					{
						if (loading)
							Logger.AlwaysLog("Save is a copy, loading from old world: " + saveId_fromWorld, Rynchodon.Logger.severity.DEBUG);
						saveId = saveId_fromWorld;
					}
					else
					{
						if (loading)
							Logger.AlwaysLog("Cannot load world, save id does not match any save: " + saveId_fromWorld, Rynchodon.Logger.severity.DEBUG);
						return null;
					}
				}
				else
				{
					if (loading)
						Logger.AlwaysLog("Cannot load world, no save id found", Rynchodon.Logger.severity.DEBUG);
					return null;
				}
			}

			return saveId;
		}

		private void LoadSaveData(Builder_ArmsData data)
		{
			if (data == null)
			{
				Logger.DebugLog("No data to load");
				return;
			}

#pragma warning disable 612, 618
			if (Comparer<Version>.Default.Compare(data.ArmsVersion, default(Version)) == 0)
			{
				Logger.DebugLog("Old version: " + data.ModVersion);
				data.ArmsVersion = new Version(data.ModVersion);
			}
#pragma warning restore 612, 618

			Logger.AlwaysLog("Save version: " + data.ArmsVersion, Rynchodon.Logger.severity.INFO);

			// relay

			Dictionary<Message.Builder_Message, Message> messages = MyAPIGateway.Multiplayer.IsServer ? new Dictionary<Message.Builder_Message, Message>() : null;
			SerializableGameTime.Adjust = new TimeSpan(data.SaveTime);
			foreach (RelayStorage.Builder_NetworkStorage bns in data.AntennaStorage)
			{
				RelayNode node;
				if (!Registrar.TryGetValue(bns.PrimaryNode, out node))
				{
					Logger.AlwaysLog("Failed to get node for: " + bns.PrimaryNode, Rynchodon.Logger.severity.WARNING);
					continue;
				}
				RelayStorage store = node.Storage;
				if (store == null) // probably always true
				{
					node.ForceCreateStorage();
					store = node.Storage;
					if (store == null)
					{
						Logger.AlwaysLog("failed to create storage for " + node.DebugName, Rynchodon.Logger.severity.ERROR);
						continue;
					}
				}

				foreach (LastSeen.Builder_LastSeen bls in bns.LastSeenList)
				{
					LastSeen ls = new LastSeen(bls);
					if (ls.IsValid)
						store.Receive(ls);
					else
					{
						Logger.AlwaysLog("failed to create a valid last seen from builder for " + bls.EntityId, Rynchodon.Logger.severity.WARNING);
						if (m_failedLastSeen == null)
						{
							m_failedLastSeen = new CachingDictionary<long, CachingList<LastSeen.Builder_LastSeen>>();
							UpdateManager.Register(100, RetryLastSeen);
						}
						CachingList<LastSeen.Builder_LastSeen> list;
						if (!m_failedLastSeen.TryGetValue(bns.PrimaryNode, out list))
						{
							list = new CachingList<LastSeen.Builder_LastSeen>();
							m_failedLastSeen.Add(bns.PrimaryNode, list, true);
						}
						list.Add(bls);
						list.ApplyAdditions();
					}
				}

				Logger.DebugLog("added " + bns.LastSeenList.Length + " last seen to " + store.PrimaryNode.DebugName, Rynchodon.Logger.severity.DEBUG);

				// messages in the save file belong on the server
				if (messages == null)
					continue;

				foreach (Message.Builder_Message bm in bns.MessageList)
				{
					Message msg;
					if (!messages.TryGetValue(bm, out msg))
					{
						msg = new Message(bm);
						messages.Add(bm, msg);
					}
					else
					{
						Logger.DebugLog("found linked message", Rynchodon.Logger.severity.TRACE);
					}
					if (msg.IsValid)
						store.Receive(msg);
					else
						Logger.AlwaysLog("failed to create a valid message from builder for " + bm.DestCubeBlock + "/" + bm.SourceCubeBlock, Rynchodon.Logger.severity.WARNING);
				}

				Logger.DebugLog("added " + bns.MessageList.Length + " message to " + store.PrimaryNode.DebugName, Rynchodon.Logger.severity.DEBUG);
			}

			// past this point, only synchronized data
			if (!MyAPIGateway.Multiplayer.IsServer)
			{
				data = null;
				return;
			}

			// system disruption

			foreach (Disruption.Builder_Disruption bd in data.SystemDisruption)
			{
				Disruption disrupt;
				switch (bd.Type)
				{
					case "AirVentDepressurize":
						disrupt = new AirVentDepressurize();
						break;
					case "CryoChamberMurder":
						disrupt = new CryoChamberMurder();
						break;
					case "DisableTurret":
						disrupt = new DisableTurret();
						break;
					case "DoorLock":
						disrupt = new DoorLock();
						break;
					case "EMP":
						disrupt = new EMP();
						break;
					case "GravityReverse":
						disrupt = new GravityReverse();
						break;
					case "JumpDriveDrain":
						disrupt = new JumpDriveDrain();
						break;
					case "MedicalRoom":
						disrupt = new MedicalRoom();
						break;
					case "TraitorTurret":
						disrupt = new TraitorTurret();
						break;
					default:
						Logger.AlwaysLog("Unknown disruption: " + bd.Type, Rynchodon.Logger.severity.WARNING);
						continue;
				}
				disrupt.Start(bd);
			}

			// autopilot

			if (data.Autopilot != null)
				foreach (ShipAutopilot.Builder_Autopilot ba in data.Autopilot)
				{
					ShipAutopilot autopilot;
					if (Registrar.TryGetValue(ba.AutopilotBlock, out autopilot))
						autopilot.ResumeFromSave(ba);
					else
						Logger.AlwaysLog("failed to find autopilot block " + ba.AutopilotBlock, Rynchodon.Logger.severity.WARNING);
				}

			// programmable block

			if (data.ProgrammableBlock != null)
				foreach (ProgrammableBlock.Builder_ProgrammableBlock bpa in data.ProgrammableBlock)
				{
					ProgrammableBlock pb;
					if (Registrar.TryGetValue(bpa.BlockId, out pb))
						pb.ResumeFromSave(bpa);
					else
						Logger.AlwaysLog("failed to find programmable block " + bpa.BlockId, Rynchodon.Logger.severity.WARNING);
				}

			// text panel

			if (data.TextPanel != null)
				foreach (TextPanel.Builder_TextPanel btp in data.TextPanel)
				{
					TextPanel panel;
					if (Registrar.TryGetValue(btp.BlockId, out panel))
						panel.ResumeFromSave(btp);
					else
						Logger.AlwaysLog("failed to find text panel " + btp.BlockId, Rynchodon.Logger.severity.WARNING);
				}

			// weapon

			if (data.Weapon != null)
				foreach (WeaponTargeting.Builder_WeaponTargeting bwt in data.Weapon)
				{
					WeaponTargeting targeting;
					if (WeaponTargeting.TryGetWeaponTargeting(bwt.WeaponId, out targeting))
						targeting.ResumeFromSave(bwt);
					else
						Logger.AlwaysLog("failed to find weapon " + bwt.WeaponId, Rynchodon.Logger.severity.WARNING);
				}

			// entity values

			if (data.EntityValues != null)
				UpgradeEntityValue.Load(data.EntityValues);

			// sync
			
			if (data.Sync != null)
				ASync.SetBuilder(data.Sync);

			data = null;
		}

		/// <summary>
		/// Saves data to a variable.
		/// </summary>
		[OnWorldSave(Order = int.MaxValue)] // has to be last, obviously
		private static void SaveData()
		{
			if (!MyAPIGateway.Multiplayer.IsServer)
				return;

			try
			{
				// fetching data needs to happen on game thread as not every script has locks

				Builder_ArmsData data = new Builder_ArmsData();

				data.SaveTime = Globals.ElapsedTimeTicks;
				data.ArmsVersion = Settings.ServerSettings.CurrentVersion;

				// network data

				Dictionary<long, RelayStorage.Builder_NetworkStorage> storages = new Dictionary<long, RelayStorage.Builder_NetworkStorage>();

				Registrar.ForEach<RelayNode>(node => {
					if (node.Block != null && node.Storage != null && !storages.ContainsKey(node.Storage.PrimaryNode.EntityId))
					{
						RelayStorage.Builder_NetworkStorage bns = node.Storage.GetBuilder();
						if (bns != null)
							storages.Add(bns.PrimaryNode, bns);
					}
				});
				data.AntennaStorage = storages.Values.ToArray();

				// disruption

				List<Disruption.Builder_Disruption> systemDisrupt = new List<Disruption.Builder_Disruption>();
				foreach (Disruption disrupt in Disruption.AllDisruptions)
					systemDisrupt.Add(disrupt.GetBuilder());
				data.SystemDisruption = systemDisrupt.ToArray();

				// autopilot

				List<ShipAutopilot.Builder_Autopilot> buildAuto = new List<ShipAutopilot.Builder_Autopilot>();
				Registrar.ForEach<ShipAutopilot>(autopilot => {
					ShipAutopilot.Builder_Autopilot builder = autopilot.GetBuilder();
					if (builder != null)
						buildAuto.Add(builder);
				});
				data.Autopilot = buildAuto.ToArray();

				// Sync

				data.Sync = ASync.GetBuilder();

				MyAPIGateway.Utilities.SetVariable(SaveXml, MyAPIGateway.Utilities.SerializeToXML(data));

				if (Instance.m_fileMaster != null)
				{
					string identifier = Instance.LegacyIdentifier(false);
					if (identifier != null)
						if (Instance.m_fileMaster.Delete(identifier))
							Rynchodon.Logger.DebugLog("file deleted: " + identifier);
				}
			}
			catch (Exception ex)
			{
				Rynchodon.Logger.AlwaysLog("Exception: " + ex, Rynchodon.Logger.severity.ERROR);
				Rynchodon.Logger.Notify("ARMS: failed to save data", 60000, Rynchodon.Logger.severity.ERROR);
			}
		}

	}
}
