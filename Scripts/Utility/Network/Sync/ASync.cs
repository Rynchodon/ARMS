#if DEBUG
#define TRACE
#endif

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Sandbox.ModAPI;
using VRage.Collections;

namespace Rynchodon.Utility.Network
{
	/// <summary>
	/// Objects of this type synchronize and save events or values.
	/// </summary>
	public abstract class ASync : LogWise
	{
		/// <summary>
		/// Serialized as byte; do not change order.
		/// </summary>
		public enum Id : byte
		{
			None,

			AutopilotTerminal_ArmsAp_OnOff,
			AutopilotTerminal_ArmsAp_Commands,
			AutopilotTerminal_ArmsAp_Status,
			AutopilotTerminal_ArmsAp_AutopilotFlags,
			AutopilotTerminal_ArmsAp_PathStatus,
			AutopilotTerminal_ArmsAp_ReasonCannotTarget,
			AutopilotTerminal_ArmsAp_Complaint,
			AutopilotTerminal_ArmsAp_JumpComplaint,
			AutopilotTerminal_ArmsAp_WaitUntil,
			AutopilotTerminal_ArmsAp_BlockedBy,
			AutopilotTerminal_ArmsAp_Distance,
			AutopilotTerminal_ArmsAp_EnemyFinderBestTarget,
			AutopilotTerminal_ArmsAp_WelderUnfinishedBlocks,
			AutopilotTerminal_ArmsAp_NavigatorMover,
			AutopilotTerminal_ArmsAp_NavigatorRotator,
			AutopilotTerminal_ArmsAp_NavigatorMoverInfo,
			AutopilotTerminal_ArmsAp_NavigatorRotatorInfo,

			GuidedMissileLauncher_ShootOnce,
			GuidedMissileLauncher_Shoot,

			ProgrammableBlock_HandleDetected,
			ProgrammableBlock_BlockCounts,

			Projector_HoloDisplay,
			Projector_HD_This_Ship,
			Projector_HD_Owner,
			Projector_HD_Faction,
			Projector_HD_Neutral,
			Projector_HD_Enemy,
			Projector_HD_RangeDetection,
			Projector_HD_RadiusHolo,
			Projector_HD_EntitySizeScale,
			Projector_HD_OffsetX,
			Projector_HD_OffsetY,
			Projector_HD_OffsetZ,
			Projector_HD_IntegrityColour,
			Projector_CentreEntity,

			Solar_FaceSun,

			TextPanel_DisplayDetected,
			TextPanel_DisplayGPS,
			TextPanel_DisplayEntityId,
			TextPanel_DisplayAutopilotStatus,

			WeaponTargeting_TargetFlag,
			WeaponTargeting_TargetType,
			WeaponTargeting_WeaponFlags,
			WeaponTargeting_EntityId,
			WeaponTargeting_Range,
			WeaponTargeting_TargetBlocks,
			WeaponTargeting_GpsList,

			RadarEquipment_RadarPowerLevel,
			RadarEquipment_JammerPowerLevel,
		}

		[Serializable]
		public class SyncBuilder
		{
			public byte[] Data;

			public SyncBuilder() { }

			public SyncBuilder(byte[] Data)
			{
				this.Data = Data;
			}
		}

		protected static MyConcurrentPool<StringBuilder> _stringBuilderPool = new MyConcurrentPool<StringBuilder>();

		private static bool _hasValues;
		private static Dictionary<Id, ASync> _syncs;

		static ASync()
		{
			MessageHandler.AddHandler(MessageHandler.SubMod.Sync, HandleValue);
			MessageHandler.AddHandler(MessageHandler.SubMod.SyncRequest, HandleRequest);
		}

		[OnWorldLoad(Order = -100)]
		private static void OnLoad()
		{
			_syncs = new Dictionary<Id, ASync>();
			_hasValues = MyAPIGateway.Multiplayer.IsServer;
		}

		[OnWorldClose]
		private static void OnClose()
		{
			_syncs = null;
		}

		public static SyncBuilder GetBuilder()
		{
			if (_syncs == null)
				throw new Exception("Not initialized");

			List<byte> bytes = new List<byte>(8192);
			foreach (KeyValuePair<Id, ASync> idSync in _syncs)
				if (idSync.Value._save)
				{
					Logger.TraceLog("saving: " + idSync.Key);
					ByteConverter.AppendBytes(bytes, idSync.Key);
					idSync.Value.WriteToSave(bytes);
				}

			return new SyncBuilder(bytes.ToArray());
		}

		public static void SetBuilder(SyncBuilder builder)
		{
			if (_syncs == null)
				throw new Exception("Failed to initialize");

			for (int position = 0; position < builder.Data.Length;)
			{
				Id id = (Id)ByteConverter.GetOfType(builder.Data, ref position, typeof(Id));
				ASync sync;
				if (!_syncs.TryGetValue(id, out sync))
					throw new KeyNotFoundException("ID not found in _syncs. ID: " + id);
				sync.ReadFromSave(builder.Data, ref position);
			}
		}

		public static bool TryGet(Id id, out ASync sync)
		{
			return _syncs.TryGetValue(id, out sync);
		}

		#region Message Handler

		private static void HandleValue(byte[] message, int position)
		{
			if (_syncs == null)
				return;

			try
			{
				Id id = (Id)ByteConverter.GetOfType(message, ref position, typeof(Id));
				Logger.TraceLog("id: " + id);
				ASync instance;
				if (!_syncs.TryGetValue(id, out instance))
				{
					Logger.AlwaysLog("Missing " + typeof(ASync).Name + " for " + id, Logger.severity.ERROR);
					return;
				}
				Logger.TraceLog("got instance: " + instance.GetType().Name);
				instance.SetValueFromNetwork(message, position);
			}
			catch (Exception ex)
			{
				Logger.AlwaysLog(ex.ToString(), Logger.severity.ERROR);
				Logger.AlwaysLog("Message: " + string.Join(",", message), Logger.severity.ERROR);
			}
		}

		private static void HandleRequest(byte[] message, int position)
		{
			if (_syncs == null)
				return;

			try
			{
				ulong recipient = ByteConverter.GetUlong(message, ref position);
				if (!MyAPIGateway.Multiplayer.IsServer)
				{
					Logger.AlwaysLog("Cannot send values, this is not the server", Logger.severity.ERROR);
					return;
				}
				if (_syncs == null)
					return;

				Logger.TraceLog("got a request for values from " + recipient);
				foreach (ASync termSync in _syncs.Values)
					termSync.SendAllToClient(recipient);
			}
			catch (Exception ex)
			{
				Logger.AlwaysLog(ex.ToString(), Logger.severity.ERROR);
				Logger.AlwaysLog("Message: " + string.Join(",", message), Logger.severity.ERROR);
			}
		}

		#endregion

		private static Id GetId(Type scriptType, string valueId)
		{
			Id id;
			if (!Enum.TryParse(scriptType.Name + '_' + valueId, out id))
				throw new Exception("No id for " + scriptType.Name + " and " + valueId);
			return id;
		}

		private static void RequestValues()
		{
			if (MyAPIGateway.Multiplayer.IsServer)
				throw new Exception("Cannot request values, this is the server");

			List<byte> bytes; ResourcePool.Get(out bytes);
			bytes.Clear();

			ByteConverter.AppendBytes(bytes, MessageHandler.SubMod.SyncRequest);
			ByteConverter.AppendBytes(bytes, MyAPIGateway.Multiplayer.MyId);

			Logger.DebugLog("requesting values from server");

			if (!MyAPIGateway.Multiplayer.SendMessageToServer(MessageHandler.ModId, bytes.ToArray()))
				Logger.AlwaysLog("Failed to send message, length: " + bytes.Count, Logger.severity.ERROR);

			bytes.Clear();
			ResourcePool.Return(bytes);
		}

		protected readonly Id _id;
		protected readonly bool _save;

		protected ASync(Type scriptType, string valueId, bool save = true)
		{
			this._id = GetId(scriptType, valueId);
			this._save = save;

			if (_syncs == null)
				throw new Exception("Not initialized");
			_syncs.Add(_id, this);

			if (!_hasValues)
			{
				_hasValues = true;
				RequestValues();
			}

			traceLog("initialized");
		}

		/// <summary>
		/// Set value from saved string.
		/// </summary>
		/// <param name="entityId">Id of the script's entity</param>
		/// <param name="value">The value as a string</param>
		public abstract void SetValueFromSave(long entityId, string value);

		protected void LogMissingFromRegistrar(long entityId, bool network, [CallerFilePath] string filePath = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			alwaysLog("entity not found in Registrar: " + entityId, network ? Logger.severity.WARNING : Logger.severity.ERROR, filePath: filePath, member: member, lineNumber: lineNumber);
		}

		/// <summary>
		/// Set the value from network message.
		/// </summary>
		protected abstract void SetValueFromNetwork(byte[] message, int position);

		/// <summary>
		/// Send all values to a client.
		/// </summary>
		/// <param name="clientId">Id of the client to send values to.</param>
		protected abstract void SendAllToClient(ulong clientId);

		protected abstract void WriteToSave(List<byte> bytes);

		protected abstract void ReadFromSave(byte[] bytes, ref int position);

		protected override sealed string GetContext()
		{
			return _id.ToString();
		}

	}
}
