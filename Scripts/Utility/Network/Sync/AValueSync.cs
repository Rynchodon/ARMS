#if DEBUG
#define TRACE
#endif

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Sandbox.ModAPI;

namespace Rynchodon.Utility.Network
{
	/// <summary>
	/// Objects of this type synchronize and save values.
	/// </summary>
	/// <typeparam name="TValue">The type of value to sync</typeparam>
	/// <typeparam name="TScript">The type of script to get/set the value from/to</typeparam>
	public abstract class AValueSync<TValue, TScript> : ASync
	{

		private class OutgoingMessage
		{
			public readonly ulong? ClientId;
			public readonly TValue Value;
			public readonly List<long> EntityId = new List<long>();

			public OutgoingMessage(TValue Value, long EntityId, ulong? ClientId)
			{
				this.Value = Value;
				this.EntityId.Add(EntityId);
				this.ClientId = ClientId;
			}
		}
		
		public delegate TValue GetterDelegate(TScript script);
		public delegate void SetterDelegate(TScript script, TValue value);

		protected readonly GetterDelegate _getter;
		protected readonly SetterDelegate _setter;
		private readonly TValue _defaultValue;

		/// <summary>Values for which scripts do not yet exist.</summary>
		private Dictionary<long, TValue> _orphanValues;
		/// <summary>The currently queued outgoing message.</summary>
		private OutgoingMessage _outgoing;

		protected virtual IEqualityComparer<TValue> EqualityComparer { get { return EqualityComparer<TValue>.Default; } }

		/// <param name="valueId">Identifier for the value</param>
		/// <param name="save">Save the value to disk.</param>
		public AValueSync(string valueId, GetterDelegate getter, SetterDelegate setter, bool save = true, TValue defaultValue = default(TValue))
			: base(typeof(TScript), valueId, save)
		{
			_defaultValue = defaultValue;
			_getter = getter;
			_setter = setter;
			RegisterSetDefaultValue();

#if DEBUG
			if (typeof(TValue) == typeof(StringBuilder))
				_getter = script => {
					TValue value = getter(script);
					if (value == null)
						alwaysLog("Returning null StringBuilder", Logger.severity.FATAL);
					return value;
				};
#endif
		}

		public AValueSync(string valueId, string fieldName, bool save = true)
			: base(typeof(TScript), valueId, save)
		{
			Type type = typeof(TScript);

			FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null)
			{
				_getter = GenerateGetter(field);
				_setter = GenerateSetter(field);

				DefaultValueAttribute defaultAtt = field.GetCustomAttribute<DefaultValueAttribute>();
				_defaultValue = defaultAtt != null ? (TValue)defaultAtt.Value : default(TValue);

				traceLog("default value: " + _defaultValue, condition: defaultAtt != null);

				RegisterSetDefaultValue();
				return;
			}

			throw new ArgumentException(fieldName + " does not match any instance field of " + type.Name);
		}

		/// <summary>
		/// Generate a GetterDelegate for a field.
		/// </summary>
		/// <param name="field">The field to generate the getter for.</param>
		/// <returns>A GetterDelegate for field.</returns>
		private static GetterDelegate GenerateGetter(FieldInfo field)
		{
			DynamicMethod getter = new DynamicMethod(field.DeclaringType.Name + ".get_" + field.Name, field.FieldType, new Type[] { typeof(TScript) }, true);
			ILGenerator il = getter.GetILGenerator();
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldfld, field);
			il.Emit(OpCodes.Ret);
			return (GetterDelegate)getter.CreateDelegate(typeof(GetterDelegate));
		}

		/// <summary>
		/// Generate a SetterDelegate for a field.
		/// </summary>
		/// <param name="field">The field to generate the setter for.</param>
		/// <returns>A SetterDelegate for field.</returns>
		private static SetterDelegate GenerateSetter(FieldInfo field)
		{
			DynamicMethod setter = new DynamicMethod(field.DeclaringType.Name + ".set_" + field.Name, null, new Type[] { typeof(TScript), typeof(TValue) }, true);
			ILGenerator il = setter.GetILGenerator();
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldarg_1);
			il.Emit(OpCodes.Stfld, field);
			il.Emit(OpCodes.Ret);
			return (SetterDelegate)setter.CreateDelegate(typeof(SetterDelegate));
		}

		private void RegisterSetDefaultValue()
		{
			if (!EqualityComparer.Equals(GetDefaultValue(), (default(TValue))))
				Registrar.AddAfterScriptAdded<TScript>(SetDefaultValue);
		}

		private void SetDefaultValue(long entityId, TScript script)
		{
			_setter(script, GetDefaultValue());
		}

		protected virtual TValue GetDefaultValue()
		{
			return _defaultValue;
		}

		/// <summary>
		/// Get the locally stored value from a block.
		/// </summary>
		/// <param name="block">The block to get the value for.</param>
		/// <returns>The local value from a block.</returns>
		public TValue GetValue(IMyTerminalBlock block)
		{
			return GetValue(block.EntityId);
		}

		/// <summary>
		/// Get the locally stored value for an entity with a specified ID
		/// </summary>
		/// <param name="entityId">Id of the entity to get the value for.</param>
		/// <returns>The local value for an entity with the specified ID</returns>
		public TValue GetValue(long entityId)
		{
			TScript script;
			if (Registrar.TryGetValue(entityId, out script))
				return _getter(script);

			if (!Globals.WorldClosed)
				LogMissingFromRegistrar(entityId, false);
			return GetDefaultValue();
		}

		/// <summary>
		/// Get the locally stored value from a script.
		/// </summary>
		/// <param name="script">The script to get the value from.</param>
		/// <returns>The locally stored value from the script.</returns>
		public TValue GetValue(TScript script)
		{
			return _getter(script);
		}

		/// <summary>
		/// Set and synchronize the value for a block. Thread Safe.
		/// </summary>
		/// <param name="block">The block whose value is being set.</param>
		/// <param name="value">The value to set.</param>
		public void SetValue(IMyTerminalBlock block, TValue value)
		{
			SetValue(block.EntityId, value, true);
		}

		/// <summary>
		/// Set and synchronize the value for an entity ID. Thread Safe.
		/// </summary>
		/// <param name="entityId">The ID of the entity whose value is to be set.</param>
		/// <param name="value">The value to set.</param>
		public void SetValue(long entityId, TValue value)
		{
			SetValue(entityId, value, true);
		}

		/// <summary>
		/// Set and, optionally, synchronize a value for an entity ID. Thread Safe.
		/// </summary>
		/// <param name="entityId">The ID of the entity whose value is being set.</param>
		/// <param name="value">The new value.</param>
		/// <param name="send">Iff true and the value has changed, send to to other game clients.</param>
		protected void SetValue(long entityId, TValue value, bool send)
		{
			if (!Threading.ThreadTracker.IsGameThread)
			{
				MyAPIGateway.Utilities.InvokeOnGameThread(() => SetValue(entityId, value, send));
				return;
			}

			TScript script;
			if (Registrar.TryGetValue(entityId, out script))
			{
				SetValue(entityId, script, value, send);
				return;
			}

			if (!Globals.WorldClosed)
				LogMissingFromRegistrar(entityId, false);
		}

		/// <summary>
		/// Get a TValue from a string. Throws an exception if value is of incorrect format.
		/// </summary>
		/// <param name="value">The value as a string.</param>
		/// <returns>TValue representation of a string.</returns>
		protected TValue FromString(string value)
		{
			return typeof(Enum).IsAssignableFrom(typeof(TValue))
				? (TValue)Enum.Parse(typeof(TValue), value)
				: typeof(IConvertible).IsAssignableFrom(typeof(TValue))
				? (TValue)Convert.ChangeType(value, typeof(TValue))
				: typeof(StringBuilder).IsAssignableFrom(typeof(TValue))
				? (TValue)(object)new StringBuilder(value)
				: MyAPIGateway.Utilities.SerializeFromXML<TValue>(value);
		}

		/// <summary>
		/// Set value from network message. Does not send.
		/// </summary>
		/// <param name="message">Received bytes.</param>
		/// <param name="position">Position in message to start reading at.</param>
		protected override sealed void SetValueFromNetwork(byte[] message, int position)
		{
			TValue value = ByteConverter.GetOfType<TValue>(message, ref position);
			List<long> orphanIds = null;

			while (position < message.Length)
			{
				long entityId = ByteConverter.GetLong(message, ref position);
				TrySet(entityId, value, ref orphanIds);
			}
			SetOrphan(value, orphanIds);
		}

		private void TrySet(long entityId, TValue value, ref List<long> orphanIds)
		{
			TScript script;
			if (Registrar.TryGetValue(entityId, out script))
			{
				traceLog("got script, setting value. entityId: " + entityId + ", value: " + value);
				SetValue(entityId, script, value, false);
			}
			else
			{
				if (orphanIds == null)
					orphanIds = new List<long>();
				orphanIds.Add(entityId);
			}
		}

		private void SetOrphan(TValue value, List<long> orphanIds)
		{
			if (orphanIds != null)
			{
				if (_orphanValues == null)
				{
					_orphanValues = new Dictionary<long, TValue>();
					Registrar.AddAfterScriptAdded<TScript>(CheckForOrphan);
				}
				traceLog("orphans: " + orphanIds.Count);
				foreach (long orphan in orphanIds)
					_orphanValues.Add(orphan, value);
			}
		}

		/// <summary>
		/// Search for orphan values to add to a script.
		/// </summary>
		/// <param name="entityId">ID of the entity the script belongs to.</param>
		/// <param name="script">The script to add the orphan value to, if one is found.</param>
		private void CheckForOrphan(long entityId, TScript script)
		{
			if (_orphanValues == null)
			{
				debugLog("no orphan values, unregister", Logger.severity.WARNING);
				RemoveCheckForOrphan();
				return;
			}

			TValue value;
			if (!_orphanValues.TryGetValue(entityId, out value))
			{
				traceLog("no orphan value for entity: " + entityId);
				return;
			}

			SetValue(entityId, script, value, false);
			_orphanValues.Remove(entityId);

			if (_orphanValues.Count == 0)
			{
				debugLog("no more orphan values");
				RemoveCheckForOrphan();
				_orphanValues = null;
			}
		}

		/// <summary>
		/// Delayed removal of CheckForOrphan from Registrar.AfterScriptAdded.
		/// </summary>
		private void RemoveCheckForOrphan()
		{
			// it's locked right now, remove later
			MyAPIGateway.Utilities.InvokeOnGameThread(() => Registrar.RemoveAfterScriptAdded<TScript>(CheckForOrphan));
		}

		#region SendValue

		/// <summary>
		/// Send all the values to a specified client.
		/// </summary>
		/// <param name="clientId">The client that needs the values.</param>
		protected override sealed void SendAllToClient(ulong clientId)
		{
			if (!MyAPIGateway.Multiplayer.IsServer)
			{
				alwaysLog("Cannot send values, this is not the server", Logger.severity.ERROR);
				return;
			}

			foreach (KeyValuePair<long, TScript> pair in Registrar.IdScripts<TScript>())
			{
				TValue value = _getter(pair.Value);
				if (!IsDefault(value))
					SendValue(pair.Key, value, clientId);
			}
		}

		/// <summary>
		/// Send a value to a specific client, or broadcast it to all clients.
		/// </summary>
		/// <param name="entityId">ID of the entity whose value is to be sent.</param>
		/// <param name="value">The value to send.</param>
		/// <param name="clientId">If null, broadcast to all. Otherwise, send to the client with the specified ID.</param>
		/// <remarks>
		/// The value is not sent immediately, it is queued briefly so that multiple outgoing values may be combined.
		/// </remarks>
		protected void SendValue(long entityId, TValue value, ulong? clientId = null)
		{
			if (_outgoing == null)
			{
				_outgoing = new OutgoingMessage(value, entityId, clientId);
				MyAPIGateway.Utilities.InvokeOnGameThread(SendOutgoing);
			}
			else if (_outgoing.ClientId == clientId && EqualityComparer.Equals(_outgoing.Value, value))
			{
				_outgoing.EntityId.Add(entityId);
			}
			else
			{
				SendOutgoing();
				_outgoing = new OutgoingMessage(value, entityId, clientId);
			}
		}

		/// <summary>
		/// Send _outgoing immediately.
		/// </summary>
		private void SendOutgoing()
		{
			const int maxSize = 4088;

			List<byte> bytes; ResourcePool.Get(out bytes);
			AddIdAndValue(bytes);

			int initCount = bytes.Count;

			if (initCount > maxSize)
			{
				alwaysLog("Cannot send message, value is too large. byte count: " + initCount + ", value: " + _outgoing.Value);
				_outgoing = null;
				return;
			}

			for (int entityIdIndex = _outgoing.EntityId.Count - 1; entityIdIndex >= 0; --entityIdIndex)
			{
				ByteConverter.AppendBytes(bytes, _outgoing.EntityId[entityIdIndex]);
				if (bytes.Count > maxSize)
				{
					traceLog("Over max size, splitting message");
					SendOutgoing(bytes);
					AddIdAndValue(bytes);
				}
			}

			SendOutgoing(bytes);
			_outgoing = null;
			bytes.Clear();
			ResourcePool.Return(bytes);
		}

		/// <summary>
		/// Clear bytes, add SudMod, _id, and _outgoing.Value.
		/// </summary>
		/// <param name="bytes">Message being prepared.</param>
		private void AddIdAndValue(List<byte> bytes)
		{
			bytes.Clear();
			ByteConverter.AppendBytes(bytes, MessageHandler.SubMod.Sync);
			ByteConverter.AppendBytes(bytes, _id);
			ByteConverter.AppendBytes(bytes, _outgoing.Value);
		}

		/// <summary>
		/// Send bytes to a specific client or to all clients, dependant upon _outgoing.
		/// </summary>
		/// <param name="bytes">The message to send.</param>
		private void SendOutgoing(List<byte> bytes)
		{
			traceLog("sending to: " + _outgoing.ClientId + ", value: " + _outgoing.Value + ", entities: " + string.Join(",", _outgoing.EntityId));
			bool result = _outgoing.ClientId.HasValue
				? MyAPIGateway.Multiplayer.SendMessageTo(MessageHandler.ModId, bytes.ToArray(), _outgoing.ClientId.Value)
				: MyAPIGateway.Multiplayer.SendMessageToOthers(MessageHandler.ModId, bytes.ToArray());
			if (!result)
				alwaysLog("Failed to send message, length: " + bytes.Count, Logger.severity.ERROR);
		}

		#endregion

		#region Save/Load

		protected override sealed void WriteToSave(List<byte> bytes)
		{
			if (!MyAPIGateway.Multiplayer.IsServer)
			{
				alwaysLog("Cannot write values, this is not the server", Logger.severity.ERROR);
				return;
			}

			Dictionary<TValue, List<long>> values = new Dictionary<TValue, List<long>>();
			foreach (KeyValuePair<long, TScript> pair in Registrar.IdScripts<TScript>())
			{
				TValue v = _getter(pair.Value);
				if (IsDefault(v))
					continue;

				List<long> ids;
				if (!values.TryGetValue(v, out ids))
				{
					ids = new List<long>();
					values.Add(v, ids);
				}

				ids.Add(pair.Key);
			}

			ByteConverter.AppendBytes(bytes, values.Count);
			foreach (KeyValuePair<TValue, List<long>> valueEntity in values)
			{
				ByteConverter.AppendBytes(bytes, valueEntity.Key);
				ByteConverter.AppendBytes(bytes, valueEntity.Value.Count);
				foreach (long entity in valueEntity.Value)
					ByteConverter.AppendBytes(bytes, entity);
			}
		}

		protected override sealed void ReadFromSave(byte[] bytes, ref int position)
		{
			if (!MyAPIGateway.Multiplayer.IsServer)
			{
				alwaysLog("Cannot read values, this is not the server", Logger.severity.ERROR);
				return;
			}

			List<long> orphanIds = new List<long>();

			int valueCount = ByteConverter.GetInt(bytes, ref position);
			for (int valueIndex = 0; valueIndex < valueCount; ++valueIndex)
			{
				orphanIds.Clear();
				TValue value = ByteConverter.GetOfType<TValue>(bytes, ref position);
				int entityCount = ByteConverter.GetInt(bytes, ref position);
				for (int entityIndex = 0; entityIndex < entityCount; ++entityIndex)
				{
					long entity = ByteConverter.GetLong(bytes, ref position);
					TrySet(entity, value, ref orphanIds);
				}
				SetOrphan(value, orphanIds);
			}
		}

		/// <summary>
		/// Set value from saved string. Does not send.
		/// </summary>
		/// <param name="entityId">Id of the script's entity</param>
		/// <param name="value">The value as a string.</param>
		public override sealed void SetValueFromSave(long entityId, string value)
		{
			TValue v = FromString(value);
			List<long> orphanIds = null;
			TrySet(entityId, v, ref orphanIds);
			SetOrphan(v, orphanIds);
		}

		#endregion

		/// <summary>
		/// Tests if a value is the default value. Default values are not sent when all values are requested.
		/// </summary>
		/// <param name="value">The value to test.</param>
		/// <returns>True iff value equals the default value.</returns>
		protected virtual bool IsDefault(TValue value)
		{
			return EqualityComparer.Equals(value, GetDefaultValue());
		}

		/// <summary>
		/// Sets the value in the script.
		/// </summary>
		/// <param name="entityId">Id of the entity of the script</param>
		/// <param name="script">The script to set the value of</param>
		/// <param name="value">The new value</param>
		/// <param name="send">Value was set locally and may need to be broadcast</param>
		protected abstract void SetValue(long entityId, TScript script, TValue value, bool send);

	}
}
