#if DEBUG
#define TRACE
#endif

using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;

namespace Rynchodon.Utility.Network
{
	/// <summary>
	/// Convert EntityValues saved to TerminalSync
	/// </summary>
	public static class UpgradeEntityValue
	{

		[Serializable]
		public class Builder_EntityValues
		{
			[XmlAttribute]
			public long entityId;
			public byte[] valueIds;
			public string[] values;
		}

		private struct IdMapping
		{
			public readonly Type BlockType;
			public readonly byte EntityValueId;
			public readonly TerminalSync.Id TerminalSyncId;

			public IdMapping(Type BlockType, byte EntityValueId, TerminalSync.Id TerminalSyncId)
			{
				this.BlockType = BlockType;
				this.EntityValueId = EntityValueId;
				this.TerminalSyncId = TerminalSyncId;
			}
		}

		private static List<IdMapping> _maps;
		private static List<Builder_EntityValues> _data;

		public static void Load(Builder_EntityValues[] data)
		{
			_data = new List<Builder_EntityValues>(data);

			_maps = new List<IdMapping>();
			Type type = typeof(IMyProgrammableBlock);
			_maps.Add(new IdMapping(type, 0, TerminalSync.Id.ProgrammableBlock_HandleDetected));
			_maps.Add(new IdMapping(type, 1, TerminalSync.Id.ProgrammableBlock_BlockList));
			_maps.Add(new IdMapping(typeof(IMySolarPanel), 0, TerminalSync.Id.Solar_FaceSun));
			_maps.Add(new IdMapping(typeof(IMyOxygenFarm), 0, TerminalSync.Id.Solar_FaceSun));
			_maps.Add(new IdMapping(typeof(IMyTextPanel), 0, TerminalSync.Id.TextPanel_Option));

			MyEntities.OnEntityAdd += MyEntities_OnEntityAdd;
			foreach (MyEntity entity in MyEntities.GetEntities())
				MyEntities_OnEntityAdd(entity);
		}

		private static void MyEntities_OnEntityAdd(MyEntity obj)
		{
			if (_data == null)
			{
				Logger.TraceLog("finished, unsubscribe from event");
				MyEntities.OnEntityAdd -= MyEntities_OnEntityAdd;
				return;
			}

			MyCubeGrid grid = obj as MyCubeGrid;
			if (grid == null)
				return;

			grid.OnBlockAdded += Grid_OnBlockAdded;
			foreach (MySlimBlock block in grid.CubeBlocks)
				Grid_OnBlockAdded(block);
		}

		private static void Grid_OnBlockAdded(MySlimBlock obj)
		{
			if (_data == null)
			{
				Logger.TraceLog("finished, unsubscribe from event");
				obj.CubeGrid.OnBlockAdded -= Grid_OnBlockAdded;
				return;
			}

			if (obj.FatBlock != null)
				for (int index = _data.Count - 1; index >= 0; --index)
					if (_data[index].entityId == obj.FatBlock.EntityId)
						Load(obj.FatBlock, _data[index]);
		}

		private static void Load(IMyCubeBlock block, Builder_EntityValues entityValues)
		{
			for (int index = entityValues.valueIds.Length - 1; index >= 0; --index)
			{
				TerminalSync.Id id = GetMapped(block, entityValues.valueIds[index]);
				if (id == TerminalSync.Id.None)
				{
					// if the value is obsolete, this is fine
					Logger.AlwaysLog("No mapping for " + block.GetType().Name + " and " + entityValues.valueIds[index], Logger.severity.INFO);
					continue;
				}
				TerminalSync sync;
				if (!TerminalSync.TryGet(id, out sync))
				{
					Logger.AlwaysLog("Failed to get TerminalSync for " + id, Logger.severity.ERROR);
					continue;
				}

				Logger.TraceLog("Setting entity value for " + block.nameWithId() + ", value id: " + id + ", value: " + entityValues.values[index]);
				sync.SetValue(entityValues.entityId, entityValues.values[index]);
			}

			Logger.TraceLog("Converted entity ids for " + block.nameWithId());
			_data.Remove(entityValues);
			if (_data.Count == 0)
			{
				Logger.DebugLog("All entity values converted");
				_data = null;
			}
		}

		private static TerminalSync.Id GetMapped(IMyCubeBlock block, byte entityValueId)
		{
			Type type = block.GetType();
			for (int index = _maps.Count - 1; index >= 0; --index)
			{
				IdMapping map = _maps[index];
				if (map.BlockType.IsAssignableFrom(type) && map.EntityValueId == entityValueId)
					return map.TerminalSyncId;
			}

			return TerminalSync.Id.None;
		}

	}
}
