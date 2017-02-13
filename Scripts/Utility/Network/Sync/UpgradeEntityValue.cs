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
using VRageMath;

namespace Rynchodon.Utility.Network
{
	/// <summary>
	/// Performs one-time conversion of EntityValues to TerminalSync
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
			public readonly ASync.Id TerminalSyncId;

			public SimpleMapping(Type BlockType, byte EntityValueId, ASync.Id TerminalSyncId)
				: base(BlockType, EntityValueId)
			{
				this.TerminalSyncId = TerminalSyncId;
			}
		}

		private abstract class ComplexMapping : Mapping
		{
			public readonly ASync.Id[] TerminalSyncId;

			public ComplexMapping(Type BlockType, byte EntityValueId, ASync.Id[] TerminalSyncId) : base(BlockType, EntityValueId)
			{
				this.TerminalSyncId = TerminalSyncId;
			}

			public abstract IEnumerable<KeyValuePair<ASync.Id, string>> Enumerate(string value);
		}

		private sealed class EnumMapping : ComplexMapping
		{
			public EnumMapping(Type BlockType, byte EntityValueId, ASync.Id[] TerminalSyncId) : base(BlockType, EntityValueId, TerminalSyncId) { }

			public override IEnumerable<KeyValuePair<ASync.Id, string>> Enumerate(string value)
			{
				int EnumValue;
				if (!int.TryParse(value, out EnumValue))
				{
					Logger.AlwaysLog("Cannot convert \"" + value + "\" to int", Logger.severity.ERROR);
					yield break;
				}

				int flag = 1;
				for (int index = 0; index < TerminalSyncId.Length; ++index)
				{
					yield return new KeyValuePair<ASync.Id, string>(TerminalSyncId[index], ((EnumValue & flag) != 0).ToString());
					flag *= 2;
				}
			}
		}

		private sealed class Vector3Mapping : ComplexMapping
		{
			public Vector3Mapping(Type BlockType, byte EntityValueId, ASync.Id[] TerminalSyncId) : base(BlockType, EntityValueId, TerminalSyncId) { }

			public override IEnumerable<KeyValuePair<ASync.Id, string>> Enumerate(string value)
			{
				Vector3 vector;
				try { vector = MyAPIGateway.Utilities.SerializeFromXML<Vector3>(value); }
				catch (Exception)
				{
					Logger.AlwaysLog("Cannot convert \"" + value + "\" to Vector3", Logger.severity.ERROR);
					yield break;
				}

				for (int dim = 0; dim < 3; ++dim)
					yield return new KeyValuePair<ASync.Id, string>(TerminalSyncId[dim], vector.GetDim(dim).ToString());
			}
		}

		private static List<Mapping> _maps;
		private static List<Builder_EntityValues> _data;

		public static void Load(Builder_EntityValues[] data)
		{
			_data = new List<Builder_EntityValues>(data);

			_maps = new List<Mapping>();

			Type type = typeof(IMyShipController);
			_maps.Add(new SimpleMapping(type, 0, ASync.Id.AutopilotTerminal_ArmsAp_OnOff));
			_maps.Add(new SimpleMapping(type, 1, ASync.Id.AutopilotTerminal_ArmsAp_Commands));

			type = typeof(IMyProgrammableBlock);
			_maps.Add(new SimpleMapping(type, 0, ASync.Id.ProgrammableBlock_HandleDetected));
			_maps.Add(new SimpleMapping(type, 1, ASync.Id.ProgrammableBlock_BlockCounts));

			type = typeof(IMyProjector);
			_maps.Add(new EnumMapping(type, 0, new ASync.Id[] { ASync.Id.Projector_HD_Enemy, ASync.Id.Projector_HD_Neutral, ASync.Id.Projector_HD_Faction, ASync.Id.Projector_HD_Owner, ASync.Id.Projector_HoloDisplay, ASync.Id.Projector_HD_IntegrityColour, ASync.Id.None, ASync.Id.Projector_HD_This_Ship }));
			_maps.Add(new SimpleMapping(type, 1, ASync.Id.Projector_HD_RangeDetection));
			_maps.Add(new SimpleMapping(type, 2, ASync.Id.Projector_HD_RadiusHolo));
			_maps.Add(new SimpleMapping(type, 3, ASync.Id.Projector_HD_EntitySizeScale));
			_maps.Add(new SimpleMapping(type, 4, ASync.Id.Projector_CentreEntity));
			_maps.Add(new Vector3Mapping(type, 5, new ASync.Id[] { ASync.Id.Projector_HD_OffsetX, ASync.Id.Projector_HD_OffsetY, ASync.Id.Projector_HD_OffsetZ }));

			_maps.Add(new SimpleMapping(typeof(IMySolarPanel), 0, ASync.Id.Solar_FaceSun));
			_maps.Add(new SimpleMapping(typeof(IMyOxygenFarm), 0, ASync.Id.Solar_FaceSun));

			_maps.Add(new EnumMapping(typeof(IMyTextPanel), 0, new ASync.Id[] { ASync.Id.TextPanel_DisplayDetected, ASync.Id.TextPanel_DisplayGPS, ASync.Id.TextPanel_DisplayEntityId, ASync.Id.TextPanel_DisplayAutopilotStatus }));

			_maps.Add(new SimpleMapping(typeof(IMyUserControllableGun), 0, ASync.Id.WeaponTargeting_TargetType));
			_maps.Add(new SimpleMapping(typeof(IMyUserControllableGun), 1, ASync.Id.WeaponTargeting_TargetFlag));
			_maps.Add(new SimpleMapping(typeof(IMyUserControllableGun), 2, ASync.Id.WeaponTargeting_Range));
			_maps.Add(new SimpleMapping(typeof(IMyUserControllableGun), 3, ASync.Id.WeaponTargeting_TargetBlocks));
			_maps.Add(new SimpleMapping(typeof(IMyUserControllableGun), 4, ASync.Id.WeaponTargeting_EntityId));
			_maps.Add(new SimpleMapping(typeof(IMyUserControllableGun), 5, ASync.Id.WeaponTargeting_WeaponFlags));
			_maps.Add(new SimpleMapping(typeof(IMyUserControllableGun), 6, ASync.Id.WeaponTargeting_GpsList));

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
					Logger.AlwaysLog("No mapping for " + block.GetType().Name + " and " + entityValues.valueIds[index], Logger.severity.WARNING);
					continue;
				}

				string value = entityValues.values[index];

				if (mapping is SimpleMapping)
					ApplyMap(block, ((SimpleMapping)mapping).TerminalSyncId, value);
				else
				{
					foreach (KeyValuePair<ASync.Id, string> pair in ((ComplexMapping)mapping).Enumerate(value))
						if (pair.Key != ASync.Id.None)
							ApplyMap(block, pair.Key, pair.Value);
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

		private static void ApplyMap(IMyCubeBlock block, ASync.Id id, string value)
		{
			ASync sync;
			if (!ASync.TryGet(id, out sync))
				Logger.AlwaysLog("Failed to get TerminalSync for " + id, Logger.severity.ERROR);
			else
			{
				Logger.TraceLog("Setting entity value for " + block.nameWithId() + ", value id: " + id + ", value: " + value);
				sync.SetValueFromSave(block.EntityId, value);
			}
		}

	}
}
