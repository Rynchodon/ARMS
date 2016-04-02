using System; // partial
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Rynchodon.AntennaRelay;
using Rynchodon.Settings;
using Rynchodon.Utility;
using Rynchodon.Weapons.Guided;
using Rynchodon.Weapons.SystemDisruption;
using Sandbox.ModAPI;
using VRage.Game.Components;

namespace Rynchodon.Update
{
	/// <summary>
	/// Saves/loads persistent data to/from a file.
	/// </summary>
	/// <remarks>
	/// Path is used as unique identifier for saving. Name is updated after using "Save As".
	/// Path is saved as a variable inside the save file so that if "Save As" is used from main menu, data can be loaded from previous save.
	/// </remarks>
	[MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
	public class Saver : MySessionComponentBase
	{

		[Serializable]
		public class Builder_ArmsData
		{
			[XmlAttribute]
			public int ModVersion = Settings.ServerSettings.latestVersion;
			[XmlAttribute]
			public long SaveTime = Globals.ElapsedTime.Ticks;
			public NetworkStorage.Builder_NetworkStorage[] AntennaStorage;
			public Disruption.Builder_Disruption[] SystemDisruption;
		}

		private const string SaveIdString = "ARMS save file id";

		public static Saver Instance;

		private readonly Logger m_logger;
		private FileMaster m_fileMaster;

		public Saver()
		{
			this.m_logger = new Logger(GetType().Name);
			Instance = this;
		}

		/// <summary>
		/// Load data from a file. Shall be called after mod is fully initialized.
		/// </summary>
		public void DoLoad()
		{
			try
			{
				if (!MyAPIGateway.Multiplayer.IsServer)
					return;

				m_fileMaster = new FileMaster("SaveDataMaster.txt", "SaveData - ", ServerSettings.GetSetting<int>(ServerSettings.SettingName.iMaxSaveKeep));

				string saveId_fromPath = GetSaveIdFromPath();
				string saveId_fromWorld;
				System.IO.TextReader reader = m_fileMaster.GetTextReader(saveId_fromPath);

				// if file from path exists, it should match stored value
				if (reader != null)
				{
					if (!MyAPIGateway.Utilities.GetVariable(SaveIdString, out saveId_fromWorld))
					{
						m_logger.alwaysLog("Save exists for path but save id could not be retrieved from world. From path: " + saveId_fromPath, "DoLoad()", Logger.severity.ERROR);
					}
					else if (saveId_fromPath != saveId_fromWorld)
					{
						m_logger.alwaysLog("Save id from path does not match save id from world. From path: " + saveId_fromPath + ", from world: " + saveId_fromWorld, "DoLoad()", Logger.severity.ERROR);

						// prefer from world
						System.IO.TextReader reader2 = m_fileMaster.GetTextReader(saveId_fromWorld);
						if (reader2 != null)
							reader = reader2;
						else
							m_logger.alwaysLog("Save id from world does not match a save. From world: " + saveId_fromWorld, "DoLoad()", Logger.severity.ERROR);
					}
				}
				else
				{
					if (MyAPIGateway.Utilities.GetVariable(SaveIdString, out saveId_fromWorld))
					{
						reader = m_fileMaster.GetTextReader(saveId_fromWorld);
						if (reader != null)
							m_logger.alwaysLog("Save is a copy, loading from old world: " + saveId_fromWorld, "DoLoad()", Logger.severity.DEBUG);
						else
						{
							m_logger.alwaysLog("Cannot load world, save id does not match any save: " + saveId_fromWorld, "DoLoad()", Logger.severity.DEBUG);
							return;
						}
					}
					else
					{
						m_logger.alwaysLog("Cannot load world, no save id found", "DoLoad()", Logger.severity.DEBUG);
						return;
					}
				}

				Builder_ArmsData data = MyAPIGateway.Utilities.SerializeFromXML<Builder_ArmsData>(reader.ReadToEnd());

				SerializableGameTime.Adjust = new TimeSpan(data.SaveTime);
				foreach (NetworkStorage.Builder_NetworkStorage bns in data.AntennaStorage)
				{
					NetworkNode node;
					if (!Registrar.TryGetValue(bns.PrimaryNode, out node))
					{
						m_logger.alwaysLog("Failed to get node for: " + bns.PrimaryNode, "DoLoad()", Logger.severity.WARNING);
						continue;
					}
					NetworkStorage store = node.Storage;
					if (store == null) // probably always true
					{
						node.ForceCreateStorage();
						store = node.Storage;
						if (store == null)
						{
							m_logger.debugLog("failed to create storage for " + node.LoggingName, "DoLoad()", Logger.severity.WARNING);
							continue;
						}
					}

					foreach (LastSeen.Builder_LastSeen bls in bns.LastSeenList)
					{
						LastSeen ls = new LastSeen(bls);
						if (ls.IsValid)
							store.Receive(ls);
					}

					m_logger.debugLog("added " + bns.LastSeenList.Length + " last seen to " + store.PrimaryNode.LoggingName, "DoLoad()", Logger.severity.DEBUG);

					foreach (Message.Builder_Message bm in bns.MessageList)
					{
						Message m = new Message(bm);
						if (m.IsValid)
							store.Receive(m);
					}

					m_logger.debugLog("added " + bns.MessageList.Length + " message to " + store.PrimaryNode.LoggingName, "DoLoad()", Logger.severity.DEBUG);
				}

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
							m_logger.alwaysLog("Unknown disruption: " + bd.Type, "DoLoad()", Logger.severity.WARNING);
							continue;
					}
					disrupt.Start(bd);
				}

				m_logger.debugLog("Loaded from " + saveId_fromWorld, "DoLoad()", Logger.severity.INFO);
			}
			catch (Exception ex)
			{
				m_logger.alwaysLog("Exception: " + ex, "DoSave()", Logger.severity.ERROR);
				Logger.notify("ARMS: failed to load data", 60000, Logger.severity.ERROR);
			}
		}

		/// <summary>
		/// Saves data to a file.
		/// </summary>
		public override void SaveData()
		{
			if (!MyAPIGateway.Multiplayer.IsServer || m_fileMaster == null)
				return;

			// critical this happens before SE saves variables (has not been an issue)
			string fileId = GetSaveIdFromPath();
			MyAPIGateway.Utilities.SetVariable(SaveIdString, fileId);

			try
			{
				// fetching data needs to happen on game thread as not every script has locks

				Builder_ArmsData data = new Builder_ArmsData();

				Dictionary<long, NetworkStorage.Builder_NetworkStorage> storages = new Dictionary<long, NetworkStorage.Builder_NetworkStorage>();

				Registrar.ForEach<NetworkNode>(node => {
					if (node.Storage != null && !storages.ContainsKey(node.Storage.PrimaryNode.EntityId))
					{
						NetworkStorage.Builder_NetworkStorage bns = node.Storage.GetBuilder();
						storages.Add(bns.PrimaryNode, bns);
					}
				});

				data.AntennaStorage = storages.Values.ToArray();

				List<Disruption.Builder_Disruption> systemDisrupt = new List<Disruption.Builder_Disruption>();
				foreach (Disruption disrupt in Disruption.AllDisruptions)
					systemDisrupt.Add(disrupt.GetBuilder());

				data.SystemDisruption = systemDisrupt.ToArray();

				var writer = m_fileMaster.GetTextWriter(fileId);
				writer.Write(MyAPIGateway.Utilities.SerializeToXML(data));
				writer.Close();

				m_logger.debugLog("Saved to " + fileId, "SaveData()", Logger.severity.INFO);
			}
			catch (Exception ex)
			{
				m_logger.alwaysLog("Exception: " + ex, "SaveData()", Logger.severity.ERROR);
				Logger.notify("ARMS: failed to save data", 60000, Logger.severity.ERROR);
			}
		}

		private string GetSaveIdFromPath()
		{
			string path = MyAPIGateway.Session.CurrentPath;
			return path.Substring(path.LastIndexOfAny(new char[] { '/', '\\' }) + 1) + ".xml";
		}

	}
}
