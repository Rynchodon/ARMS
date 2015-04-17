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
	//public abstract class EntityScript // where T : IMyEntity
	//{
	//	//internal abstract void Init(T myEntity);
	//	internal abstract void Close(IMyEntity myEntity);
	//}

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
		/// For MySessionComponentBase
		/// </summary>
		public UpdateManager() { }

		public void Init()
		{
			try
			{
				UpdateRegistrar = new Dictionary<uint, List<Action>>();
				AllBlockScriptConstructors = new Dictionary<MyObjectBuilderType, List<Action<IMyCubeBlock>>>();
				CharacterScriptConstructors = new List<Action<IMyCharacter>>();
				GridScriptConstructors = new List<Action<IMyCubeGrid>>();

				// register scripts
				RegisterForBlock(typeof(MyObjectBuilder_Beacon), (IMyCubeBlock block) =>
				{
					Beacon newBeacon = new Beacon(block);
					RegisterForUpdates(100, newBeacon.UpdateAfterSimulation100);
				});

				// create script for each entity
				HashSet<IMyEntity> allEntities = new HashSet<IMyEntity>();
				MyAPIGateway.Entities.GetEntities(allEntities);

				foreach (IMyEntity entity in allEntities)
					Entities_OnEntityAdd(entity);

				MyAPIGateway.Entities.OnEntityAdd += Entities_OnEntityAdd;

				MangerStatus = Status.Initialized;
			}
			catch (Exception ex)
			{
				myLogger.debugLog("failed to Init(): " + ex, "Init()");
				MangerStatus = Status.Terminated;
			}
		}

		private static byte DelayInitBy = 100;
		public static ulong Update { get; private set; }

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
						Init();
						return;
					case Status.Terminated:
						return;
				}
				foreach (KeyValuePair<uint, List<Action>> pair in UpdateRegistrar)
					if (Update % pair.Key == 0)
						foreach (Action item in pair.Value)
							item.Invoke();
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

		private static void RegisterForUpdates(uint frequency, Action toInvoke)
		{ UpdateList(frequency).Add(toInvoke); }

		private static void RegisterForBlock(MyObjectBuilderType objBuildType, Action<IMyCubeBlock> constructor) //where T : EntityScript<IMyCubeBlock>, new()
		{
			BlockScriptConstructor(objBuildType).Add(constructor);
				/*(IMyCubeBlock block) =>
			{
				T obj = new T();
				obj.Init(block);
				block.OnClose += obj.Close;
			});*/
		}

		private static void RegisterForCharacter(Action<IMyCharacter> constructor) //where T : EntityScript<IMyCharacter>, new()
		{
			CharacterScriptConstructors.Add(constructor);
				/*(IMyCharacter character) =>
			{
				T obj = new T();
				obj.Init(character);
				(character as IMyEntity).OnClose += obj.Close;
			});*/
		}

		private static void RegisterForGrid(Action<IMyCubeGrid> constructor) //where T : EntityScript<IMyCubeGrid>, new()
		{
			GridScriptConstructors.Add(constructor);
				/*(IMyCubeGrid grid) =>
			{
				T obj = new T();
				obj.Init(grid);
				grid.OnClose += obj.Close;
			});*/
		}

		private static void Entities_OnEntityAdd(IMyEntity entity)
		{
			IMyCubeBlock asBlock = entity as IMyCubeBlock;
			if (asBlock != null)
			{
				MyObjectBuilderType typeId = asBlock.BlockDefinition.TypeId;
				if (AllBlockScriptConstructors.ContainsKey(typeId))
					foreach (Action<IMyCubeBlock> constructor in BlockScriptConstructor(typeId))
						constructor.Invoke(asBlock);
				return;
			}
			IMyCubeGrid asGrid = entity as IMyCubeGrid;
			if (asGrid != null)
			{
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

		private static List<Action<IMyCubeBlock>> BlockScriptConstructor(MyObjectBuilderType objBuildType)
		{
			List<Action<IMyCubeBlock>> scripts;
			if (!AllBlockScriptConstructors.TryGetValue(objBuildType, out scripts))
			{
				scripts = new List<Action<IMyCubeBlock>>();
				AllBlockScriptConstructors.Add(objBuildType, scripts);
			}
			return scripts;
		}

		private static List<Action> UpdateList(uint frequency)
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
