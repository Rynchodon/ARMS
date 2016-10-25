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
using VRage.Game.Components;

namespace Rynchodon.Update
{
	/// <summary>
	/// Saves/loads persistent data to/from a save file.
	/// </summary>
	/// TODO: client saving
	[MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
	public class Saver : MySessionComponentBase
	{

		[Serializable]
		public class Builder_ArmsData
		{
			[XmlAttribute]
			[Obsolete("Needed for backwards compatibility")]
			public int ModVersion;
			[XmlAttribute]
			public long SaveTime;
			public Version ArmsVersion;
			public RelayStorage.Builder_NetworkStorage[] AntennaStorage;
			public Disruption.Builder_Disruption[] SystemDisruption;
			public ShipAutopilot.Builder_Autopilot[] Autopilot;
			public ProgrammableBlock.Builder_ProgrammableBlock[] ProgrammableBlock;
			public TextPanel.Builder_TextPanel[] TextPanel;
			public WeaponTargeting.Builder_WeaponTargeting[] Weapon;
			public EntityValue.Builder_EntityValues[] EntityValues;
		}

		private const string SaveIdString = "ARMS save file id", SaveXml = "ARMS save XML data";

		public static Saver Instance;

		private readonly Logger m_logger;
		private FileMaster m_fileMaster;

		private Builder_ArmsData m_data;

		public Saver()
		{
			this.m_logger = new Logger();
			Instance = this;
		}

		public void Initialize()
		{
			GetData();
		}

		private void GetData()
		{
			m_fileMaster = new FileMaster("SaveDataMaster.txt", "SaveData - ", int.MaxValue);

			string serialized;
			if (MyAPIGateway.Utilities.GetVariable(SaveXml, out serialized))
			{
				m_data = MyAPIGateway.Utilities.SerializeFromXML<Builder_ArmsData>(serialized);
				if (m_data != null)
				{
					m_logger.debugLog("ARMS data was imbeded in the save file proper", Logger.severity.DEBUG);
					return;
				}
			}

			string identifier = LegacyIdentifier();
			if (identifier == null)
			{
				m_logger.debugLog("no identifier");
				return;
			}

			var reader = m_fileMaster.GetTextReader(identifier);
			if (reader != null)
			{
				m_logger.debugLog("loading from file: " + identifier);
				m_data = MyAPIGateway.Utilities.SerializeFromXML<Builder_ArmsData>(reader.ReadToEnd());
				reader.Close();
			}
			else
				m_logger.alwaysLog("Failed to open file reader for " + identifier);
		}

		/// <summary>
		/// Load data from a file. Shall be called after mod is fully initialized.
		/// </summary>
		public void DoLoad()
		{
			try
			{
				LoadSaveData();
			}
			catch (Exception ex)
			{
				m_logger.alwaysLog("Exception: " + ex, Logger.severity.ERROR);
				Logger.Notify("ARMS: failed to load data", 60000, Logger.severity.ERROR);
			}
		}

		private string LegacyIdentifier()
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
					m_logger.alwaysLog("Save exists for path but save id could not be retrieved from world. From path: " + saveId_fromPath, Logger.severity.ERROR);
				}
				else if (saveId_fromPath != saveId_fromWorld)
				{
					m_logger.alwaysLog("Save id from path does not match save id from world. From path: " + saveId_fromPath + ", from world: " + saveId_fromWorld, Logger.severity.ERROR);

					// prefer from world
					if (m_fileMaster.FileExists(saveId_fromWorld))
						saveId = saveId_fromWorld;
					else
						m_logger.alwaysLog("Save id from world does not match a save. From world: " + saveId_fromWorld, Logger.severity.ERROR);
				}
			}
			else
			{
				if (MyAPIGateway.Utilities.GetVariable(SaveIdString, out saveId_fromWorld))
				{
					if (m_fileMaster.FileExists(saveId_fromWorld))
					{
						m_logger.alwaysLog("Save is a copy, loading from old world: " + saveId_fromWorld, Logger.severity.DEBUG);
						saveId = saveId_fromWorld;
					}
					else
					{
						m_logger.alwaysLog("Cannot load world, save id does not match any save: " + saveId_fromWorld, Logger.severity.DEBUG);
						return null;
					}
				}
				else
				{
					m_logger.alwaysLog("Cannot load world, no save id found", Logger.severity.DEBUG);
					return null;
				}
			}

			return saveId;
		}

		private void LoadSaveData()
		{
			if (m_data == null)
			{
				m_logger.debugLog("No data to load");
				return;
			}

#pragma warning disable 612, 618
			if (Comparer<Version>.Default.Compare(m_data.ArmsVersion, default(Version)) == 0)
				m_data.ArmsVersion = new Version(m_data.ModVersion);
#pragma warning restore 612, 618

			m_logger.alwaysLog("Save version: " + m_data.ArmsVersion, Logger.severity.INFO);

			// network

			Dictionary<Message.Builder_Message, Message> messages = MyAPIGateway.Multiplayer.IsServer ? new Dictionary<Message.Builder_Message, Message>() : null;
			SerializableGameTime.Adjust = new TimeSpan(m_data.SaveTime);
			foreach (RelayStorage.Builder_NetworkStorage bns in m_data.AntennaStorage)
			{
				RelayNode node;
				if (!Registrar.TryGetValue(bns.PrimaryNode, out node))
				{
					m_logger.alwaysLog("Failed to get node for: " + bns.PrimaryNode, Logger.severity.WARNING);
					continue;
				}
				RelayStorage store = node.Storage;
				if (store == null) // probably always true
				{
					node.ForceCreateStorage();
					store = node.Storage;
					if (store == null)
					{
						m_logger.debugLog("failed to create storage for " + node.LoggingName, Logger.severity.ERROR);
						continue;
					}
				}

				foreach (LastSeen.Builder_LastSeen bls in bns.LastSeenList)
				{
					LastSeen ls = new LastSeen(bls);
					if (ls.IsValid)
						store.Receive(ls);
					else
						m_logger.debugLog("failed to create a valid last seen from builder for " + bls.EntityId, Logger.severity.WARNING);
				}

				m_logger.debugLog("added " + bns.LastSeenList.Length + " last seen to " + store.PrimaryNode.LoggingName, Logger.severity.DEBUG);

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
						m_logger.debugLog("found linked message", Logger.severity.TRACE);
					}
					if (msg.IsValid)
						store.Receive(msg);
					else
						m_logger.debugLog("failed to create a valid message from builder for " + bm.DestCubeBlock, Logger.severity.WARNING);
				}

				m_logger.debugLog("added " + bns.MessageList.Length + " message to " + store.PrimaryNode.LoggingName, Logger.severity.DEBUG);
			}

			// past this point, only synchronized data
			if (!MyAPIGateway.Multiplayer.IsServer)
			{
				m_data = null;
				return;
			}

			// system disruption

			foreach (Disruption.Builder_Disruption bd in m_data.SystemDisruption)
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
						m_logger.alwaysLog("Unknown disruption: " + bd.Type, Logger.severity.WARNING);
						continue;
				}
				disrupt.Start(bd);
			}

			// autopilot

			if (m_data.Autopilot != null)
				foreach (ShipAutopilot.Builder_Autopilot ba in m_data.Autopilot)
				{
					ShipAutopilot autopilot;
					if (Registrar.TryGetValue(ba.AutopilotBlock, out autopilot))
						autopilot.ResumeFromSave(ba);
					else
						m_logger.alwaysLog("failed to find autopilot block " + ba.AutopilotBlock, Logger.severity.WARNING);
				}

			// programmable block

			if (m_data.ProgrammableBlock != null)
				foreach (ProgrammableBlock.Builder_ProgrammableBlock bpa in m_data.ProgrammableBlock)
				{
					ProgrammableBlock pb;
					if (Registrar.TryGetValue(bpa.BlockId, out pb))
						pb.ResumeFromSave(bpa);
					else
						m_logger.alwaysLog("failed to find programmable block " + bpa.BlockId, Logger.severity.WARNING);
				}

			// text panel

			if (m_data.TextPanel != null)
				foreach (TextPanel.Builder_TextPanel btp in m_data.TextPanel)
				{
					TextPanel panel;
					if (Registrar.TryGetValue(btp.BlockId, out panel))
						panel.ResumeFromSave(btp);
					else
						m_logger.alwaysLog("failed to find text panel " + btp.BlockId, Logger.severity.WARNING);
				}

			// weapon

			if (m_data.Weapon != null)
				foreach (WeaponTargeting.Builder_WeaponTargeting bwt in m_data.Weapon)
				{
					WeaponTargeting targeting;
					if (WeaponTargeting.TryGetWeaponTargeting(bwt.WeaponId, out targeting))
						targeting.ResumeFromSave(bwt);
					else
						m_logger.alwaysLog("failed to find weapon " + bwt.WeaponId, Logger.severity.WARNING);
				}

			// entity values

			if (m_data.EntityValues != null)
				EntityValue.ResumeFromSave(m_data.EntityValues);

			m_data = null;
		}

		/// <summary>
		/// Saves data to a variable.
		/// </summary>
		public override void SaveData()
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

				//// programmable block

				//List<ProgrammableBlock.Builder_ProgrammableBlock> buildProgram = new List<ProgrammableBlock.Builder_ProgrammableBlock>();
				//Registrar.ForEach<ProgrammableBlock>(program => {
				//	ProgrammableBlock.Builder_ProgrammableBlock builder = program.GetBuilder();
				//	if (builder != null)
				//		buildProgram.Add(builder);
				//});
				//data.ProgrammableBlock = buildProgram.ToArray();

				//// text panel

				//List<TextPanel.Builder_TextPanel> buildPanel = new List<TextPanel.Builder_TextPanel>();
				//Registrar.ForEach<TextPanel>(panel => {
				//	TextPanel.Builder_TextPanel builder = panel.GetBuilder();
				//	if (builder != null)
				//		buildPanel.Add(builder);
				//});
				//data.TextPanel = buildPanel.ToArray();

				//// weapon

				//List<WeaponTargeting.Builder_WeaponTargeting> buildWeapon = new List<WeaponTargeting.Builder_WeaponTargeting>();
				//Action<WeaponTargeting> act = weapon => {
				//	WeaponTargeting.Builder_WeaponTargeting builder = weapon.GetBuilder();
				//	if (builder != null)
				//		buildWeapon.Add(builder);
				//};
				//Registrar.ForEach<FixedWeapon>(act);
				//Registrar.ForEach<Turret>(act);
				//data.Weapon = buildWeapon.ToArray();

				// entity values

				data.EntityValues = EntityValue.GetBuilders();


				MyAPIGateway.Utilities.SetVariable(SaveXml, MyAPIGateway.Utilities.SerializeToXML(data));

				if (m_fileMaster != null)
				{
					string identifier = LegacyIdentifier();
					if (identifier != null)
						if (m_fileMaster.Delete(identifier))
							m_logger.debugLog("file deleted: " + identifier);
				}
			}
			catch (Exception ex)
			{
				m_logger.alwaysLog("Exception: " + ex, Logger.severity.ERROR);
				Logger.Notify("ARMS: failed to save data", 60000, Logger.severity.ERROR);
			}
		}

	}
}
