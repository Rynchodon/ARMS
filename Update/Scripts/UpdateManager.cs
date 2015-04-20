#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.Common;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;

using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot;

namespace Rynchodon.Update
{
	[MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
	public class UpdateManager : MySessionComponentBase
	{
		private static Dictionary<uint, List<Action>> UpdateRegistrar;

		private static Dictionary<MyObjectBuilderType, List<Action<IMyCubeBlock>>> AllBlockScriptConstructors;
		private static List<Action<IMyCharacter>> CharacterScriptConstructors;
		private static List<Action<IMyCubeGrid>> GridScriptConstructors;
		//private static Dictionary<Func<bool>, Action> OtherScriptConstructors = new Dictionary<Func<bool>, Action>();

		private enum Status : byte { Not_Initialized, Initialized, Terminated }
		private Status MangerStatus = Status.Not_Initialized;

		private Logger myLogger = new Logger(null, "UpdateManager");

		/// <summary>
		/// Scripts that use UpdateManager shall be added here.
		/// </summary>
		private void RegisterScripts()
		{
			RegisterForBlock(typeof(MyObjectBuilder_Beacon), (IMyCubeBlock block) =>
			{
				myLogger.debugLog("running beacon creation", "Init()");
				Beacon newBeacon = new Beacon(block);
				RegisterForUpdates(100, newBeacon.UpdateAfterSimulation100);
			});
		}

		/// <summary>
		/// For MySessionComponentBase
		/// </summary>
		public UpdateManager() { }

		public void Init()
		{
			myLogger.debugLog("entered Init", "Init()");
			try
			{
				UpdateRegistrar = new Dictionary<uint, List<Action>>();
				AllBlockScriptConstructors = new Dictionary<MyObjectBuilderType, List<Action<IMyCubeBlock>>>();
				CharacterScriptConstructors = new List<Action<IMyCharacter>>();
				GridScriptConstructors = new List<Action<IMyCubeGrid>>();

				RegisterScripts();

				// create script for each entity
				HashSet<IMyEntity> allEntities = new HashSet<IMyEntity>();
				MyAPIGateway.Entities.GetEntities(allEntities);

				myLogger.debugLog("Adding all entities", "Init()");
				foreach (IMyEntity entity in allEntities)
					Entities_OnEntityAdd(entity);

				MyAPIGateway.Entities.OnEntityAdd += Entities_OnEntityAdd;

				MangerStatus = Status.Initialized;
			}
			catch (Exception ex)
			{
				myLogger.log("Failed to Init(): " + ex, "Init()", Logger.severity.FATAL);
				MangerStatus = Status.Terminated;
			}
		}

		private byte DelayInitBy = 100;
		public ulong Update { get; private set; }

		public override void UpdateAfterSimulation()
		{
			while (DelayInitBy > 0)
			{
				DelayInitBy--;
				return;
			}

			MainLock.MainThread_TryReleaseExclusive();
			try
			{
				switch (MangerStatus)
				{
					case Status.Not_Initialized:
						myLogger.debugLog("Not Initialized", "UpdateAfterSimulation()");
						Init();
						return;
					case Status.Terminated:
						return;
				}
				//myLogger.debugLog("Checking for need to update (currently disabled)", "UpdateAfterSimulation()");
				foreach (KeyValuePair<uint, List<Action>> pair in UpdateRegistrar)
					if (Update % pair.Key == 0)
					{
						myLogger.debugLog("Issuing an update at " + pair.Key, "UpdateAfterSimulation()");
						foreach (Action item in pair.Value)
							item.Invoke();
					}
			}
			catch (Exception ex)
			{
				myLogger.debugLog("Exception: " + ex, "UpdateAfterSimulation()");
			}
			finally
			{
				Update++;
				MainLock.MainThread_TryAcquireExclusive();
			}
		}

		private void RegisterForUpdates(uint frequency, Action toInvoke)
		{
			myLogger.debugLog("Registering action with frequency = " + frequency, "RegisterForUpdates()");
			UpdateList(frequency).Add(toInvoke);
		}

		private void RegisterForBlock(MyObjectBuilderType objBuildType, Action<IMyCubeBlock> constructor) //where T : EntityScript<IMyCubeBlock>, new()
		{
			myLogger.debugLog("Registered for block: " + objBuildType, "RegisterForBlock()");
			BlockScriptConstructor(objBuildType).Add(constructor);
				/*(IMyCubeBlock block) =>
			{
				T obj = new T();
				obj.Init(block);
				block.OnClose += obj.Close;
			});*/
		}

		private void RegisterForCharacter(Action<IMyCharacter> constructor) //where T : EntityScript<IMyCharacter>, new()
		{
			myLogger.debugLog("Registered for character", "RegisterForCharacter()");
			CharacterScriptConstructors.Add(constructor);
				/*(IMyCharacter character) =>
			{
				T obj = new T();
				obj.Init(character);
				(character as IMyEntity).OnClose += obj.Close;
			});*/
		}

		private void RegisterForGrid(Action<IMyCubeGrid> constructor) //where T : EntityScript<IMyCubeGrid>, new()
		{
			myLogger.debugLog("Registered for grid", "RegisterForGrid()");
			GridScriptConstructors.Add(constructor);
				/*(IMyCubeGrid grid) =>
			{
				T obj = new T();
				obj.Init(grid);
				grid.OnClose += obj.Close;
			});*/
		}

		private void Entities_OnEntityAdd(IMyEntity entity)
		{
			IMyCubeGrid asGrid = entity as IMyCubeGrid;
			if (asGrid != null)
			{
				List<IMySlimBlock> blocksInGrid = new List<IMySlimBlock>();
				asGrid.GetBlocks(blocksInGrid, slim => slim.FatBlock != null);
				foreach (IMySlimBlock slim in blocksInGrid)
					Grid_OnBlockAdded(slim);
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
			IMyCubeBlock fatblock = block.FatBlock;
			if (fatblock != null)
			{
				MyObjectBuilderType typeId = fatblock.BlockDefinition.TypeId;
				if (AllBlockScriptConstructors.ContainsKey(typeId))
				{
					//myLogger.debugLog("Found for ID: " + typeId, "Grid_OnBlockAdded()");
					foreach (Action<IMyCubeBlock> constructor in BlockScriptConstructor(typeId))
						constructor.Invoke(fatblock);
				}
				//else
				//	myLogger.debugLog("Nothing for ID: " + typeId, "Grid_OnBlockAdded()");
				return;
			}
		}

		private void Grid_OnClosing(IMyEntity gridAsEntity)
		{
			IMyCubeGrid asGrid = gridAsEntity as IMyCubeGrid;
			asGrid.OnBlockAdded -= Grid_OnBlockAdded;
			asGrid.OnClosing -= Grid_OnClosing;
		}

		/// <summary>
		/// Gets the constructor list mapped to a MyObjectBuilderType
		/// </summary>
		/// <param name="objBuildType"></param>
		/// <returns></returns>
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
		/// <param name="frequency"></param>
		/// <returns></returns>
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
