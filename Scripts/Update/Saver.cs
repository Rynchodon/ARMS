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
			public GuidedMissile.Builder_GuidedMissile[] GuidedMissiles;
		}

		private const string FileIdString = "ARMS save file id";

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

				long fileId;
				if (!MyAPIGateway.Utilities.GetVariable<long>(FileIdString, out fileId))
					return;

				var reader = m_fileMaster.GetTextReader(fileId + ".xml");

				if (reader == null)
				{
					m_logger.debugLog("Save file is missing!", "DoLoad()", Logger.severity.ERROR);
					return;
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

				foreach (GuidedMissile.Builder_GuidedMissile bgm in data.GuidedMissiles)
					GuidedMissile.CreateFromBuilder(bgm);

				m_logger.debugLog("loading complete", "DoLoad()", Logger.severity.INFO);
				Logger.debugNotify("loading complete: " + fileId, 10000, Logger.severity.INFO);
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

				List<GuidedMissile.Builder_GuidedMissile> guidedMissiles = new List<GuidedMissile.Builder_GuidedMissile>();
				GuidedMissile.ForEach(guided => {
					GuidedMissile.Builder_GuidedMissile bgm = guided.GetBuilder();
					if (bgm != null)
						guidedMissiles.Add(bgm);
				});

				data.GuidedMissiles = guidedMissiles.ToArray();

				long Ticks = DateTime.UtcNow.Ticks;
				MyAPIGateway.Utilities.SetVariable<long>(FileIdString, Ticks);

				var writer = m_fileMaster.GetTextWriter(Ticks + ".xml");
				writer.Write(MyAPIGateway.Utilities.SerializeToXML(data));
				writer.Close();

				m_logger.debugLog("Saved to " + Ticks, "SaveData()", Logger.severity.INFO);
				Logger.debugNotify("Saved to " + Ticks, 10000, Logger.severity.INFO);
			}
			catch (Exception ex)
			{
				m_logger.alwaysLog("Exception: " + ex, "SaveData()", Logger.severity.ERROR);
				Logger.notify("ARMS: failed to save data", 60000, Logger.severity.ERROR);
			}
		}

	}
}
