using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Rynchodon.AntennaRelay;
using Rynchodon.Attached;
using Rynchodon.Autopilot;
using Rynchodon.Autopilot.Aerodynamics;
using Rynchodon.Autopilot.Harvest;
using Rynchodon.Settings;
using Rynchodon.Threading;
using Rynchodon.Utility;
using Rynchodon.Utility.Collections;
using Rynchodon.Weapons;
using Rynchodon.Weapons.Guided;
using Rynchodon.Weapons.SystemDisruption;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.FileSystem;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;

namespace Rynchodon.Update
{
	/// <summary>
	/// <para>Completely circumvents MyGameLogicComponent to avoid conflicts, and offers a bit more flexibility.</para>
	/// <para>Will send updates after creating object, until object is closing.</para>
	/// <para>Creation of script objects is delayed until MyAPIGateway fields are filled.</para>
	/// <para>If an update script throws an exception, it will stop receiving updates.</para>
	/// </summary>
	/// <remarks>
	/// <para>Comparision to MyGameLogicComponent</para>
	/// <para>    Disadvantages of MyGameLogicComponent:</para>
	/// <para>        NeedsUpdate can be changed by the game after you set it, so you have to work around that. i.e. For remotes it is set to NONE and UpdatingStopped() never gets called.</para>
	/// <para>        Scripts can get created before MyAPIGateway fields are filled, which can be a serious problem for initializing.</para>
	/// <para> </para>
	/// <para>    Advantages of UpdateManager:</para>
	/// <para>        Scripts can be registered conditionally. MyGameLogicComponent now supports subtypes but UpdateManager can technically do any condition.</para>
	/// <para>        Elegant handling of Exception thrown by script.</para>
	/// <para>        You don't have to create a new object for every entity if you don't need one.</para>
	/// <para>        You can set any update frequency you like without having to create a counter.</para>
	/// <para>        UpdateManager supports characters and players.</para>
	/// </remarks>
	[MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
	public class UpdateManager : MySessionComponentBase
	{
		/// <summary>Update frequency for player leave/join events</summary>
		private const byte CheckPlayerJoinLeaveFrequency = 100;

		/// <summary>
		/// <para>Scripts that use UpdateManager and run on a server shall be added here.</para>
		/// </summary>
		private void RegisterScripts_Server()
		{
			#region Autopilot

			//RadarEquipment.Definition apRadar = new RadarEquipment.Definition()
			//{
			//	Radar = true,
			//	LineOfSight = false,
			//	MaxTargets_Tracking = 3,
			//	MaxPowerLevel = 1000
			//};

			Action<IMyCubeBlock> construct = block => {
				if (ShipAutopilot.IsAutopilotBlock(block))
				{
					var sca = new ShipAutopilot(block);
					RegisterForUpdates(ShipAutopilot.UpdateFrequency, sca.Update, block);
					RegisterForUpdates(100, sca.m_block.NetworkNode.Update100, block);
					//RadarEquipment r = new RadarEquipment(block, apRadar, block);
					//RegisterForUpdates(100, r.Update100, block);
				}
			};

			RegisterForBlock(typeof(MyObjectBuilder_Cockpit), construct);
			if (ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bUseRemoteControl))
				RegisterForBlock(typeof(MyObjectBuilder_RemoteControl), construct);

			#endregion

			#region Weapons

			if (ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bAllowHacker))
				RegisterForBlock(typeof(MyObjectBuilder_LandingGear), block => {
					if (Hacker.IsHacker(block))
					{
						Hacker h = new Hacker(block);
						RegisterForUpdates(10, h.Update10, block);
					}
				});
			else
				Log.DebugLog("Hacker is disabled in settings");

			#endregion

			#region Solar

			{
				SunProperties sun = new SunProperties();
				RegisterForUpdates(10, sun.Update10);
			}
			RegisterForBlock(typeof(MyObjectBuilder_OxygenFarm), (block) => {
				Solar s = new Solar(block);
				RegisterForUpdates(100, s.Update100, block);
			});
			RegisterForBlock(typeof(MyObjectBuilder_SolarPanel), (block) => {
				Solar s = new Solar(block);
				RegisterForUpdates(100, s.Update100, block);
			});

			#endregion

			RegisterForBlock(typeof(MyObjectBuilder_OreDetector), block => {
				var od = new OreDetector(block);
				RegisterForUpdates(1000, od.Update, block);
			});
		}

		/// <summary>
		/// Scripts that use UpdateManager and run on clients as well as on server shall be added here.
		/// </summary>
		private void RegisterScripts_ClientAndServer()
		{
			#region Attached

			RegisterForBlock(new MyObjectBuilderType[] { typeof(MyObjectBuilder_MotorStator), typeof(MyObjectBuilder_MotorAdvancedStator), typeof(MyObjectBuilder_MotorSuspension) }, 
				block => RegisterForUpdates(100, (new StatorRotor.Stator(block)).Update, block));

			RegisterForBlock(typeof(MyObjectBuilder_ExtendedPistonBase), (block) => {
				Piston.PistonBase pistonBase = new Piston.PistonBase(block);
				RegisterForUpdates(100, pistonBase.Update, block);
			});

			RegisterForBlock(typeof(MyObjectBuilder_ShipConnector), (block) => {
				Connector conn = new Connector(block);
				RegisterForUpdates(10, conn.Update, block);
			});

			RegisterForBlock(typeof(MyObjectBuilder_LandingGear), (block) => {
				if (!Hacker.IsHacker(block))
					new LandingGear(block);
			});

			#endregion

			#region Antenna Communication

			Action<IMyCubeBlock> nodeConstruct = block => {
				RelayNode node = new RelayNode(block);
				RegisterForUpdates(100, node.Update100, block);
			};

			RegisterForBlock(typeof(MyObjectBuilder_Beacon), nodeConstruct);
			RegisterForBlock(typeof(MyObjectBuilder_LaserAntenna), nodeConstruct);
			RegisterForBlock(typeof(MyObjectBuilder_RadioAntenna), nodeConstruct);

			RegisterForCharacter(character => {
				if (character.IsPlayer)
				{
					RelayNode node = new RelayNode(character);
					RegisterForUpdates(100, node.Update100, (IMyEntity)character);
				}
			});

			RegisterForBlock(typeof(MyObjectBuilder_MyProgrammableBlock), block => {
				ProgrammableBlock pb = new ProgrammableBlock(block);
				if (MyAPIGateway.Multiplayer.IsServer)
					RegisterForUpdates(100, pb.Update100, block);
			});

			RegisterForBlock(typeof(MyObjectBuilder_TextPanel), block => {
				TextPanel tp = new TextPanel(block);
				if (MyAPIGateway.Multiplayer.IsServer)
					RegisterForUpdates(100, tp.Update100, block);
			});

			RegisterForBlock(typeof(MyObjectBuilder_Projector), block => {
				Projector p = new Projector(block);
				if (MyAPIGateway.Session.Player != null)
				{
					RegisterForUpdates(100, p.Update100, block);
					RegisterForUpdates(1, p.Update1, block);
				}
			});

			if (MyAPIGateway.Session.Player != null)
				new Player();

			#endregion

			#region Autopilot

			if (!MyAPIGateway.Multiplayer.IsServer)
			{
				//RadarEquipment.Definition apRadar = new RadarEquipment.Definition()
				//{
				//	Radar = true,
				//	LineOfSight = false,
				//	MaxTargets_Tracking = 3,
				//	MaxPowerLevel = 1000
				//};

				Action<IMyCubeBlock> apConstruct = (block) => {
					if (ShipAutopilot.IsAutopilotBlock(block))
					{
						nodeConstruct(block);
						new AutopilotTerminal(block);
						//RadarEquipment r = new RadarEquipment(block, apRadar, block);
						//RegisterForUpdates(100, r.Update100, block);
					}
				};

				if (ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bUseRemoteControl))
					RegisterForBlock(typeof(MyObjectBuilder_RemoteControl), apConstruct);
				RegisterForBlock(typeof(MyObjectBuilder_Cockpit), apConstruct);
			}

			if (ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bAirResistanceBeta))
			{
				RegisterForGrid(grid => {
					AeroEffects aero = new AeroEffects(grid);
					RegisterForUpdates(1, aero.Update1, grid);
					if (MyAPIGateway.Multiplayer.IsServer)
						RegisterForUpdates(100, aero.Update100, grid);
				});
				RegisterForBlock(typeof(MyObjectBuilder_Cockpit), block => RegisterForUpdates(1, (new CockpitTerminal(block)).Update1, block));
			}

			#endregion

			#region Radar

			if (ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bAllowRadar))
			{
				RegisterForBlock(typeof(MyObjectBuilder_Beacon), (block) => {
					if (RadarEquipment.IsDefinedRadarEquipment(block))
						new RadarEquipment(block);
				});
				RegisterForBlock(typeof(MyObjectBuilder_RadioAntenna), (block) => {
					if (RadarEquipment.IsDefinedRadarEquipment(block))
						new RadarEquipment(block);
				});
				RegisterForUpdates(100, RadarEquipment.UpdateAll);
			}

			#endregion

			#region Terminal Control

			RegisterForBlock(new MyObjectBuilderType[] { typeof(MyObjectBuilder_RadioAntenna), typeof(MyObjectBuilder_LaserAntenna) }, block => new ManualMessage(block));

			#endregion Terminal Control

			#region Weapon Control

			if (ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bAllowWeaponControl))
			{
				#region Turrets

				Action<IMyCubeBlock> constructor;
				if (ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bAllowGuidedMissile))
					constructor = block => {
						if (!WeaponTargeting.ValidWeaponBlock(block))
							return;
						Turret t = new Turret(block);
						RegisterForUpdates(1, t.Update_Targeting, block);
						if (GuidedMissileLauncher.IsGuidedMissileLauncher(block))
						{
							GuidedMissileLauncher gml = new GuidedMissileLauncher(t);
							RegisterForUpdates(1, gml.Update1, block);
						}
					};
				else
					constructor = block => {
						if (!WeaponTargeting.ValidWeaponBlock(block))
							return; 
						Turret t = new Turret(block);
						RegisterForUpdates(1, t.Update_Targeting, block);
					};

				RegisterForBlock(typeof(MyObjectBuilder_LargeGatlingTurret), constructor);
				RegisterForBlock(typeof(MyObjectBuilder_LargeMissileTurret), constructor);
				RegisterForBlock(typeof(MyObjectBuilder_InteriorTurret), constructor);

				#endregion

				#region Fixed

				if (ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bAllowGuidedMissile))
				{
					constructor = block => {
						if (!WeaponTargeting.ValidWeaponBlock(block))
							return; 
						FixedWeapon w = new FixedWeapon(block);
						RegisterForUpdates(1, w.Update_Targeting, block);
						if (GuidedMissileLauncher.IsGuidedMissileLauncher(block))
						{
							GuidedMissileLauncher gml = new GuidedMissileLauncher(w);
							RegisterForUpdates(1, gml.Update1, block);
						}
					};
				}
				else
					constructor = block => {
						if (!WeaponTargeting.ValidWeaponBlock(block))
							return; 
						FixedWeapon w = new FixedWeapon(block);
						RegisterForUpdates(1, w.Update_Targeting, block);
					};

				RegisterForBlock(typeof(MyObjectBuilder_SmallGatlingGun), constructor);
				RegisterForBlock(typeof(MyObjectBuilder_SmallMissileLauncher), constructor);
				RegisterForBlock(typeof(MyObjectBuilder_SmallMissileLauncherReload), constructor);

				#endregion

				// apparently missiles do not have their positions synced
				RegisterForUpdates(1, GuidedMissile.Update1);
				RegisterForUpdates(10, GuidedMissile.Update10);
				RegisterForUpdates(100, GuidedMissile.Update100);
			}
			else
				Log.DebugLog("Weapon Control is disabled", Logger.severity.INFO);

			#endregion

			#region Solar

			if (!MyAPIGateway.Multiplayer.IsServer)
			{
				RegisterForBlock(typeof(MyObjectBuilder_OxygenFarm), (block) => new Solar(block));
				RegisterForBlock(typeof(MyObjectBuilder_SolarPanel), (block) => new Solar(block));
			}

			#endregion

			new ChatHandler();
			Globals.Update100();
			RegisterForUpdates(100, Globals.Update100);

			Action<IMyCubeBlock> act = (block) => MainCockpitFix.AddController((IMyShipController)block);
			RegisterForBlock(typeof(MyObjectBuilder_Cockpit), act);
			RegisterForBlock(typeof(MyObjectBuilder_RemoteControl), act);
		}

		private static UpdateManager Instance;

		static UpdateManager()
		{
			string oldPath = Path.Combine(MyFileSystem.UserDataPath, "Storage", "363880940.sbm_Autopilot");
			if (Directory.Exists(oldPath))
			{
				string newPath = Path.Combine(MyFileSystem.UserDataPath, "Storage", "ARMS");
				if (Directory.Exists(newPath))
				{
					Logger.AlwaysLog("ARMS folder exists, data must be manually merged", Logger.severity.WARNING);
					return;
				}
				Directory.Move(oldPath, newPath);
				string AutopilotSettings = Path.Combine(newPath, "AutopilotSettings.txt");
				if (File.Exists(AutopilotSettings))
				{
					string ServerSettings = Path.Combine(newPath, "ServerSettings.txt");
					if (File.Exists(ServerSettings))
					{
						Logger.AlwaysLog("AutopilotSettings.txt and ServerSettings.txt both exist", Logger.severity.WARNING);
						return;
					}
					File.Move(AutopilotSettings, ServerSettings);
				}
				else
					Logger.AlwaysLog("No file at " + AutopilotSettings, Logger.severity.WARNING);
			}
			else
				Logger.DebugLog("No directory at " + oldPath);
		}

		/// <param name="unregisterOnClosing">Leave as null if you plan on using Unregister at all.</param>
		public static void Register(uint frequency, Action toInvoke, IMyEntity unregisterOnClosing = null)
		{
			if (Globals.WorldClosed)
				return;
			Instance.ExternalRegistrations.AddTail(() => {
				Instance.RegisterForUpdates(frequency, toInvoke, unregisterOnClosing);
			});
		}

		public static void Unregister(uint frequency, Action toInvoke)
		{
			if (Globals.WorldClosed)
				return;
			Instance.ExternalRegistrations.AddTail(() => {
				Instance.UnRegisterForUpdates(frequency, toInvoke);
			});
		}

		private Dictionary<uint, List<Action>> UpdateRegistrar = new Dictionary<uint, List<Action>>();

		private Dictionary<MyObjectBuilderType, List<Action<IMyCubeBlock>>> AllBlockScriptConstructors = new Dictionary<MyObjectBuilderType, List<Action<IMyCubeBlock>>>();
		/// <summary>For scripts that use a separate condition to determine if they run for a block.</summary>
		private List<Action<IMyCubeBlock>> EveryBlockScriptConstructors = new List<Action<IMyCubeBlock>>();
		/// <summary>For scripts that run on IMyCharacter entities.</summary>
		private List<Action<IMyCharacter>> CharacterScriptConstructors = new List<Action<IMyCharacter>>();
		private List<Action<IMyCubeGrid>> GridScriptConstructors = new List<Action<IMyCubeGrid>>();

		private enum Status : byte { Not_Initialized, Initialized, Started, Terminated }
		private Status ManagerStatus = Status.Not_Initialized;

		private LockedDeque<Action> AddRemoveActions = new LockedDeque<Action>();
		private LockedDeque<Action> ExternalRegistrations = new LockedDeque<Action>();

		private HashSet<long> CubeBlocks = new HashSet<long>();
		private HashSet<long> Characters = new HashSet<long>();

		private DateTime m_lastUpdate;

		private Logable Log { get { return new Logable("", ManagerStatus.ToString()); } }

		public UpdateManager()
		{
			ThreadTracker.SetGameThread();
			Instance = this;
			MainLock.MainThread_AcquireExclusive();
		}

		public void Init()
		{
			//Log.DebugLog("entered Init", "Init()");
			try
			{
				if (MyAPIGateway.CubeBuilder == null || MyAPIGateway.Entities == null || MyAPIGateway.Multiplayer == null || MyAPIGateway.Parallel == null
					|| MyAPIGateway.Players == null || MyAPIGateway.Session == null || MyAPIGateway.TerminalActionsHelper == null || MyAPIGateway.Utilities == null)
					return;

				if (!MyAPIGateway.Multiplayer.IsServer && MyAPIGateway.Session.Player == null)
					return;

				Globals.WorldClosed = false;
				Log.DebugLog("World: " + MyAPIGateway.Session.Name + ", Path: " + MyAPIGateway.Session.CurrentPath, Logger.severity.INFO);
				AttributeFinder.InvokeMethodsWithAttribute<OnWorldLoad>();
				MyAPIGateway.Entities.OnCloseAll += UnloadData;

				if (!MyAPIGateway.Multiplayer.MultiplayerActive)
				{
					Log.AlwaysLog("Single player, running server scripts", Logger.severity.INFO);
					RegisterScripts_Server();
				}
				else if (MyAPIGateway.Multiplayer.IsServer)
				{
					Log.AlwaysLog("This is the server, running server scripts", Logger.severity.INFO);
					RegisterScripts_Server();
				}
				else
				{
					Log.AlwaysLog("Client, running client scripts only", Logger.severity.INFO);
				}

				if (!CheckFinalBuildConstant("IS_OFFICIAL"))
					Log.AlwaysLog("Space Engineers build is UNOFFICIAL; this build is not supported. Version: " + MyFinalBuildConstants.APP_VERSION_STRING, Logger.severity.WARNING);
				else if (CheckFinalBuildConstant("IS_DEBUG"))
					Log.AlwaysLog("Space Engineers build is DEBUG; this build is not supported. Version: " + MyFinalBuildConstants.APP_VERSION_STRING, Logger.severity.WARNING);
				else
					Log.AlwaysLog("Space Engineers version: " + MyFinalBuildConstants.APP_VERSION_STRING, Logger.severity.INFO);

				Logger.DebugNotify("ARMS DEBUG build loaded", 10000, Logger.severity.INFO);

				ManagerStatus = Status.Initialized;
			}
			catch (Exception ex)
			{
				Log.AlwaysLog("Failed to Init(): " + ex, Logger.severity.FATAL);
				ManagerStatus = Status.Terminated;
			}
		}

		private bool CheckFinalBuildConstant(string fieldName)
		{
			FieldInfo field = typeof(MyFinalBuildConstants).GetField(fieldName);
			if (field == null)
				throw new NullReferenceException("MyFinalBuildConstants does not have a field named " + fieldName + " or it has unexpected binding");
			return (bool)field.GetValue(null);
		}

		private void Start()
		{
			RegisterScripts_ClientAndServer();

			// create script for each entity
			HashSet<IMyEntity> allEntities = new HashSet<IMyEntity>();
			MyAPIGateway.Entities.GetEntities(allEntities);

			//Log.DebugLog("Adding all entities", "Init()");
			foreach (IMyEntity entity in allEntities)
				AddEntity(entity);

			MyAPIGateway.Entities.OnEntityAdd += Entities_OnEntityAdd;
			ManagerStatus = Status.Started;

			if (!ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bAllowAutopilot))
			{
				Log.AlwaysLog("Disabling autopilot blocks", Logger.severity.INFO);
				MyDefinitionManager.Static.GetCubeBlockDefinition(new SerializableDefinitionId(typeof(MyObjectBuilder_Cockpit), "Autopilot-Block_Large")).Enabled = false;
				MyDefinitionManager.Static.GetCubeBlockDefinition(new SerializableDefinitionId(typeof(MyObjectBuilder_Cockpit), "Autopilot-Block_Small")).Enabled = false;
			}
			if (!ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bAllowGuidedMissile))
			{
				Log.AlwaysLog("Disabling guided missile blocks", Logger.severity.INFO);
				MyDefinitionManager.Static.GetCubeBlockDefinition(new SerializableDefinitionId(typeof(MyObjectBuilder_SmallMissileLauncher), "Souper_R12VP_Launcher")).Enabled = false;
				MyDefinitionManager.Static.GetCubeBlockDefinition(new SerializableDefinitionId(typeof(MyObjectBuilder_SmallMissileLauncher), "Souper_R8EA_Launcher")).Enabled = false;
				MyDefinitionManager.Static.GetCubeBlockDefinition(new SerializableDefinitionId(typeof(MyObjectBuilder_SmallMissileLauncher), "Souper_B3MP_Launcher")).Enabled = false;
				MyDefinitionManager.Static.GetCubeBlockDefinition(new SerializableDefinitionId(typeof(MyObjectBuilder_LargeMissileTurret), "Souper_Missile_Defense_Turret")).Enabled = false;
			}
			if (!ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bAllowHacker))
			{
				Log.AlwaysLog("Disabling hacker blocks", Logger.severity.INFO);
				MyDefinitionManager.Static.GetCubeBlockDefinition(new SerializableDefinitionId(typeof(MyObjectBuilder_LandingGear), "ARMS_SmallHackerBlock")).Enabled = false;
				MyDefinitionManager.Static.GetCubeBlockDefinition(new SerializableDefinitionId(typeof(MyObjectBuilder_LandingGear), "ARMS_LargeHackerBlock")).Enabled = false;
			}
			if (!ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bAllowRadar))
			{
				Log.AlwaysLog("Disabling radar blocks", Logger.severity.INFO);
				MyDefinitionManager.Static.GetCubeBlockDefinition(new SerializableDefinitionId(typeof(MyObjectBuilder_Beacon), "LargeBlockRadarRynAR")).Enabled = false;
				MyDefinitionManager.Static.GetCubeBlockDefinition(new SerializableDefinitionId(typeof(MyObjectBuilder_Beacon), "SmallBlockRadarRynAR")).Enabled = false;
				MyDefinitionManager.Static.GetCubeBlockDefinition(new SerializableDefinitionId(typeof(MyObjectBuilder_Beacon), "Radar_A_Large_Souper07")).Enabled = false;
				MyDefinitionManager.Static.GetCubeBlockDefinition(new SerializableDefinitionId(typeof(MyObjectBuilder_Beacon), "Radar_A_Small_Souper07")).Enabled = false;
				MyDefinitionManager.Static.GetCubeBlockDefinition(new SerializableDefinitionId(typeof(MyObjectBuilder_RadioAntenna), "PhasedArrayRadar_Large_Souper07")).Enabled = false;
				MyDefinitionManager.Static.GetCubeBlockDefinition(new SerializableDefinitionId(typeof(MyObjectBuilder_RadioAntenna), "PhasedArrayRadar_Small_Souper07")).Enabled = false;
				MyDefinitionManager.Static.GetCubeBlockDefinition(new SerializableDefinitionId(typeof(MyObjectBuilder_RadioAntenna), "PhasedArrayRadarOffset_Large_Souper07")).Enabled = false;
				MyDefinitionManager.Static.GetCubeBlockDefinition(new SerializableDefinitionId(typeof(MyObjectBuilder_RadioAntenna), "PhasedArrayRadarOffset_Small_Souper07")).Enabled = false;
				MyDefinitionManager.Static.GetCubeBlockDefinition(new SerializableDefinitionId(typeof(MyObjectBuilder_Beacon), "AWACSRadarLarge_JnSm")).Enabled = false;
				MyDefinitionManager.Static.GetCubeBlockDefinition(new SerializableDefinitionId(typeof(MyObjectBuilder_Beacon), "AWACSRadarSmall_JnSm")).Enabled = false;
				MyDefinitionManager.Static.GetCubeBlockDefinition(new SerializableDefinitionId(typeof(MyObjectBuilder_RadioAntenna), "AP_Radar_Jammer_Large")).Enabled = false;
				MyDefinitionManager.Static.GetCubeBlockDefinition(new SerializableDefinitionId(typeof(MyObjectBuilder_RadioAntenna), "AP_Radar_Jammer_Small")).Enabled = false;
			}

			AttributeFinder.InvokeMethodsWithAttribute<AfterArmsInit>();
		}

		/// <summary>
		/// Initializes if needed, issues updates.
		/// </summary>
		public override void UpdateAfterSimulation()
		{
			MainLock.MainThread_ReleaseExclusive();
			try
			{
				switch (ManagerStatus)
				{
					case Status.Not_Initialized:
						Init();
						return;

					case Status.Initialized:
						if (ServerSettings.ServerSettingsLoaded)
						{
							Log.DebugLog("Server settings loaded");
							Start();
						}
						return;

					case Status.Terminated:
						return;
				}
				Dictionary<Action, uint> Unregister = null;

				if (AddRemoveActions.Count != 0)
					try
					{ AddRemoveActions.PopHeadInvokeAll();	}
					catch (Exception ex)
					{ Log.AlwaysLog("Exception in AddRemoveActions: " + ex, Logger.severity.ERROR); }

				if (ExternalRegistrations.Count != 0)
					try
					{ ExternalRegistrations.PopHeadInvokeAll(); }
					catch (Exception ex)
					{ Log.AlwaysLog("Exception in ExternalRegistrations: " + ex, Logger.severity.ERROR); }

				foreach (KeyValuePair<uint, List<Action>> pair in UpdateRegistrar)
					if (Globals.UpdateCount % pair.Key == 0)
					{
						foreach (Action item in pair.Value)
							try
							{
								Profiler.StartProfileBlock(item);
								item.Invoke();
								Profiler.EndProfileBlock();
							}
							catch (Exception ex2)
							{
								if (Unregister == null)
									Unregister = new Dictionary<Action, uint>();
								if (!Unregister.ContainsKey(item))
								{
									Log.AlwaysLog("Script threw exception, unregistering: " + ex2, Logger.severity.ERROR);
									Logger.DebugNotify("A script has been terminated", 10000, Logger.severity.ERROR);
									Unregister.Add(item, pair.Key);
								}
							}
					}

				if (Unregister != null)
					foreach (KeyValuePair<Action, uint> pair in Unregister)
						UnRegisterForUpdates(pair.Value, pair.Key);
			}
			catch (Exception ex)
			{
				Log.AlwaysLog("Exception: " + ex, Logger.severity.FATAL);
				ManagerStatus = Status.Terminated;
			}
			finally
			{
				Globals.UpdateCount++;

				float instantSimSpeed = Globals.UpdateDuration / (float)(DateTime.UtcNow - m_lastUpdate).TotalSeconds;
				if (instantSimSpeed > 0.01f && instantSimSpeed < 1.1f)
					Globals.SimSpeed = Globals.SimSpeed * 0.9f + instantSimSpeed * 0.1f;
				//Log.DebugLog("instantSimSpeed: " + instantSimSpeed + ", SimSpeed: " + Globals.SimSpeed);
				m_lastUpdate = DateTime.UtcNow;

				MainLock.MainThread_AcquireExclusive();
			}
		}

		#region Register

		/// <summary>
		/// register an Action for updates
		/// </summary>
		/// <param name="frequency">how often to invoke</param>
		/// <param name="toInvoke">action to invoke</param>
		/// <param name="unregisterOnClosing">stop invoking when entity closes</param>
		private void RegisterForUpdates(uint frequency, Action toInvoke, IMyEntity unregisterOnClosing = null)
		{
			UpdateList(frequency).Add(toInvoke);

			if (unregisterOnClosing != null)
				unregisterOnClosing.OnClosing += (entity) => UnRegisterForUpdates(frequency, toInvoke);
		}

		/// <summary>
		/// Unregister an Action from updates
		/// </summary>
		private void UnRegisterForUpdates(uint frequency, Action toInvoke)
		{
			if (UpdateRegistrar == null)
				return;

			//Log.DebugLog("entered UnRegisterForUpdates()");
			List<Action> UpdateL = UpdateList(frequency);
			UpdateL.Remove(toInvoke);

			if (UpdateL.Count == 0)
				UpdateRegistrar.Remove(frequency);
			//Log.DebugLog("leaving UnRegisterForUpdates()");
		}

		/// <summary>
		/// Register a constructor for every block, it is highly recommended to include a condition in the Action.
		/// </summary>
		/// <param name="constructor">constructor wrapped in an Action</param>
		private void RegisterForEveryBlock(Action<IMyCubeBlock> constructor)
		{
			EveryBlockScriptConstructors.Add(constructor);
		}

		/// <summary>
		/// register a constructor Action for a block
		/// </summary>
		/// <param name="objBuildType">type of block to create for</param>
		/// <param name="constructor">construcor wrapped in an Action</param>
		private void RegisterForBlock(MyObjectBuilderType objBuildType, Action<IMyCubeBlock> constructor)
		{
			//Log.DebugLog("Registered for block: " + objBuildType, "RegisterForBlock()", Logger.severity.DEBUG);
			BlockScriptConstructor(objBuildType).Add(constructor);
		}

		private void RegisterForBlock(IEnumerable<MyObjectBuilderType> objBuildTypes, Action<IMyCubeBlock> constructor)
		{
			foreach (MyObjectBuilderType obt in objBuildTypes)
				RegisterForBlock(obt, constructor);
		}

		/// <summary>
		/// register a constructor Action for a character
		/// </summary>
		/// <param name="constructor">constructor wrapped in an Action</param>
		private void RegisterForCharacter(Action<IMyCharacter> constructor)
		{
			//Log.DebugLog("Registered for character", "RegisterForCharacter()", Logger.severity.DEBUG);
			CharacterScriptConstructors.Add(constructor);
		}

		/// <summary>
		/// register a constructor Action for a grid
		/// </summary>
		/// <param name="constructor">constructor wrapped in an Action</param>
		private void RegisterForGrid(Action<IMyCubeGrid> constructor)
		{
			//Log.DebugLog("Registered for grid", "RegisterForGrid()", Logger.severity.DEBUG);
			GridScriptConstructors.Add(constructor);
		}

		#endregion
		#region Events

		private void Entities_OnEntityAdd(IMyEntity entity)
		{
			if (entity.Save || entity is IMyCharacter)
				AddRemoveActions.AddTail(() => AddEntity(entity));
		}

		/// <summary>
		/// if necessary, builds script for an entity
		/// </summary>
		/// <param name="entity"></param>
		private void AddEntity(IMyEntity entity)
		{
			// the save flag is often on initially and disabled after
			if (!(entity.Save || entity is IMyCharacter))
				return;

			IMyCubeGrid asGrid = entity as IMyCubeGrid;
			if (asGrid != null)
			{
				List<IMySlimBlock> blocksInGrid = new List<IMySlimBlock>();
				asGrid.GetBlocks(blocksInGrid, slim => slim.FatBlock != null);
				foreach (IMySlimBlock slim in blocksInGrid)
					AddBlock(slim);
				asGrid.OnBlockAdded += Grid_OnBlockAdded;
				asGrid.OnClosing += Grid_OnClosing;

				foreach (var constructor in GridScriptConstructors)
					try { constructor.Invoke(asGrid); }
					catch (Exception ex)
					{
						Log.AlwaysLog("Exception in grid constructor: " + ex, Logger.severity.ERROR);
						Logger.DebugNotify("Exception in grid constructor", 10000, Logger.severity.ERROR);
					}
				return;
			}
			IMyCharacter asCharacter = entity as IMyCharacter;
			if (asCharacter != null)
			{
				if (!Characters.Add(entity.EntityId))
					return;
				entity.OnClosing += alsoChar => {
					if (Characters != null)
						Characters.Remove(alsoChar.EntityId);
				};

				Log.DebugLog("adding character: " + entity.getBestName());
				foreach (var constructor in CharacterScriptConstructors)
					try { constructor.Invoke(asCharacter); }
					catch (Exception ex)
					{
						Log.AlwaysLog("Exception in character constructor: " + ex, Logger.severity.ERROR);
						Logger.DebugNotify("Exception in character constructor", 10000, Logger.severity.ERROR);
					}
				return;
			}
		}

		private void Grid_OnBlockAdded(IMySlimBlock block)
		{ AddRemoveActions.AddTail(() => { AddBlock(block); }); }

		/// <summary>
		/// if necessary, builds script for a block
		/// </summary>
		private void AddBlock(IMySlimBlock block)
		{
			IMyCubeBlock fatblock = block.FatBlock;
			if (fatblock != null)
			{
				if (!CubeBlocks.Add(fatblock.EntityId))
					return;
				fatblock.OnClosing += alsoFatblock => {
					if (CubeBlocks != null)
						CubeBlocks.Remove(alsoFatblock.EntityId);
				};

				MyObjectBuilderType typeId = fatblock.BlockDefinition.TypeId;

				//Log.DebugLog("block definition: " + fatblock.DefinitionDisplayNameText + ", typeId: " + typeId, "AddBlock()"); // used to find which builder is associated with a block

				if (AllBlockScriptConstructors.ContainsKey(typeId))
					foreach (Action<IMyCubeBlock> constructor in BlockScriptConstructor(typeId))
						try { constructor.Invoke(fatblock); }
						catch (Exception ex)
						{
							Log.AlwaysLog("Exception in " + typeId + " constructor: " + ex, Logger.severity.ERROR);
							Logger.DebugNotify("Exception in " + typeId + " constructor", 10000, Logger.severity.ERROR);
						}

				if (EveryBlockScriptConstructors.Count > 0)
					foreach (Action<IMyCubeBlock> constructor in EveryBlockScriptConstructors)
						try { constructor.Invoke(fatblock); }
						catch (Exception ex)
						{
							Log.AlwaysLog("Exception in every block constructor: " + ex, Logger.severity.ERROR);
							Logger.DebugNotify("Exception in every block constructor", 10000, Logger.severity.ERROR);
						}

				return;
			}
		}

		/// <summary>
		/// unregister events for grid
		/// </summary>
		private void Grid_OnClosing(IMyEntity gridAsEntity)
		{
			// Log may be null, so log with Logger...

			IMyCubeGrid asGrid = gridAsEntity as IMyCubeGrid;
			asGrid.OnBlockAdded -= Grid_OnBlockAdded;
			asGrid.OnClosing -= Grid_OnClosing;
		}

		#endregion

		public override void SaveData()
		{
			AttributeFinder.InvokeMethodsWithAttribute<OnWorldSave>();
		}

		protected override void UnloadData()
		{
			base.UnloadData();
			if (MyAPIGateway.Entities != null)
			{
				MyAPIGateway.Entities.OnEntityAdd -= Entities_OnEntityAdd;
				MyAPIGateway.Entities.OnCloseAll -= UnloadData;
			}

			if (!Globals.WorldClosed)
			{
				MainLock.MainThread_ReleaseExclusive();
				try
				{
					AttributeFinder.InvokeMethodsWithAttribute<OnWorldClose>();
				}
				catch (Exception ex)
				{
					// if world is closed by X button, expect an exception
					Log.AlwaysLog("Exception while unloading: " + ex, Logger.severity.ERROR);
				}
				Globals.WorldClosed = true;
				Profiler.Write();
			}

			// in case SE doesn't clean up properly, clear all fields
			foreach (FieldInfo field in GetType().GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
				if (!field.IsLiteral && !field.IsInitOnly)
					field.SetValue(this, null);

			ManagerStatus = Status.Terminated;
		}

		/// <summary>
		/// Gets the constructor list mapped to a MyObjectBuilderType
		/// </summary>
		private List<Action<IMyCubeBlock>> BlockScriptConstructor(MyObjectBuilderType objBuildType)
		{
			List<Action<IMyCubeBlock>> scripts;
			if (!AllBlockScriptConstructors.TryGetValue(objBuildType, out scripts))
			{
				scripts = new List<Action<IMyCubeBlock>>();
				AllBlockScriptConstructors.Add(objBuildType, scripts);
			}
			return scripts;
		}

		/// <summary>
		/// Gets the update list mapped to a frequency
		/// </summary>
		private List<Action> UpdateList(uint frequency)
		{
			List<Action> updates;
			if (!UpdateRegistrar.TryGetValue(frequency, out updates))
			{
				updates = new List<Action>();
				UpdateRegistrar.Add(frequency, updates);
			}
			return updates;
		}

	}
}
