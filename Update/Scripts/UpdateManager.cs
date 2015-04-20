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
	/// <summary>
	/// <para>Completely circumvents MyGameLogicComponent to avoid conflicts, and offers a bit more flexibility.</para>
	/// <para>Does not attach to entities the same way, so access is lost to ObjectBuilder for MyGameLogicComponent.</para>
	/// </summary>
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

		/// <summary>
		/// Initializes if needed, issues updates.
		/// </summary>
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
				foreach (KeyValuePair<uint, List<Action>> pair in UpdateRegistrar)
					if (Update % pair.Key == 0)
					{
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

		/// <summary>
		/// register a script object for updates
		/// </summary>
		private void RegisterForUpdates(uint frequency, Action toInvoke)
		{ UpdateList(frequency).Add(toInvoke); }

		/// <summary>
		/// register a constructor Action for a block
		/// </summary>
		private void RegisterForBlock(MyObjectBuilderType objBuildType, Action<IMyCubeBlock> constructor)
		{
			myLogger.debugLog("Registered for block: " + objBuildType, "RegisterForBlock()", Logger.severity.DEBUG);
			BlockScriptConstructor(objBuildType).Add(constructor);
		}

		/// <summary>
		/// register a constructor Action for a character
		/// </summary>
		private void RegisterForCharacter(Action<IMyCharacter> constructor)
		{
			myLogger.debugLog("Registered for character", "RegisterForCharacter()", Logger.severity.DEBUG);
			CharacterScriptConstructors.Add(constructor);
		}

		/// <summary>
		/// register a constructor Action for a grid
		/// </summary>
		private void RegisterForGrid(Action<IMyCubeGrid> constructor)
		{
			myLogger.debugLog("Registered for grid", "RegisterForGrid()", Logger.severity.DEBUG);
			GridScriptConstructors.Add(constructor);
		}

		/// <summary>
		/// if necessary, builds script for an entity
		/// </summary>
		/// <param name="entity"></param>
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

		/// <summary>
		/// if necessary, builds script for a block
		/// </summary>
		private void Grid_OnBlockAdded(IMySlimBlock block)
		{
			IMyCubeBlock fatblock = block.FatBlock;
			if (fatblock != null)
			{
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
