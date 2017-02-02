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
	/// Objects of this type synchronize and save terminal controls.
	/// </summary>
	/// TODO: saving
	/// TODO: default value
	public abstract partial class TerminalSync
	{

		public enum Id : byte
		{
			None,
			ProgrammableBlock_HandleDetected,
			ProgrammableBlock_BlockList,
			Solar_FaceSun,
			TextPanel_Option
		}

		public struct SyncMessage
		{
			public ulong? recipient;
			public TerminalSync.Id id;
			public object value;
			public List<long> blockId;

			public SyncMessage(ulong? recipient, TerminalSync.Id id, object value, long blockId)
			{
				this.recipient = recipient;
				this.id = id;
				this.value = value;
				this.blockId = new List<long>();
				this.blockId.Add(blockId);
			}

			public override string ToString()
			{
				return "recipient: " + recipient + ", id: " + id + ", value: " + value + ", blockId: " + string.Join(",", blockId);
			}
		}

		protected static MyConcurrentPool<StringBuilder> _stringBuilderPool = new MyConcurrentPool<StringBuilder>();
		/// <summary>Values for which scripts do not yet exist.</summary>
		protected static List<SyncMessage> _orphanValues;

		private static bool _hasValues;
		private static List<SyncMessage> _outgoingMessages;
		private static Dictionary<Id, TerminalSync> _syncs;

		static TerminalSync()
		{
			MessageHandler.AddHandler(MessageHandler.SubMod.TerminalSync, HandleValue);
			MessageHandler.AddHandler(MessageHandler.SubMod.TerminalSyncRequest, HandleRequest);
		}

		[OnWorldLoad]
		private static void OnLoad()
		{
			_hasValues = MyAPIGateway.Multiplayer.IsServer;
			_outgoingMessages = new List<SyncMessage>();
			_syncs = new Dictionary<Id, TerminalSync>();

			UpdateManager.Register(1, SendOutgoingMessages);
		}

		[OnWorldClose]
		private static void OnClose()
		{
			_orphanValues = null;
			_outgoingMessages = null;
			_syncs = null;
		}

		public static bool TryGet(Id id, out TerminalSync sync)
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
				TerminalSync instance;
				if (!_syncs.TryGetValue(sync.id, out instance))
				{
					Logger.AlwaysLog("Missing " + typeof(TerminalSync).Name + " for " + sync.id, Logger.severity.ERROR);
					return;
				}
				Logger.TraceLog("got instance: " + instance.GetType().Name);

				Type type = instance.ValueType;
				Logger.TraceLog("type: " + type);
				sync.value = type == null ? null : ByteConverter.GetOfType(message, ref position, type);
				Logger.TraceLog("value: " + sync.value);

				sync.blockId = new List<long>((message.Length - position) / 8);
				while (position < message.Length)
					sync.blockId.Add(ByteConverter.GetLong(message, ref position));

				Logger.TraceLog("block id count: " + sync.blockId.Count);

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
				foreach (KeyValuePair<Id, TerminalSync> termSync in _syncs)
				{
					Logger.TraceLog("adding values for: " + termSync.Key);
					foreach (KeyValuePair<long, object> valuePair in termSync.Value.AllValues())
					{
						Logger.TraceLog("value from: " + valuePair.Key + " is " + valuePair.Value);
						_outgoingMessages.AddSyncMessage(recipient, termSync.Key, valuePair.Value, valuePair.Key);
					}
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
			ByteConverter.AppendBytes(bytes, MessageHandler.SubMod.TerminalSync);
			ByteConverter.AppendBytes(bytes, sync.id);
			ByteConverter.AppendBytes(bytes, sync.value);
		}

		private static void RequestValues()
		{
			if (MyAPIGateway.Multiplayer.IsServer)
				throw new Exception("Cannot request values, this is the server");

			List<byte> bytes; ResourcePool.Get(out bytes);
			bytes.Clear();

			ByteConverter.AppendBytes(bytes, MessageHandler.SubMod.TerminalSyncRequest);
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

				for (int blockIdIndex = sync.blockId.Count - 1; blockIdIndex >= 0; --blockIdIndex)
				{
					ByteConverter.AppendBytes(bytes, sync.blockId[blockIdIndex]);
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
		protected readonly Logger _logger;

		protected TerminalSync(Id id, bool save = true)
		{
			this._id = id;
			this._save = save;
			this._logger = new Logger(_id.ToString);

			if (_syncs == null)
				return;
			_syncs.Add(id, this);

			if (!_hasValues)
			{
				_hasValues = true;
				RequestValues();
			}

			_logger.traceLog("initialized");
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

			_logger.traceLog("entered, blockId: " + blockId + ", value: " + value + ", clientId: " + clientId);
			_outgoingMessages.AddSyncMessage(clientId, _id, value, blockId);
		}

		protected void LogMissingFromRegistrar(long blockId, bool network, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			_logger.alwaysLog("block not found in Registrar: " + blockId, network ? Logger.severity.WARNING : Logger.severity.ERROR, _id.ToString(), member: member, lineNumber: lineNumber);
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

	}
}
