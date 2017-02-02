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

		private class Mapping
		{
			public readonly Type BlockType;
			public readonly byte EntityValueId;

			public Mapping(Type BlockType, byte EntityValueId)
			{
				this.BlockType = BlockType;
				this.EntityValueId = EntityValueId;
			}
		}

		private class SimpleMapping: Mapping
		{
			public readonly TerminalSync.Id TerminalSyncId;

			public SimpleMapping(Type BlockType, byte EntityValueId, TerminalSync.Id TerminalSyncId)
				: base(BlockType, EntityValueId)
			{
				this.TerminalSyncId = TerminalSyncId;
			}
		}

		private class EnumMapping : Mapping
		{
			public readonly TerminalSync.Id[] TerminalSyncId;

			public EnumMapping(Type BlockType, byte EntityValueId, TerminalSync.Id[] TerminalSyncId)
				: base(BlockType, EntityValueId)
			{
				this.TerminalSyncId = TerminalSyncId;
			}

			public IEnumerable<KeyValuePair<TerminalSync.Id, bool>> Enumerate(int EnumValue)
			{
				int flag = 1;
				for (int index = 0; index < TerminalSyncId.Length; ++index)
				{
					yield return new KeyValuePair<TerminalSync.Id, bool>(TerminalSyncId[index], (EnumValue & flag) != 0);
					flag *= 2;
				}
			}
		}

		private static List<Mapping> _maps;
		private static List<Builder_EntityValues> _data;

		public static void Load(Builder_EntityValues[] data)
		{
			_data = new List<Builder_EntityValues>(data);

			_maps = new List<Mapping>();
			Type type = typeof(IMyProgrammableBlock);
			_maps.Add(new SimpleMapping(type, 0, TerminalSync.Id.ProgrammableBlock_HandleDetected));
			_maps.Add(new SimpleMapping(type, 1, TerminalSync.Id.ProgrammableBlock_BlockList));
			_maps.Add(new SimpleMapping(typeof(IMySolarPanel), 0, TerminalSync.Id.Solar_FaceSun));
			_maps.Add(new SimpleMapping(typeof(IMyOxygenFarm), 0, TerminalSync.Id.Solar_FaceSun));

			_maps.Add(new EnumMapping(typeof(IMyTextPanel), 0, new TerminalSync.Id[] { TerminalSync.Id.TextPanel_DisplayDetected, TerminalSync.Id.TextPanel_DisplayGPS, TerminalSync.Id.TextPanel_DisplayEntityId, TerminalSync.Id.TextPanel_DisplayAutopilotStatus }));

			_maps.Add(new EnumMapping(typeof(IMyProjector), 0, new TerminalSync.Id[] { TerminalSync.Id.Projector_HD_Enemy, TerminalSync.Id.Projector_HD_Neutral, TerminalSync.Id.Projector_HD_Faction, TerminalSync.Id.Projector_HD_Owner, TerminalSync.Id.Projector_HoloDisplay, TerminalSync.Id.Projector_HD_IntegrityColour, TerminalSync.Id.None, TerminalSync.Id.Projector_HD_This_Ship }));

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
				Mapping mapping = GetMapping(block, entityValues.valueIds[index]);
				if (mapping == null)
				{
					// if the value is obsolete, this is fine
					Logger.AlwaysLog("No mapping for " + block.GetType().Name + " and " + entityValues.valueIds[index], Logger.severity.INFO);
					continue;
				}
				if (mapping is SimpleMapping)
					ApplyMap(block, entityValues.values[index], ((SimpleMapping)mapping).TerminalSyncId);
				else
				{
					int enumValue;
					if (!int.TryParse(entityValues.values[index], out enumValue))
					{
						Logger.AlwaysLog("Cannot convert: " + entityValues.values[index] + " to int");
						continue;
					}
					foreach (KeyValuePair<TerminalSync.Id, bool> pair in ((EnumMapping)mapping).Enumerate(enumValue))
						if (pair.Key != TerminalSync.Id.None)
							ApplyMap(block, pair.Value.ToString(), pair.Key);
				}
			}

			Logger.TraceLog("Converted entity ids for " + block.nameWithId());
			_data.Remove(entityValues);
			if (_data.Count == 0)
			{
				Logger.DebugLog("All entity values converted");
				_data = null;
				_maps = null;
			}
		}

		private static Mapping GetMapping(IMyCubeBlock block, byte entityValueId)
		{
			Type type = block.GetType();
			for (int index = _maps.Count - 1; index >= 0; --index)
			{
				Mapping map = _maps[index];
				if (map.BlockType.IsAssignableFrom(type) && map.EntityValueId == entityValueId)
					return map;
			}

			return null;
		}

		private static void ApplyMap(IMyCubeBlock block, string value, TerminalSync.Id id)
		{
			TerminalSync sync;
			if (!TerminalSync.TryGet(id, out sync))
				Logger.AlwaysLog("Failed to get TerminalSync for " + id, Logger.severity.ERROR);
			else
			{
				Logger.TraceLog("Setting entity value for " + block.nameWithId() + ", value id: " + id + ", value: " + value);
				sync.SetValue(block.EntityId, value);
			}
		}

	}
}
