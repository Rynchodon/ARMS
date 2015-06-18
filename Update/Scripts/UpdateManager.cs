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
	/// <para>Creation of script objects is delayed until MyAPIGateway Fields are filled.</para>
	/// <para>If an update script throws an exception, it will stop receiving updates.</para>
	/// </summary>
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
				StatorRotor.Rotor rotor = new StatorRotor.Rotor(block);
			});
			RegisterForBlock(typeof(MyObjectBuilder_MotorAdvancedRotor), (block) => {
				StatorRotor.Rotor rotor = new StatorRotor.Rotor(block);
			});
		}

		private static Dictionary<uint, List<Action>> UpdateRegistrar;

		private static Dictionary<MyObjectBuilderType, List<Action<IMyCubeBlock>>> AllBlockScriptConstructors;
		private static List<Action<IMyCharacter>> CharacterScriptConstructors;
		private static List<Action<IMyCubeGrid>> GridScriptConstructors;

		private enum Status : byte { Not_Initialized, Initialized, Terminated }
		private Status MangerStatus = Status.Not_Initialized;

		private MyQueue<Action> AddRemoveActions = new MyQueue<Action>(8);
		private FastResourceLock lock_AddRemoveActions = new FastResourceLock();

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
				{
					using (lock_AddRemoveActions.AcquireExclusiveUsing())
						for (int i = 0; i < AddRemoveActions.Count; i++)
							AddRemoveActions[i].Invoke();
				}
				catch (Exception ex)
				{ myLogger.alwaysLog("Exception in addAction[i]: " + ex, "UpdateAfterSimulation()", Logger.severity.ERROR); }

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
									myLogger.debugNotify("A script has been terminated", 10000, Logger.severity.ERROR);
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
			List<Action> UpdateL = UpdateList(frequency);
			UpdateL.Remove(toInvoke);

			if (UpdateL.Count == 0)
				UpdateRegistrar.Remove(frequency);
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
			using (lock_AddRemoveActions.AcquireExclusiveUsing())
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
				List<IMySlimBlock> blocksInGrid = new List<IMySlimBlock>();
				asGrid.GetBlocks(blocksInGrid, slim => slim.FatBlock != null);
				foreach (IMySlimBlock slim in blocksInGrid)
					AddBlock(slim);
				asGrid.OnBlockAdded += Grid_OnBlockAdded;
				asGrid.OnClosing += Grid_OnClosing;

				foreach (var constructor in GridScriptConstructors)
					constructor.Invoke(asGrid);
				return;
			}
			IMyCharacter asCharacter = entity as IMyCharacter;
			if (asCharacter != null)
			{
				foreach (var constructor in CharacterScriptConstructors)
					constructor.Invoke(asCharacter);
				return;
			}
		}

		private void Grid_OnBlockAdded(IMySlimBlock block)
		{
			using (lock_AddRemoveActions.AcquireExclusiveUsing())
				AddRemoveActions.Enqueue(() => { AddBlock(block); });
		}

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
						constructor.Invoke(fatblock);
				return;
			}
		}

		/// <summary>
		/// unregister events for grid
		/// </summary>
		private void Grid_OnClosing(IMyEntity gridAsEntity)
		{
			IMyCubeGrid asGrid = gridAsEntity as IMyCubeGrid;
			asGrid.OnBlockAdded -= Grid_OnBlockAdded;
			asGrid.OnClosing -= Grid_OnClosing;
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
