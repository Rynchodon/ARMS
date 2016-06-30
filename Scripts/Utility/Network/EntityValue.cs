using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;
using Sandbox.ModAPI;
using VRage;
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

		protected static Dictionary<long, Dictionary<byte, EntityValue>> allEntityValues = new Dictionary<long, Dictionary<byte, EntityValue>>();
		protected static Logger logger = new Logger("SyncEntityValue");

		static EntityValue()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
			MessageHandler.Handlers.Add(MessageHandler.SubMod.SyncEntityValue, Handle_SyncEntityValue);
			if (MyAPIGateway.Multiplayer.IsServer)
				MessageHandler.Handlers.Add(MessageHandler.SubMod.RequestEntityValue, Handle_RequestEntityValue);
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			allEntityValues = null;
			logger = null;
		}

		public static Builder_EntityValues[] GetBuilders()
		{
			List<Builder_EntityValues> all = new List<Builder_EntityValues>(allEntityValues.Count);
			List<byte> valueIds = new List<byte>();
			List<string> values = new List<string>();

			foreach (KeyValuePair<long, Dictionary<byte, EntityValue>> entityValues in allEntityValues)
			{
				valueIds.Clear();
				values.Clear();

				foreach (KeyValuePair<byte, EntityValue> byteValue in entityValues.Value)
				{
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
			foreach (Builder_EntityValues builder in savedValues)
			{
				Dictionary<byte, EntityValue> entityValues;
				if (!allEntityValues.TryGetValue(builder.entityId, out entityValues))
				{
					logger.alwaysLog("Entity not found in world: " + builder.entityId, Logger.severity.WARNING);
					continue;
				}
				for (int index = 0; index < builder.valueIds.Length; index++)
				{
					EntityValue value;
					if (!entityValues.TryGetValue(builder.valueIds[index], out value))
					{
						logger.alwaysLog("Entity value: " + builder.valueIds[index] + " is missing for " + builder.entityId, Logger.severity.WARNING);
						continue;
					}
					if (!string.IsNullOrEmpty(builder.values[index]))
						value.SetValue(builder.values[index]);
				}
			}
		}

		private static void Handle_SyncEntityValue(byte[] bytes, int pos)
		{
			long entityId = ByteConverter.GetLong(bytes, ref pos);
			byte valueId = ByteConverter.GetByte(bytes, ref pos);

			Dictionary<byte, EntityValue> entityValues;
			if (!allEntityValues.TryGetValue(entityId, out entityValues))
			{
				logger.alwaysLog("Failed lookup of entity id: " + entityId, Logger.severity.WARNING);
				return;
			}

			EntityValue instance;
			if (!entityValues.TryGetValue(valueId, out instance))
			{
				logger.alwaysLog("Failed lookup of value id: " + valueId + ", entityId: " + entityId, Logger.severity.ERROR);
				return;
			}

			instance.SetValue(bytes, ref pos);
		}

		private static void Handle_RequestEntityValue(byte[] bytes, int pos)
		{
			long entityId = ByteConverter.GetLong(bytes, ref pos);
			ulong recipient = ByteConverter.GetUlong(bytes, ref pos);

			Dictionary<byte, EntityValue> entityValues;
			if (!allEntityValues.TryGetValue(entityId, out entityValues))
			{
				logger.alwaysLog("Failed lookup of entity id: " + entityId, Logger.severity.WARNING);
				return;
			}

			foreach (EntityValue instance in entityValues.Values)
				instance.SendValue(recipient);
		}

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
			if (allEntityValues != null)
				allEntityValues.Remove(obj.EntityId);
		}

		protected readonly long m_entityId;
		protected readonly byte m_valueId;
		protected readonly Action m_afterValueChanged;

		protected T m_value;
		protected bool m_synced;

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
		public EntityValue(IMyEntity entity, byte valueId, Action afterValueChanged = null, T defaultValue = default(T))
		{
			this.m_entityId = entity.EntityId;
			this.m_valueId = valueId;
			this.m_value = defaultValue;
			this.m_afterValueChanged = afterValueChanged;
			this.m_synced = MyAPIGateway.Multiplayer.IsServer;

			Dictionary<byte, EntityValue> entityValues;
			if (!allEntityValues.TryGetValue(m_entityId, out entityValues))
			{
				entityValues = new Dictionary<byte, EntityValue>();
				allEntityValues.Add(m_entityId, entityValues);
				entity.OnClose += entity_OnClose;
			}

			EntityValue existing;
			if (entityValues.TryGetValue(valueId, out existing))
			{
				if (GetValueType() == existing.GetValueType())
				{
					logger.alwaysLog("valueId: " + valueId + ", already used for entity: " + entity.nameWithId() + ". types match: " + GetValueType(), Logger.severity.WARNING);
					return;
				}
				else
					// types don't match, if it gets sent, the server would crash!
					throw new Exception("valueId: " + valueId + ", already used for entity: " + entity.nameWithId() + ", this type: " + GetValueType() + ", existing: " + existing.GetValueType());
			}

			entityValues.Add(valueId, this);
		}

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
					logger.alwaysLog("Failed to send message, length: " + bytes.Count + ", value: " + m_value, Logger.severity.ERROR, m_entityId.ToString(), m_valueId.ToString());
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
			m_afterValueChanged.InvokeIfExists();
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
			{
				m_value = MyAPIGateway.Utilities.SerializeFromXML<T>(value);
				return;
			}
			m_value = (T)Convert.ChangeType(value, code);
		}

		private void RequestEntityValueFromServer()
		{
			logger.debugLog(MyAPIGateway.Multiplayer.IsServer, "This is the server!", Logger.severity.ERROR);
			List<byte> bytes = ResourcePool<List<byte>>.Get();
			try
			{
				ByteConverter.AppendBytes(bytes, (byte)MessageHandler.SubMod.RequestEntityValue);
				ByteConverter.AppendBytes(bytes, m_entityId);
				ByteConverter.AppendBytes(bytes, MyAPIGateway.Multiplayer.MyId);
				if (!MyAPIGateway.Multiplayer.SendMessageToServer(bytes.ToArray()))
					logger.alwaysLog("Failed to send message, length: " + bytes.Count, Logger.severity.ERROR, m_entityId.ToString(), m_valueId.ToString());
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

		private byte m_updatesSinceValueChange = byte.MaxValue;

		/// <param name="valueId">Each value for a block must have a unique ID, these are saved to disk.</param>
		public EntityStringBuilder(IMyEntity entity, byte valueId, Action afterValueChanged)
			: base(entity, valueId, afterValueChanged)
		{
			m_value = new StringBuilder();
			Update.UpdateManager.Register(100, Update100, entity);
		}

		protected override void SetValue(byte[] bytes, ref int pos)
		{
			m_synced = true;
			m_value = new StringBuilder(ByteConverter.GetString(bytes, ref pos));
			m_afterValueChanged.InvokeIfExists();
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
			m_afterValueChanged.InvokeIfExists();
		}

		private void Update100()
		{
			if (m_updatesSinceValueChange > 2)
				return;

			if (m_updatesSinceValueChange == 2)
			{
				m_afterValueChanged.InvokeIfExists();
				SendValue();
			}
			m_updatesSinceValueChange++;
		}

	}

}
