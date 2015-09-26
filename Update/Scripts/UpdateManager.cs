using System;
using System.Collections.Generic;
using System.Linq;
using Rynchodon.AntennaRelay;
using Rynchodon.AttachedGrid;
using Rynchodon.Autopilot;
using Rynchodon.Settings;
using Rynchodon.Weapons;
using Sandbox.Common;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
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
	/// <para>    Advantages of MyGameLogicComponent</para>
	/// <para>        An object builder is available, I assume this can be used to save data (have not tried it).</para>
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
			#region Antenna Communication

			RegisterForBlock(typeof(MyObjectBuilder_Beacon), (IMyCubeBlock block) => {
				Beacon newBeacon = new Beacon(block);
				RegisterForUpdates(100, newBeacon.UpdateAfterSimulation100, block);
			});
			RegisterForBlock(typeof(MyObjectBuilder_TextPanel), (IMyCubeBlock block) => {
				TextPanel newTextPanel = new TextPanel(block);
				RegisterForUpdates(100, newTextPanel.UpdateAfterSimulation100, block);
			});
			RegisterForBlock(typeof(MyObjectBuilder_LaserAntenna), (IMyCubeBlock block) => {
				LaserAntenna newLA = new LaserAntenna(block);
				RegisterForUpdates(100, newLA.UpdateAfterSimulation100, block);
			});
			RegisterForBlock(typeof(MyObjectBuilder_MyProgrammableBlock), (IMyCubeBlock block) => {
				ProgrammableBlock newPB = new ProgrammableBlock(block);
				RegisterForUpdates(100, newPB.UpdateAfterSimulation100, block);
			});
			RegisterForBlock(typeof(MyObjectBuilder_RadioAntenna), (IMyCubeBlock block) => {
				RadioAntenna newRA = new RadioAntenna(block);
				RegisterForUpdates(100, newRA.UpdateAfterSimulation100, block);
			});
			RegisterForPlayer((player) => {
				Player p = new Player(player);
				RegisterForUpdates(100, p.Update100, player, Player.OnLeave);
			});
			if (ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bAllowRadar))
			{
				RegisterForBlock(typeof(MyObjectBuilder_Beacon), (block) => {
					if (RadarEquipment.IsRadarOrJammer(block))
					{
						RadarEquipment r = new RadarEquipment(block);
						RegisterForUpdates(100, r.Update100, block);
					}
				});
				RegisterForBlock(typeof(MyObjectBuilder_RadioAntenna), (block) => {
					if (RadarEquipment.IsRadarOrJammer(block))
					{
						RadarEquipment r = new RadarEquipment(block);
						RegisterForUpdates(100, r.Update100, block);
					}
				});
				//RegisterForEveryBlock((IMyCubeBlock block) => {
				//	if (RadarEquipment.IsRadarOrJammer(block))
				//	{
				//		RadarEquipment r = new RadarEquipment(block);
				//		RegisterForUpdates(100, r.Update100, block);
				//	}
				//});
			}
			if (ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bUseRemoteControl))
				RegisterForBlock(typeof(MyObjectBuilder_RemoteControl), (IMyCubeBlock block) => {
					if (Navigator.IsControllableBlock(block))
						new ShipController(block);
					// Does not receive Updates
				});
			RegisterForBlock(typeof(MyObjectBuilder_Cockpit), (IMyCubeBlock block) => {
				if (Navigator.IsControllableBlock(block))
					new ShipController(block);
				// Does not receive Updates
			});

			#endregion

			#region Weapons

			if (ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bAllowWeaponControl))
			{
				#region Turrets

				RegisterForBlock(typeof(MyObjectBuilder_LargeGatlingTurret), (block) => {
					Turret t = new Turret(block);
					RegisterForUpdates(1, t.Update_Targeting, block);
				});
				RegisterForBlock(typeof(MyObjectBuilder_LargeMissileTurret), (block) => {
					Turret t = new Turret(block);
					RegisterForUpdates(1, t.Update_Targeting, block);
				});
				RegisterForBlock(typeof(MyObjectBuilder_InteriorTurret), (block) => {
					Turret t = new Turret(block);
					RegisterForUpdates(1, t.Update_Targeting, block);
				});

				#endregion

				#region Fixed

				RegisterForBlock(typeof(MyObjectBuilder_SmallGatlingGun), block => {
					FixedWeapon w = new FixedWeapon(block);
					RegisterForUpdates(1, w.Update_Targeting, block);
				});
				RegisterForBlock(typeof(MyObjectBuilder_SmallMissileLauncher), block => {
					FixedWeapon w = new FixedWeapon(block);
					RegisterForUpdates(1, w.Update_Targeting, block);
				});
				RegisterForBlock(typeof(MyObjectBuilder_SmallMissileLauncherReload), block => {
					FixedWeapon w = new FixedWeapon(block);
					RegisterForUpdates(1, w.Update_Targeting, block);
				});

				#endregion
			}
			else
				myLogger.debugLog("Weapon Control is disabled", "RegisterScripts_Server()", Logger.severity.INFO);

			#endregion

			#region Solar

			{
				SunProperties sun = new SunProperties();
				RegisterForUpdates(10, sun.Update10);
			}
			RegisterForBlock(typeof(MyObjectBuilder_OxygenFarm), (block) => {
				Solar s = new Solar(block);
				RegisterForUpdates(1, s.Update1, block);
			});
			RegisterForBlock(typeof(MyObjectBuilder_SolarPanel), (block) => {
				Solar s = new Solar(block);
				RegisterForUpdates(1, s.Update1, block);
			});

			#endregion

			#region Attached

			RegisterForBlock(typeof(MyObjectBuilder_MotorStator), (block) => {
				StatorRotor.Stator stator = new StatorRotor.Stator(block);
				RegisterForUpdates(1, stator.Update10, block);
			});
			RegisterForBlock(typeof(MyObjectBuilder_MotorAdvancedStator), (block) => {
				StatorRotor.Stator stator = new StatorRotor.Stator(block);
				RegisterForUpdates(1, stator.Update10, block);
			});
			RegisterForBlock(typeof(MyObjectBuilder_MotorRotor), (block) => {
				StatorRotor.Rotor rotor = new StatorRotor.Rotor(block);
			});
			RegisterForBlock(typeof(MyObjectBuilder_MotorAdvancedRotor), (block) => {
				StatorRotor.Rotor rotor = new StatorRotor.Rotor(block);
			});

			#endregion

			RegisterForPlayerLeaves(UserSettings.OnPlayerLeave);
		}

		/// <summary>
		/// Scripts that use UpdateManager and run on clients as well as on server shall be added here.
		/// </summary>
		private void RegisterScripts_ClientAndServer()
		{
			UserSettings.CreateLocal();
		}

		private Dictionary<uint, List<Action>> UpdateRegistrar;

		private Dictionary<MyObjectBuilderType, List<Action<IMyCubeBlock>>> AllBlockScriptConstructors;
		/// <summary>For scripts that use a separate condition to determine if they run for a block.</summary>
		private List<Action<IMyCubeBlock>> EveryBlockScriptConstructors;
		/// <summary>For scripts that run on IMyCharacter entities.</summary>
		private List<Action<IMyCharacter>> CharacterScriptConstructors;
		private List<Action<IMyCubeGrid>> GridScriptConstructors;

		/// <summary>For scripts that run on IMyPlayer.</summary>
		private List<Action<IMyPlayer>> PlayerScriptConstructors;
		private List<Action<IMyPlayer>> AnyPlayerLeaves;
		private Dictionary<IMyPlayer, Action> PlayerLeaves;

		private enum Status : byte { Not_Initialized, Initialized, Terminated }
		private Status ManagerStatus = Status.Not_Initialized;

		private LockedQueue<Action> AddRemoveActions;
		private List<IMyPlayer> playersAPI;
		private List<IMyPlayer> playersCached;

		private Logger myLogger = new Logger(null, "UpdateManager");
		//private static UpdateManager Instance;

		/// <summary>
		/// For MySessionComponentBase
		/// </summary>
		public UpdateManager() { }

		public void Init()
		{
			//myLogger.debugLog("entered Init", "Init()");
			try
			{
				if (MyAPIGateway.CubeBuilder == null || MyAPIGateway.Entities == null || MyAPIGateway.Multiplayer == null || MyAPIGateway.Parallel == null
					|| MyAPIGateway.Players == null || MyAPIGateway.Session == null || MyAPIGateway.TerminalActionsHelper == null || MyAPIGateway.Utilities == null)
					return;

				UpdateRegistrar = new Dictionary<uint, List<Action>>();
				AllBlockScriptConstructors = new Dictionary<MyObjectBuilderType, List<Action<IMyCubeBlock>>>();
				EveryBlockScriptConstructors = new List<Action<IMyCubeBlock>>();
				CharacterScriptConstructors = new List<Action<IMyCharacter>>();
				PlayerScriptConstructors = new List<Action<IMyPlayer>>();
				GridScriptConstructors = new List<Action<IMyCubeGrid>>();

				PlayerLeaves = new Dictionary<IMyPlayer, Action>();
				AnyPlayerLeaves = new List<Action<IMyPlayer>>();
				playersAPI = new List<IMyPlayer>();
				playersCached = new List<IMyPlayer>();

				AddRemoveActions = new LockedQueue<Action>(8);

				if (!MyAPIGateway.Multiplayer.MultiplayerActive)
				{
					myLogger.debugLog("Single player, running server scripts", "Init()", Logger.severity.INFO);
					RegisterScripts_Server();
				}
				else if (MyAPIGateway.Multiplayer.IsServer)
				{
					myLogger.debugLog("This is the server, running server scripts", "Init()", Logger.severity.INFO);
					RegisterScripts_Server();
				}
				else
				{
					myLogger.debugLog("Client, running client scripts only", "Init()", Logger.severity.INFO);
				}

				RegisterScripts_ClientAndServer();

				if (AllBlockScriptConstructors.Count == 0
					&& EveryBlockScriptConstructors.Count == 0
					&& CharacterScriptConstructors.Count == 0
					&& PlayerScriptConstructors.Count == 0
					&& GridScriptConstructors.Count == 0)
				{
					myLogger.alwaysLog("No scripts registered, terminating manager", "Init()", Logger.severity.INFO);
					ManagerStatus = Status.Terminated;
					return;
				}

				if (PlayerScriptConstructors.Count != 0)
					RegisterForUpdates(CheckPlayerJoinLeaveFrequency, CheckPlayerJoinLeave);

				// create script for each entity
				HashSet<IMyEntity> allEntities = new HashSet<IMyEntity>();
				MyAPIGateway.Entities.GetEntities(allEntities);

				//myLogger.debugLog("Adding all entities", "Init()");
				foreach (IMyEntity entity in allEntities)
					AddEntity(entity);

				MyAPIGateway.Entities.OnEntityAdd += Entities_OnEntityAdd;

				ManagerStatus = Status.Initialized;
			}
			catch (Exception ex)
			{
				myLogger.alwaysLog("Failed to Init(): " + ex, "Init()", Logger.severity.FATAL);
				ManagerStatus = Status.Terminated;
			}
		}

		public ulong Update { get; private set; }

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
						//myLogger.debugLog("Not Initialized", "UpdateAfterSimulation()");
						Init();
						return;
					case Status.Terminated:
						return;
				}
				Dictionary<Action, uint> Unregister = null;

				try
				{ AddRemoveActions.DequeueAll(action => action.Invoke()); }
				catch (Exception ex)
				{ myLogger.alwaysLog("Exception in AddRemoveActions: " + ex, "UpdateAfterSimulation()", Logger.severity.ERROR); }

				foreach (KeyValuePair<uint, List<Action>> pair in UpdateRegistrar)
					if (Update % pair.Key == 0)
					{
						foreach (Action item in pair.Value)
							try
							{ item.Invoke(); }
							catch (Exception ex2)
							{
								if (Unregister == null)
									Unregister = new Dictionary<Action, uint>();
								if (!Unregister.ContainsKey(item))
								{
									myLogger.alwaysLog("Script threw exception, unregistering: " + ex2, "UpdateAfterSimulation()", Logger.severity.ERROR);
									Logger.debugNotify("A script has been terminated", 10000, Logger.severity.ERROR);
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
				myLogger.alwaysLog("Exception: " + ex, "UpdateAfterSimulation()", Logger.severity.FATAL);
				ManagerStatus = Status.Terminated;
			}
			finally
			{
				Update++;
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
				unregisterOnClosing.OnClosing += (entity) => UnRegisterForUpdates(frequency, toInvoke); // we never unsubscribe from OnClosing event, hopefully that is not an issue
		}

		private void RegisterForUpdates(uint frequency, Action toInvoke, IMyPlayer unregisterOnLeaving, Action<IMyPlayer> onLeaving = null)
		{
			UpdateList(frequency).Add(toInvoke);

			PlayerLeaves.Add(unregisterOnLeaving, () => {
				UnRegisterForUpdates(frequency, toInvoke);
				try
				{
					if (onLeaving != null)
						onLeaving.Invoke(unregisterOnLeaving);
				}
				catch (Exception ex)
				{
					myLogger.debugLog("Exception in onLeaving: " + ex, "RegisterForUpdates()", Logger.severity.ERROR);
					Logger.debugNotify("Exception on player leaving", 10000, Logger.severity.ERROR);
				}
			});
		}

		/// <summary>
		/// Unregister an Action from updates
		/// </summary>
		private void UnRegisterForUpdates(uint frequency, Action toInvoke)
		{
			myLogger.debugLog("entered UnRegisterForUpdates()", "UnRegisterForUpdates()");
			List<Action> UpdateL = UpdateList(frequency);
			UpdateL.Remove(toInvoke);

			if (UpdateL.Count == 0)
				UpdateRegistrar.Remove(frequency);
			myLogger.debugLog("leaving UnRegisterForUpdates()", "UnRegisterForUpdates()");
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
			//myLogger.debugLog("Registered for block: " + objBuildType, "RegisterForBlock()", Logger.severity.DEBUG);
			BlockScriptConstructor(objBuildType).Add(constructor);
		}

		/// <summary>
		/// register a constructor Action for a character
		/// </summary>
		/// <param name="constructor">constructor wrapped in an Action</param>
		private void RegisterForCharacter(Action<IMyCharacter> constructor)
		{
			//myLogger.debugLog("Registered for character", "RegisterForCharacter()", Logger.severity.DEBUG);
			CharacterScriptConstructors.Add(constructor);
		}

		/// <summary>
		/// register a constructor Action for a grid
		/// </summary>
		/// <param name="constructor">constructor wrapped in an Action</param>
		private void RegisterForGrid(Action<IMyCubeGrid> constructor)
		{
			//myLogger.debugLog("Registered for grid", "RegisterForGrid()", Logger.severity.DEBUG);
			GridScriptConstructors.Add(constructor);
		}

		private void RegisterForPlayer(Action<IMyPlayer> constructor)
		{
			PlayerScriptConstructors.Add(constructor);
		}

		private void RegisterForPlayerLeaves(Action<IMyPlayer> onLeave)
		{
			AnyPlayerLeaves.Add(onLeave);
		}

		#endregion
		#region Events

		private void Entities_OnEntityAdd(IMyEntity entity)
		{
			if (entity.Save || entity is IMyCharacter)
				AddRemoveActions.Enqueue(() => AddEntity(entity));
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

			//myLogger.debugLog("adding entity: " + entity.getBestName() + ", flags: " + entity.Flags + ", persistent: " + entity.PersistentFlags, "AddEntity()");

			IMyCubeGrid asGrid = entity as IMyCubeGrid;
			if (asGrid != null)
			{
				//myLogger.debugLog("adding grid: " + asGrid.DisplayName + ", flags: " + asGrid.Flags + ", persistent: " + asGrid.PersistentFlags, "AddEntity()");

				//myLogger.debugLog("save: " + asGrid.Save, "AddEntity()");

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
						myLogger.alwaysLog("Exception in grid constructor: " + ex, "AddEntity()", Logger.severity.ERROR);
						Logger.debugNotify("Exception in grid constructor", 10000, Logger.severity.ERROR);
					}
				return;
			}
			IMyCharacter asCharacter = entity as IMyCharacter;
			if (asCharacter != null)
			{
				myLogger.debugLog("adding character: " + entity.getBestName(), "AddEntity()");
				foreach (var constructor in CharacterScriptConstructors)
					try { constructor.Invoke(asCharacter); }
					catch (Exception ex)
					{
						myLogger.alwaysLog("Exception in character constructor: " + ex, "AddEntity()", Logger.severity.ERROR);
						Logger.debugNotify("Exception in character constructor", 10000, Logger.severity.ERROR);
					}
				return;
			}
		}

		private void Grid_OnBlockAdded(IMySlimBlock block)
		{ AddRemoveActions.Enqueue(() => { AddBlock(block); }); }

		private HashSet<long> CubeBlock_entityIds = new HashSet<long>();

		/// <summary>
		/// if necessary, builds script for a block
		/// </summary>
		private void AddBlock(IMySlimBlock block)
		{
			IMyCubeBlock fatblock = block.FatBlock;
			if (fatblock != null)
			{
				long EntityId = fatblock.EntityId;
				if (CubeBlock_entityIds.Contains(EntityId))
					return;
				CubeBlock_entityIds.Add(EntityId);
				fatblock.OnClosing += (alsoFatblock) => { CubeBlock_entityIds.Remove(EntityId); };

				MyObjectBuilderType typeId = fatblock.BlockDefinition.TypeId;
				if (AllBlockScriptConstructors.ContainsKey(typeId))
					foreach (Action<IMyCubeBlock> constructor in BlockScriptConstructor(typeId))
						try { constructor.Invoke(fatblock); }
						catch (Exception ex)
						{
							myLogger.alwaysLog("Exception in " + typeId + " constructor: " + ex, "AddBlock()", Logger.severity.ERROR);
							Logger.debugNotify("Exception in " + typeId + " constructor", 10000, Logger.severity.ERROR);
						}

				if (EveryBlockScriptConstructors.Count > 0)
					foreach (Action<IMyCubeBlock> constructor in EveryBlockScriptConstructors)
						try { constructor.Invoke(fatblock); }
						catch (Exception ex)
						{
							myLogger.alwaysLog("Exception in every block constructor: " + ex, "AddBlock()", Logger.severity.ERROR);
							Logger.debugNotify("Exception in every block constructor", 10000, Logger.severity.ERROR);
						}

				return;
			}
		}

		/// <summary>
		/// unregister events for grid
		/// </summary>
		private void Grid_OnClosing(IMyEntity gridAsEntity)
		{
			myLogger.debugLog("entered Grid_OnClosing(): " + gridAsEntity.getBestName(), "Grid_OnClosing()");
			IMyCubeGrid asGrid = gridAsEntity as IMyCubeGrid;
			asGrid.OnBlockAdded -= Grid_OnBlockAdded;
			asGrid.OnClosing -= Grid_OnClosing;
			myLogger.debugLog("leaving Grid_OnClosing(): " + gridAsEntity.getBestName(), "Grid_OnClosing()");
		}

		private void CheckPlayerJoinLeave()
		{
			playersAPI.Clear();
			MyAPIGateway.Players.GetPlayers(playersAPI);

			foreach (IMyPlayer player in playersAPI.Except(playersCached))
				AddRemoveActions.Enqueue(() => {
					myLogger.debugLog("player joined: " + player, "CheckPlayerJoinLeave()", Logger.severity.INFO);
					playersCached.Add(player);

					foreach (var constructor in PlayerScriptConstructors)
						try { constructor.Invoke(player); }
						catch (Exception ex)
						{
							myLogger.alwaysLog("Exceptiong in player constructor: " + ex, "CheckPlayerJoinLeave()", Logger.severity.ERROR);
							Logger.debugNotify("Exception in player constructor", 10000, Logger.severity.ERROR);
						}
				});

			foreach (IMyPlayer player in playersCached.Except(playersAPI))
				AddRemoveActions.Enqueue(() => {
					myLogger.debugLog("player left: " + player, "CheckPlayerJoinLeave()", Logger.severity.INFO);
					playersCached.Remove(player);
					Action onPlayerLeave;
					if (PlayerLeaves.TryGetValue(player, out onPlayerLeave))
					{
						onPlayerLeave.Invoke();
						PlayerLeaves.Remove(player);
					}
					foreach (var onLeave in AnyPlayerLeaves)
						onLeave.Invoke(player);
				});
		}

		#endregion

		protected override void UnloadData()
		{
			base.UnloadData();
			MyAPIGateway.Entities.OnEntityAdd -= Entities_OnEntityAdd;
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
