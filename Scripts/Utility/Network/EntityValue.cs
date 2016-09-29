using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;
using Rynchodon.Update;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.ModAPI;

namespace Rynchodon.Utility.Network
{

	public abstract class EntityValue
	{

		[Serializable]
		public class Builder_EntityValues
		{
			[XmlAttribute]
			public long entityId;
			public byte[] valueIds;
			public string[] values;
		}

		protected class StaticVariables
		{
			public Dictionary<long, Dictionary<byte, EntityValue>> allEntityValues = new Dictionary<long, Dictionary<byte, EntityValue>>();
			public Logger logger = new Logger();
			public MyConcurrentPool<StringBuilder> updateSB = new MyConcurrentPool<StringBuilder>();
			public int bytesSent, messagesSent;
		}

		protected static StaticVariables Static = new StaticVariables();

		static EntityValue()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
			MessageHandler.Handlers.Add(MessageHandler.SubMod.SyncEntityValue, Handle_SyncEntityValue);
			if (MyAPIGateway.Multiplayer.IsServer)
				MessageHandler.Handlers.Add(MessageHandler.SubMod.RequestEntityValue, Handle_RequestEntityValue);
		}

		private static void Entities_OnCloseAll()
		{
			Static.logger.debugLog("bytes sent: " + Static.bytesSent + ", messages sent: " + Static.messagesSent, Logger.severity.INFO);
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			Static = null;
		}

		public static Builder_EntityValues[] GetBuilders()
		{
			if (Static == null)
				return null;

			List<Builder_EntityValues> all = new List<Builder_EntityValues>(Static.allEntityValues.Count);
			List<byte> valueIds = new List<byte>();
			List<string> values = new List<string>();

			foreach (KeyValuePair<long, Dictionary<byte, EntityValue>> entityValues in Static.allEntityValues)
			{
				valueIds.Clear();
				values.Clear();

				foreach (KeyValuePair<byte, EntityValue> byteValue in entityValues.Value)
				{
					if (!byteValue.Value.Save)
						continue;
					string value = byteValue.Value.GetValue();
					if (value != null)
					{
						valueIds.Add(byteValue.Key);
						values.Add(value);
					}
				}

				if (valueIds.Count != 0)
				{
					all.Add(new Builder_EntityValues()
					{
						entityId = entityValues.Key,
						valueIds = valueIds.ToArray(),
						values = values.ToArray()
					});
				}
			}

			return all.ToArray();
		}

		public static void ResumeFromSave(Builder_EntityValues[] savedValues)
		{
			if (Static == null)
				return;

			foreach (Builder_EntityValues builder in savedValues)
			{
				Dictionary<byte, EntityValue> entityValues;
				if (!Static.allEntityValues.TryGetValue(builder.entityId, out entityValues))
				{
					Static.logger.alwaysLog("Entity not found in world: " + builder.entityId, Logger.severity.WARNING);
					continue;
				}
				for (int index = 0; index < builder.valueIds.Length; index++)
				{
					EntityValue value;
					if (!entityValues.TryGetValue(builder.valueIds[index], out value))
					{
						IMyEntity entity;
						if (MyAPIGateway.Entities.TryGetEntityById(builder.entityId, out entity))
							Static.logger.alwaysLog("Entity value: " + builder.valueIds[index] + " is missing for " + entity.nameWithId(), Logger.severity.WARNING);
						else
							Static.logger.alwaysLog("Entity value: " + builder.valueIds[index] + " is missing for " + builder.entityId, Logger.severity.WARNING);
						continue;
					}
					if (!string.IsNullOrEmpty(builder.values[index]))
						value.SetValue(builder.values[index]);
				}
			}
		}

		public static EntityValue TryGetEntityValue(long entityId, byte valueId)
		{
			if (Static == null)
				return null;

			Dictionary<byte, EntityValue> entityValues;
			if (!Static.allEntityValues.TryGetValue(entityId, out entityValues))
				return null;

			EntityValue value;
			if (!entityValues.TryGetValue(valueId, out value))
				return null;

			return value;
		}

		private static void Handle_SyncEntityValue(byte[] bytes, int pos)
		{
			if (Static == null)
				return;

			long entityId = ByteConverter.GetLong(bytes, ref pos);
			byte valueId = ByteConverter.GetByte(bytes, ref pos);

			Dictionary<byte, EntityValue> entityValues;
			if (!Static.allEntityValues.TryGetValue(entityId, out entityValues))
			{
				Static.logger.alwaysLog("Failed lookup of entity id: " + entityId, Logger.severity.WARNING);
				return;
			}

			EntityValue instance;
			if (!entityValues.TryGetValue(valueId, out instance))
			{
				Static.logger.alwaysLog("Failed lookup of value id: " + valueId + ", entityId: " + entityId, Logger.severity.ERROR);
				return;
			}

			instance.SetValue(bytes, ref pos);
		}

		private static void Handle_RequestEntityValue(byte[] bytes, int pos)
		{
			if (Static == null)
				return;

			long entityId = ByteConverter.GetLong(bytes, ref pos);
			ulong recipient = ByteConverter.GetUlong(bytes, ref pos);

			Dictionary<byte, EntityValue> entityValues;
			if (!Static.allEntityValues.TryGetValue(entityId, out entityValues))
			{
				Static.logger.alwaysLog("Failed lookup of entity id: " + entityId, Logger.severity.WARNING);
				return;
			}

			foreach (EntityValue instance in entityValues.Values)
				instance.SendValue(recipient);
		}

		[System.Diagnostics.Conditional("LOG_ENABLED")]
		protected static void RecordBytesSent<T>(EntityValue<T> sender, ICollection<byte> bytes)
		{
			Static.logger.debugLog("entity: " + sender.m_entityId + ", valueID: " + sender.m_valueId + ", value: " + sender.Value + ", bytes: " + bytes.Count);
			Static.bytesSent += bytes.Count;
			Static.messagesSent++;
		}

		protected abstract bool Save { get; }
		protected abstract void SendValue(ulong? clientId = null);
		protected abstract void SetValue(byte[] bytes, ref int pos);
		protected abstract string GetValue();
		protected abstract void SetValue(string value);
		public abstract Type GetValueType();

	}

	public class EntityValue<T> : EntityValue
	{

		private static void entity_OnClose(IMyEntity obj)
		{
			if (Static != null)
				Static.allEntityValues.Remove(obj.EntityId);
		}

		public readonly long m_entityId;
		public readonly byte m_valueId;
		public readonly bool m_save;

		protected readonly Action<EntityValue<T>> m_afterValueChanged;
		protected T m_value;
		protected bool m_synced;

		protected override bool Save
		{
			get { return m_save; }
		}

		public virtual T Value
		{
			get
			{
				if (!m_synced)
				{
					m_synced = true;
					RequestEntityValueFromServer();
				}
				return m_value;
			}
			set
			{
				if (m_value.Equals(value))
					return;
				m_synced = true;
				m_value = value;
				SendValue();
				// set value will invoke m_afterValueChanged
			}
		}

		/// <param name="valueId">Each value for a block must have a unique ID, these are saved to disk.</param>
		public EntityValue(IMyEntity entity, byte valueId, Action<EntityValue<T>> afterValueChanged = null, T defaultValue = default(T), bool save = true)
		{
			this.m_entityId = entity.EntityId;
			this.m_valueId = valueId;
			this.m_value = defaultValue;
			this.m_afterValueChanged = afterValueChanged;
			this.m_save = save;
			this.m_synced = MyAPIGateway.Multiplayer.IsServer;

			Dictionary<byte, EntityValue> entityValues;
			if (!Static.allEntityValues.TryGetValue(m_entityId, out entityValues))
			{
				entityValues = new Dictionary<byte, EntityValue>();
				Static.allEntityValues.Add(m_entityId, entityValues);
				entity.OnClose += entity_OnClose;
			}

			EntityValue existing;
			if (entityValues.TryGetValue(valueId, out existing))
			{
				if (GetValueType() == existing.GetValueType())
				{
					Static.logger.alwaysLog("valueId: " + valueId + ", already used for entity: " + entity.nameWithId() + ". types match: " + GetValueType(), Logger.severity.WARNING);
					return;
				}
				else
					// types don't match, if it gets sent, the server would crash!
					throw new Exception("valueId: " + valueId + ", already used for entity: " + entity.nameWithId() + ", this type: " + GetValueType() + ", existing: " + existing.GetValueType());
			}

			entityValues.Add(valueId, this);
		}

		/// <param name="valueId">Each value for a block must have a unique ID, these are saved to disk.</param>
		public EntityValue(IMyEntity entity, byte valueId, Action afterValueChanged, T defaultValue = default(T), bool save = true)
			: this(entity, valueId, ev => afterValueChanged(), defaultValue, save) { }

		protected override void SendValue(ulong? clientId = null)
		{
			List<byte> bytes = ResourcePool<List<byte>>.Get();
			try
			{
				ByteConverter.AppendBytes(bytes, MessageHandler.SubMod.SyncEntityValue);
				ByteConverter.AppendBytes(bytes, m_entityId);
				ByteConverter.AppendBytes(bytes, m_valueId);
				ByteConverter.AppendBytes(bytes, m_value);
				bool result = clientId.HasValue ?
					MyAPIGateway.Multiplayer.SendMessageTo(MessageHandler.ModId, bytes.ToArray(), clientId.Value) :
					MyAPIGateway.Multiplayer.SendMessageToOthers(MessageHandler.ModId, bytes.ToArray());
				if (!result)
					Static.logger.alwaysLog("Failed to send message, length: " + bytes.Count + ", value: " + m_value, Logger.severity.ERROR, m_entityId.ToString(), m_valueId.ToString());
				RecordBytesSent(this, bytes);
			}
			finally
			{
				bytes.Clear();
				ResourcePool<List<byte>>.Return(bytes);
			}
		}

		protected override void SetValue(byte[] bytes, ref int pos)
		{
			m_synced = true;
			ByteConverter.GetOfType(bytes, ref pos, ref m_value);
			m_afterValueChanged.InvokeIfExists(this);
		}

		protected override string GetValue()
		{
			if (m_value.Equals(default(T)))
				return null;

			TypeCode code = Convert.GetTypeCode(m_value);
			if (code == TypeCode.Object)
				return MyAPIGateway.Utilities.SerializeToXML<T>(m_value);

			// enum must be changed to underlying type
			return Convert.ChangeType(m_value, code).ToString();
		}

		protected override void SetValue(string value)
		{
			TypeCode code = Convert.GetTypeCode(m_value);
			if (code == TypeCode.Object)
				m_value = MyAPIGateway.Utilities.SerializeFromXML<T>(value);
			else
				m_value = (T)Convert.ChangeType(value, code);
			m_afterValueChanged.InvokeIfExists(this);
		}

		private void RequestEntityValueFromServer()
		{
			Static.logger.debugLog("This is the server!", Logger.severity.ERROR, condition: MyAPIGateway.Multiplayer.IsServer);
			List<byte> bytes = ResourcePool<List<byte>>.Get();
			try
			{
				ByteConverter.AppendBytes(bytes, (byte)MessageHandler.SubMod.RequestEntityValue);
				ByteConverter.AppendBytes(bytes, m_entityId);
				ByteConverter.AppendBytes(bytes, MyAPIGateway.Multiplayer.MyId);
				if (!MyAPIGateway.Multiplayer.SendMessageToServer(bytes.ToArray()))
					Static.logger.alwaysLog("Failed to send message, length: " + bytes.Count, Logger.severity.ERROR, m_entityId.ToString(), m_valueId.ToString());
				RecordBytesSent(this, bytes);
			}
			finally
			{
				bytes.Clear();
				ResourcePool<List<byte>>.Return(bytes);
			}
		}

		public override Type GetValueType()
		{
			return typeof(T);
		}

	}

	public class EntityStringBuilder : EntityValue<StringBuilder>
	{

		/// <summary>
		/// set always generates network data, so use Update if the content may not have changed.
		/// </summary>
		public override StringBuilder Value
		{
			get
			{
				return base.Value;
			}
			set
			{
				m_synced = true;
				m_value = value;
				m_updatesSinceValueChange = 0;
			}
		}

		/// <summary>
		/// Update a new StringBuilder with the specified method. If the result is not equal to Value, replace Value with the new StringBuilder.
		/// </summary>
		/// <param name="updateMethod">Method to update Value from.</param>
		public void Update(Action<StringBuilder> updateMethod)
		{
			StringBuilder temp = Static.updateSB.Get(), current = Value;

			updateMethod.Invoke(temp);
			if (temp.EqualsIgnoreCapacity(current))
			{
				Static.logger.debugLog("equals, no send");
				temp.Clear();
				Static.updateSB.Return(temp);
			}
			else
			{
				Static.logger.debugLog("not equal, send data");
				Value = temp;
				current.Clear();
				Static.updateSB.Return(current);
			}
		}

		private byte m_updatesSinceValueChange = byte.MaxValue;

		/// <param name="valueId">Each value for a block must have a unique ID, these are saved to disk.</param>
		public EntityStringBuilder(IMyEntity entity, byte valueId, Action afterValueChanged = null, bool save = true)
			: base(entity, valueId, afterValueChanged, new StringBuilder(), save: save)
		{
			UpdateManager.Register(100, Update100, entity);
		}

		protected override void SetValue(byte[] bytes, ref int pos)
		{
			m_synced = true;
			m_value = new StringBuilder(ByteConverter.GetString(bytes, ref pos));
			m_afterValueChanged.InvokeIfExists(this);
		}

		protected override string GetValue()
		{
			if (m_value == null || m_value.Length == 0)
				return null;
			return m_value.ToString();
		}

		protected override void SetValue(string value)
		{
			m_value = new StringBuilder(value);
			m_afterValueChanged.InvokeIfExists(this);
		}

		private void Update100()
		{
			if (m_updatesSinceValueChange > 2)
				return;

			if (m_updatesSinceValueChange == 2)
			{
				m_afterValueChanged.InvokeIfExists(this);
				SendValue();
			}
			m_updatesSinceValueChange++;
		}

	}

}
