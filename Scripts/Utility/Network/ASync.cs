#if DEBUG
#define TRACE
#endif

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Rynchodon.Update;
using Sandbox.ModAPI;
using VRage.Collections;

namespace Rynchodon.Utility.Network
{
	/// <summary>
	/// Objects of this type synchronize and save events or values.
	/// </summary>
	/// TODO: saving
	/// TODO: default value
	public abstract class ASync : LogWise
	{

		public enum Id : byte
		{
			None,

			AutopilotTerminal_ArmsAp_OnOff,
			AutopilotTerminal_ArmsAp_Commands,
			AutopilotTerminal_ArmsAp_Status,

			AutopilotTerminal_ArmsAp_HasControl,
			AutopilotTerminal_ArmsAp_RotationBlocked,
			AutopilotTerminal_ArmsAp_EnemyFinderIssue,
			AutopilotTerminal_ArmsAp_HasNavigatorMover,
			AutopilotTerminal_ArmsAp_HasNavigatorRotator,

			AutopilotTerminal_ArmsAp_PathStatus,
			AutopilotTerminal_ArmsAp_ReasonCannotTarget,
			AutopilotTerminal_ArmsAp_Complaint,
			AutopilotTerminal_ArmsAp_JumpComplaint,
			AutopilotTerminal_ArmsAp_WaitUntil,
			AutopilotTerminal_ArmsAp_BlockedBy,
			AutopilotTerminal_ArmsAp_LinearDistance,
			AutopilotTerminal_ArmsAp_AngularDistance,
			AutopilotTerminal_ArmsAp_EnemyFinderBestTarget,
			AutopilotTerminal_ArmsAp_WelderUnfinishedBlocks,
			AutopilotTerminal_ArmsAp_NavigatorMover,
			AutopilotTerminal_ArmsAp_NavigatorRotator,
			AutopilotTerminal_ArmsAp_NavigatorMoverInfo,
			AutopilotTerminal_ArmsAp_NavigatorRotatorInfo,

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
		}

		public struct SyncMessage
		{
			public ulong? recipient;
			public ASync.Id id;
			public object value;
			public List<long> entityId;

			public SyncMessage(ulong? recipient, ASync.Id id, object value, long blockId)
			{
				this.recipient = recipient;
				this.id = id;
				this.value = value;
				this.entityId = new List<long>();
				this.entityId.Add(blockId);
			}

			public override string ToString()
			{
				return "recipient: " + recipient + ", id: " + id + ", value: " + value + ", blockId: " + string.Join(",", entityId);
			}
		}

		protected static MyConcurrentPool<StringBuilder> _stringBuilderPool = new MyConcurrentPool<StringBuilder>();
		/// <summary>Values for which scripts do not yet exist.</summary>
		protected static List<SyncMessage> _orphanValues;

		private static bool _hasValues;
		private static List<SyncMessage> _outgoingMessages;
		private static Dictionary<Id, ASync> _syncs;

		static ASync()
		{
			MessageHandler.AddHandler(MessageHandler.SubMod.Sync, HandleValue);
			MessageHandler.AddHandler(MessageHandler.SubMod.SyncRequest, HandleRequest);
		}

		[OnWorldLoad]
		private static void OnLoad()
		{
			_hasValues = MyAPIGateway.Multiplayer.IsServer;
			_outgoingMessages = new List<SyncMessage>();
			_syncs = new Dictionary<Id, ASync>();

			UpdateManager.Register(1, SendOutgoingMessages);
		}

		[OnWorldClose]
		private static void OnClose()
		{
			_orphanValues = null;
			_outgoingMessages = null;
			_syncs = null;
		}

		public static bool TryGet(Id id, out ASync sync)
		{
			return _syncs.TryGetValue(id, out sync);
		}

		private static void HandleValue(byte[] message, int position)
		{
			if (_syncs == null)
				return;

			try
			{
				SyncMessage sync;
				sync.recipient = null;
				sync.id = (Id)ByteConverter.GetOfType(message, ref position, typeof(Id));
				Logger.TraceLog("id: " + sync.id);
				ASync instance;
				if (!_syncs.TryGetValue(sync.id, out instance))
				{
					Logger.AlwaysLog("Missing " + typeof(ASync).Name + " for " + sync.id, Logger.severity.ERROR);
					return;
				}
				Logger.TraceLog("got instance: " + instance.GetType().Name);

				Type type = instance.ValueType;
				Logger.TraceLog("type: " + type);
				sync.value = type == null ? null : ByteConverter.GetOfType(message, ref position, type);
				Logger.TraceLog("value: " + sync.value);

				sync.entityId = new List<long>((message.Length - position) / 8);
				while (position < message.Length)
					sync.entityId.Add(ByteConverter.GetLong(message, ref position));

				Logger.TraceLog("block id count: " + sync.entityId.Count);

				instance.SetValue(sync);
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
				foreach (KeyValuePair<Id, ASync> termSync in _syncs)
				{
					Logger.TraceLog("adding values for: " + termSync.Key);
					foreach (KeyValuePair<long, object> valuePair in termSync.Value.AllValues())
					{
						Logger.TraceLog("value from: " + valuePair.Key + " is " + valuePair.Value);
						_outgoingMessages.AddSyncMessage(recipient, termSync.Key, valuePair.Value, valuePair.Key);
					}
					SendOutgoingMessages();
				}
			}
			catch (Exception ex)
			{
				Logger.AlwaysLog(ex.ToString(), Logger.severity.ERROR);
				Logger.AlwaysLog("Message: " + string.Join(",", message), Logger.severity.ERROR);
			}
		}

		private static void AddIdAndValue(List<byte> bytes, SyncMessage sync)
		{
			bytes.Clear();
			ByteConverter.AppendBytes(bytes, MessageHandler.SubMod.Sync);
			ByteConverter.AppendBytes(bytes, sync.id);
			ByteConverter.AppendBytes(bytes, sync.value);
		}

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

		private static void SendOutgoingMessages()
		{
			const int maxSize = 4088;

			List<byte> bytes; ResourcePool.Get(out bytes);

			for (int outMsgIndex = _outgoingMessages.Count - 1; outMsgIndex >= 0; --outMsgIndex)
			{
				SyncMessage sync = _outgoingMessages[outMsgIndex];

				AddIdAndValue(bytes, sync);
				int initCount = bytes.Count;

				if (initCount > maxSize)
				{
					Logger.AlwaysLog("Cannot send message, value is too large. byte count: " + initCount + ", value: " + sync.value);
					continue;
				}

				for (int blockIdIndex = sync.entityId.Count - 1; blockIdIndex >= 0; --blockIdIndex)
				{
					ByteConverter.AppendBytes(bytes, sync.entityId[blockIdIndex]);
					if (bytes.Count > maxSize)
					{
						Logger.TraceLog("Over max size, sending message");
						SendOutgoingMessages(bytes, sync);
						AddIdAndValue(bytes, sync);
					}
				}

				if (bytes.Count != initCount)
				{
					Logger.TraceLog("Added all block ids, sending message");
					SendOutgoingMessages(bytes, sync);
				}
			}

			_outgoingMessages.Clear();
			bytes.Clear();
			ResourcePool.Return(bytes);
		}

		private static void SendOutgoingMessages(List<byte> bytes, SyncMessage sync)
		{
			Logger.TraceLog("SyncMessage: " + sync + ", byte count: " + bytes.Count);
			bool result = sync.recipient.HasValue
				? MyAPIGateway.Multiplayer.SendMessageTo(MessageHandler.ModId, bytes.ToArray(), sync.recipient.Value)
				: MyAPIGateway.Multiplayer.SendMessageToOthers(MessageHandler.ModId, bytes.ToArray());
			if (!result)
				Logger.AlwaysLog("Failed to send message, length: " + bytes.Count, Logger.severity.ERROR);
		}

		protected readonly Id _id;
		protected readonly bool _save;

		protected ASync(Type scriptType, string valueId, bool save = true)
		{
			this._id = GetId(scriptType, valueId);
			this._save = save;

			if (_syncs == null)
				return;
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
		/// <param name="blockId">EntityId of the block</param>
		/// <param name="value">The value as a string</param>
		public abstract void SetValue(long blockId, string value);

		/// <summary>
		/// Send a value to other game clients.
		/// </summary>
		/// <param name="blockId">EntityId of the block</param>
		/// <param name="value">The value to send</param>
		/// <param name="clientId">Id of the client to send to, null = all</param>
		protected void SendValue(long blockId, object value, ulong? clientId = null)
		{
			if (_outgoingMessages == null)
				return;

			traceLog("entered, blockId: " + blockId + ", value: " + value + ", clientId: " + clientId);
			_outgoingMessages.AddSyncMessage(clientId, _id, value, blockId);
		}

		protected void LogMissingFromRegistrar(long blockId, bool network, [CallerFilePath] string filePath = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			alwaysLog("block not found in Registrar: " + blockId, network ? Logger.severity.WARNING : Logger.severity.ERROR, _id.ToString(), filePath: filePath, member: member, lineNumber: lineNumber);
		}

		/// <summary>The type of value contained.</summary>
		protected abstract Type ValueType { get; }

		/// <summary>
		/// Set the value from network message.
		/// </summary>
		/// <param name="blockId">EntityId of the block</param>
		/// <param name="value">The value as object</param>
		protected abstract void SetValue(SyncMessage sync);

		protected abstract IEnumerable<KeyValuePair<long, object>> AllValues();

		protected override sealed string GetContext()
		{
			return _id.ToString();
		}

	}
}
