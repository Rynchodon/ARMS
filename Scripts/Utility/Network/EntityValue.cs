using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;

namespace Rynchodon.Utility.Network
{

	public abstract class EntityValue
	{
		protected static Dictionary<long, Dictionary<byte, EntityValue>> allEntityValues = new Dictionary<long, Dictionary<byte, EntityValue>>();
		protected static FastResourceLock lock_entityValues = new FastResourceLock();
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
			lock_entityValues = null;
			logger = null;
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

	}

	public class EntityValue<T> : EntityValue
	{

		protected readonly long m_entityId;
		protected readonly byte m_valueId;

		protected T m_value;
		private bool m_synced;

		public Action AfterValueChanged;

		public T Value
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
				m_value = value;
				AfterValueChanged.InvokeIfExists();
				SendValue();
			}
		}

		public EntityValue(long entityId, byte valueId, T initialValue = default(T))
		{
			this.m_entityId = entityId;
			this.m_valueId = valueId;
			this.m_value = initialValue;
			this.m_synced = MyAPIGateway.Multiplayer.IsServer;

			using (lock_entityValues.AcquireExclusiveUsing())
			{
				Dictionary<byte, EntityValue> entityValues;
				if (!allEntityValues.TryGetValue(entityId, out entityValues))
				{
					entityValues = new Dictionary<byte, EntityValue>();
					allEntityValues.Add(entityId, entityValues);
				}

				entityValues.Add(valueId, this);
			}
		}

		protected override void SendValue(ulong? clientId = null)
		{
			List<byte> bytes = ResourcePool<List<byte>>.Get();
			try
			{
				ByteConverter.AppendBytes(bytes, (byte)MessageHandler.SubMod.SyncEntityValue);
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
			AfterValueChanged.InvokeIfExists();
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

	}

}
