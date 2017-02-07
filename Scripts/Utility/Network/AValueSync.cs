using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Sandbox.ModAPI;
using VRage.Collections;

namespace Rynchodon.Utility.Network
{
	/// <summary>
	/// Objects of this type synchronize and save values.
	/// </summary>
	/// <typeparam name="TValue">The type of value to sync</typeparam>
	/// <typeparam name="TScript">The type of script to get/set the value from/to</typeparam>
	public abstract class AValueSync<TValue, TScript> : ASync
	{

		private struct Orphan
		{
			public TValue Value;
			public List<long> EntityId;

			public Orphan(TValue Value, List<long> EntityId)
			{
				this.Value = Value;
				this.EntityId = EntityId;
			}
		}

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
		protected readonly TValue _defaultValue;

		private List<Orphan> _orphanValues;
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

				return;
			}

			throw new ArgumentException(fieldName + " does not match any instance field of " + type.Name);
		}

		private static GetterDelegate GenerateGetter(FieldInfo field)
		{
			DynamicMethod getter = new DynamicMethod(field.DeclaringType.Name + ".get_" + field.Name, field.FieldType, new Type[] { typeof(TScript) }, true);
			ILGenerator il = getter.GetILGenerator();
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldfld, field);
			il.Emit(OpCodes.Ret);
			return (GetterDelegate)getter.CreateDelegate(typeof(GetterDelegate));
		}

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

		public TValue GetValue(IMyTerminalBlock block)
		{
			return GetValue(block.EntityId);
		}

		public TValue GetValue(long entityId)
		{
			TScript script;
			if (Registrar.TryGetValue(entityId, out script))
				return _getter(script);

			if (!Globals.WorldClosed)
				LogMissingFromRegistrar(entityId, false);
			return _defaultValue;
		}

		public TValue GetValue(TScript script)
		{
			return _getter(script);
		}

		public void SetValue(IMyTerminalBlock block, TValue value)
		{
			SetValue(block.EntityId, value, true);
		}

		public void SetValue(long entityId, TValue value)
		{
			SetValue(entityId, value, true);
		}

		protected void SetValue(long entityId, TValue value, bool send)
		{
			TScript script;
			if (Registrar.TryGetValue(entityId, out script))
			{
				SetValue(entityId, script, value, send);
				return;
			}

			if (!Globals.WorldClosed)
				LogMissingFromRegistrar(entityId, false);
		}

		protected override sealed void SetValueFromNetwork(byte[] message, int position)
		{
			TValue value = ByteConverter.GetOfType<TValue>(message, ref position);
			List<long> orphanIds = null;

			while (position < message.Length)
			{
				long entityId = ByteConverter.GetLong(message, ref position);
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

			if (orphanIds != null)
			{
				if (_orphanValues == null)
				{
					_orphanValues = new List<Orphan>();
					Registrar.AddAfterScriptAdded<TScript>(CheckForOrphan);
				}
				traceLog("orphans: " + orphanIds.Count);
				_orphanValues.Add(new Orphan(value, orphanIds));
			}
		}

		private void CheckForOrphan(long entityId, TScript script)
		{
			if (_orphanValues == null)
			{
				debugLog("no orphan values, unregister", Logger.severity.WARNING);
				RemoveCheckForOrphan();
				return;
			}

			for (int index = _orphanValues.Count - 1; index >= 0; --index)
			{
				Orphan orphan = _orphanValues[index];
				if (orphan.EntityId.Contains(entityId))
				{
					SetValue(entityId, script, orphan.Value, false);

					orphan.EntityId.Remove(entityId);
					if (orphan.EntityId.Count == 0)
					{
						debugLog("all id values have parents");
						_orphanValues.Remove(orphan);
						RemoveCheckForOrphan();
						if (_orphanValues.Count == 0)
						{
							debugLog("no more orphan values");
							_orphanValues = null;
						}
					}
					return;
				}
			}

			traceLog("no orphan value for block: " + entityId);
		}

		private void RemoveCheckForOrphan()
		{
			// it's locked right now, remove later
			MyAPIGateway.Utilities.InvokeOnGameThread(() => Registrar.RemoveAfterScriptAdded<TScript>(CheckForOrphan));
		}

		#region SendValue

		protected override void SendAllToClient(ulong clientId)
		{
			if (!MyAPIGateway.Multiplayer.IsServer)
			{
				Logger.AlwaysLog("Cannot send values, this is not the server", Logger.severity.ERROR);
				return;
			}

			foreach (KeyValuePair<long, TScript> pair in Registrar.IdScripts<TScript>())
			{
				TValue value = _getter(pair.Value);
				if (!IsDefault(value))
					SendValue(pair.Key, value, clientId);
			}
		}

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

		private void SendOutgoing()
		{
			const int maxSize = 4088;

			List<byte> bytes; ResourcePool.Get(out bytes);
			AddIdAndValue(bytes);

			int initCount = bytes.Count;

			if (initCount > maxSize)
			{
				Logger.AlwaysLog("Cannot send message, value is too large. byte count: " + initCount + ", value: " + _outgoing.Value);
				_outgoing = null;
				return;
			}

			for (int entityIdIndex = _outgoing.EntityId.Count - 1; entityIdIndex >= 0; --entityIdIndex)
			{
				ByteConverter.AppendBytes(bytes, _outgoing.EntityId[entityIdIndex]);
				if (bytes.Count > maxSize)
				{
					Logger.TraceLog("Over max size, splitting message");
					SendOutgoing(bytes);
					AddIdAndValue(bytes);
				}
			}

			SendOutgoing(bytes);
			_outgoing = null;
			bytes.Clear();
			ResourcePool.Return(bytes);
		}

		private void AddIdAndValue(List<byte> bytes)
		{
			bytes.Clear();
			ByteConverter.AppendBytes(bytes, MessageHandler.SubMod.Sync);
			ByteConverter.AppendBytes(bytes, _id);
			ByteConverter.AppendBytes(bytes, _outgoing.Value);
		}

		private void SendOutgoing(List<byte> bytes)
		{
			Logger.TraceLog("sending to: " + _outgoing.ClientId + ", value: " + _outgoing.Value + ", entities: " + string.Join(",", _outgoing.EntityId));
			bool result = _outgoing.ClientId.HasValue
			? MyAPIGateway.Multiplayer.SendMessageTo(MessageHandler.ModId, bytes.ToArray(), _outgoing.ClientId.Value)
			: MyAPIGateway.Multiplayer.SendMessageToOthers(MessageHandler.ModId, bytes.ToArray());
			if (!result)
				Logger.AlwaysLog("Failed to send message, length: " + bytes.Count, Logger.severity.ERROR);
		}

		#endregion

		protected virtual bool IsDefault(TValue value)
		{
			return EqualityComparer.Equals(value, _defaultValue);
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
