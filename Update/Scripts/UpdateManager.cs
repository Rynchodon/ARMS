#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using Rynchodon.AntennaRelay;
using Rynchodon.AttachedGrid;
using Rynchodon.Autopilot;
using Rynchodon.Weapons;
using Sandbox.Common;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage;
using VRage.Collections;
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
	/// <para>        UpdateManager supports characters and could be improved to include any entity.</para>
	/// </remarks>
	[MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
	public class UpdateManager : MySessionComponentBase
	{
		/// <summary>
		/// If true, all scripts will run only on server / single player
		/// </summary>
		private const bool ServerOnly = true;

		/// <summary>
		/// Scripts that use UpdateManager shall be added here.
		/// </summary>
		private void RegisterScripts()
		{
			// Antenna Communication
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

			// Navigation
			if (Settings.GetSetting<bool>(Settings.SettingName.bUseRemoteControl))
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

			// Weapons
			if (Settings.GetSetting<bool>(Settings.SettingName.bAllowWeaponControl))
			{
				// Turrets
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

				// Fixed
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
			}
			else
				myLogger.debugLog("Weapon Control is disabled", "RegisterScripts()", Logger.severity.INFO);

			// Attached Blocks
			RegisterForBlock(typeof(MyObjectBuilder_MotorStator), (block) => {
				StatorRotor.Stator stator = new StatorRotor.Stator(block);
				RegisterForUpdates(1, stator.Update10, block);
			});
			RegisterForBlock(typeof(MyObjectBuilder_MotorAdvancedStator), (block) => {
				StatorRotor.Stator stator = new StatorRotor.Stator(block);
				RegisterForUpdates(1, stator.Update10, block);
			});
			RegisterForBlock(typeof(MyObjectBuilder_MotorRotor), (block) => {
				new StatorRotor.Rotor(block);
			});
			RegisterForBlock(typeof(MyObjectBuilder_MotorAdvancedRotor), (block) => {
				new StatorRotor.Rotor(block);
			});

			RegisterForBlock(typeof(MyObjectBuilder_ExtendedPistonBase), (block) => {
				Piston.PistonBase pistonBase = new Piston.PistonBase(block);
				RegisterForUpdates(100, pistonBase.Update100, block);
			});
			RegisterForBlock(typeof(MyObjectBuilder_PistonTop), (block) => {
				Piston.PistonTop pistonTop = new Piston.PistonTop(block);
				RegisterForUpdates(100, pistonTop.Update100, block);
			});
			RegisterForBlock(typeof(MyObjectBuilder_ShipConnector), (block) => {
				Connector conn = new Connector(block);
				RegisterForUpdates(10, conn.Update100, block);
			});
			RegisterForBlock(typeof(MyObjectBuilder_LandingGear), (block) => {
				new LandingGear(block);
			});
		}

		private static Dictionary<uint, List<Action>> UpdateRegistrar;

		private static Dictionary<MyObjectBuilderType, List<Action<IMyCubeBlock>>> AllBlockScriptConstructors;
		private static List<Action<IMyCharacter>> CharacterScriptConstructors;
		private static List<Action<IMyCubeGrid>> GridScriptConstructors;

		private enum Status : byte { Not_Initialized, Initialized, Terminated }
		private Status MangerStatus = Status.Not_Initialized;

		private LockedQueue<Action> AddRemoveActions = new LockedQueue<Action>(8);

		private Logger myLogger = new Logger(null, "UpdateManager");
		private static UpdateManager Instance;

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

				if (ServerOnly && MyAPIGateway.Multiplayer.MultiplayerActive && !MyAPIGateway.Multiplayer.IsServer)
				{
					myLogger.alwaysLog("Not a server, disabling scripts", "Init()", Logger.severity.INFO);
					MangerStatus = Status.Terminated;
					return;
				}
				if (MyAPIGateway.Multiplayer.MultiplayerActive)
					myLogger.alwaysLog("Is server, running scripts", "Init()", Logger.severity.INFO);
				else
					myLogger.alwaysLog("Single player, running scripts", "Init()", Logger.severity.INFO);

				UpdateRegistrar = new Dictionary<uint, List<Action>>();
				AllBlockScriptConstructors = new Dictionary<MyObjectBuilderType, List<Action<IMyCubeBlock>>>();
				CharacterScriptConstructors = new List<Action<IMyCharacter>>();
				GridScriptConstructors = new List<Action<IMyCubeGrid>>();

				RegisterScripts();

				// create script for each entity
				HashSet<IMyEntity> allEntities = new HashSet<IMyEntity>();
				MyAPIGateway.Entities.GetEntities(allEntities);

				//myLogger.debugLog("Adding all entities", "Init()");
				foreach (IMyEntity entity in allEntities)
					AddEntity(entity);

				MyAPIGateway.Entities.OnEntityAdd += Entities_OnEntityAdd;

				MangerStatus = Status.Initialized;
			}
			catch (Exception ex)
			{
				myLogger.alwaysLog("Failed to Init(): " + ex, "Init()", Logger.severity.FATAL);
				MangerStatus = Status.Terminated;
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
				switch (MangerStatus)
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
				MangerStatus = Status.Terminated;
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

		#endregion
		#region Events

		private void Entities_OnEntityAdd(IMyEntity entity)
		{
			if (entity.Save)
				AddRemoveActions.Enqueue(() => AddEntity(entity));
		}

		/// <summary>
		/// if necessary, builds script for an entity
		/// </summary>
		/// <param name="entity"></param>
		private void AddEntity(IMyEntity entity)
		{
			IMyCubeGrid asGrid = entity as IMyCubeGrid;
			if (asGrid != null)
			{
				myLogger.debugLog("adding grid: " + asGrid.DisplayName, "AddEntity()");

				List<IMySlimBlock> blocksInGrid = new List<IMySlimBlock>();
				asGrid.GetBlocks(blocksInGrid, slim => slim.FatBlock != null);
				foreach (IMySlimBlock slim in blocksInGrid)
					AddBlock(slim);
				asGrid.OnBlockAdded += Grid_OnBlockAdded;
				asGrid.OnClosing += Grid_OnClosing;

				foreach (var constructor in GridScriptConstructors)
					try { constructor.Invoke(asGrid); }
					catch (Exception ex) { myLogger.alwaysLog("Exception in grid constructor: " + ex, "AddEntity()", Logger.severity.ERROR); }
				return;
			}
			IMyCharacter asCharacter = entity as IMyCharacter;
			if (asCharacter != null)
			{
				foreach (var constructor in CharacterScriptConstructors)
					try { constructor.Invoke(asCharacter); }
					catch (Exception ex) { myLogger.alwaysLog("Exception in character constructor: " + ex, "AddEntity()", Logger.severity.ERROR); }
				return;
			}
		}

		private void Grid_OnBlockAdded(IMySlimBlock block)
		{ AddRemoveActions.Enqueue(() => { AddBlock(block); }); }

		/// <remarks>Can't use Entity Ids because multiple blocks may share an Id.</remarks>
		private HashSet<IMyCubeBlock> CubeBlocks = new HashSet<IMyCubeBlock>();

		/// <summary>
		/// if necessary, builds script for a block
		/// </summary>
		private void AddBlock(IMySlimBlock block)
		{
			IMyCubeBlock fatblock = block.FatBlock;
			if (fatblock != null)
			{
				if (CubeBlocks.Contains(fatblock))
					return;
				CubeBlocks.Add(fatblock);
				fatblock.OnClosing += (alsoFatblock) => { CubeBlocks.Remove(fatblock); };

				MyObjectBuilderType typeId = fatblock.BlockDefinition.TypeId;
				
				//myLogger.debugLog("block definition: " + fatblock.DefinitionDisplayNameText + ", typeId: " + typeId, "AddBlock()"); // used to find which builder is associated with a block
				
				if (AllBlockScriptConstructors.ContainsKey(typeId))
					foreach (Action<IMyCubeBlock> constructor in BlockScriptConstructor(typeId))
						try { constructor.Invoke(fatblock); }
						catch (Exception ex) { myLogger.alwaysLog("Exception in " + typeId + " constructor: " + ex, "AddBlock()", Logger.severity.ERROR); }
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
