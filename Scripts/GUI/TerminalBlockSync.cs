using System;
using System.Collections;
using System.Collections.Generic;
using Rynchodon.Utility.Network;
using Sandbox.ModAPI;

namespace Rynchodon.GUI
{
	public class TerminalBlockSync
	{

		private const byte ID_Array = 100; // + TypeCode
		private const byte ID_RequestAll = 200;

		private static readonly Logger s_logger = new Logger("Rynchodon.GUI.TerminalBlockSync");
		private static readonly List<byte> byteList = new List<byte>();

		static TerminalBlockSync()
		{
			// don't register through NetworkClient because we do not unregister on unload
			MyAPIGateway.Multiplayer.RegisterMessageHandler(NetworkClient.ModId, Receive);
		}

		/// <summary>
		/// Receives network data for a TerminalBlockSync.
		/// </summary>
		/// <param name="bytes">Network data being transmitted.</param>
		private static void Receive(byte[] bytes)
		{
			//s_logger.debugLog("Received: " + string.Join(", ", bytes), "Receive()");

			int pos = 0;
			byte subMod = ByteConverter.GetByte(bytes, ref pos);
			if (subMod != (byte)NetworkClient.SubModule.TerminalBlockSync)
			{
				s_logger.debugLog("Wrong submodule: " + subMod, "Receive()");
				return;
			}
			long entityId = ByteConverter.GetLong(bytes, ref pos);
			TerminalBlockSync sb;
			if (!Registrar.TryGetValue(entityId, out sb))
			{
				s_logger.alwaysLog("Failed to get SyncBlock from Registrar: " + entityId, "Receive()", Logger.severity.ERROR);
				return;
			}

			byte typeCode = ByteConverter.GetByte(bytes, ref pos);
			if (typeCode == ID_RequestAll)
			{
				ulong client = ByteConverter.GetUlong(bytes, ref pos);
				foreach (IDictionary dataGroup in sb.SyncData.Values)
					sb.SendToClient(client, dataGroup);
			}
			else if (typeCode >= ID_Array) // is an array
			{
				TypeCode code = (TypeCode)(typeCode - ID_Array);
				IDictionary dataGroup = sb.GetDataGroup(code);
				if (dataGroup == null)
				{
					s_logger.alwaysLog("failed to get type for array, message: " + string.Join(":", bytes), "Receive()", Logger.severity.FATAL);
					return;
				}
				while (pos < bytes.Length)
				{
					byte key = ByteConverter.GetByte(bytes, ref pos);
					object value = ByteConverter.GetOfType(bytes, code, ref pos);
					dataGroup[key] = value;
					sb.m_logger.debugLog("Set value, type: " + code + ", key: " + key + ", value: " + value, "Receive()");
				}
				StateSaver.NeedToSave();
			}
			else // not an array
			{
				TypeCode code = (TypeCode)typeCode;
				IDictionary dataGroup = sb.GetDataGroup(code);
				if (dataGroup == null)
				{
					s_logger.alwaysLog("failed to get type for array, message: " + string.Join(":", bytes), "Receive()", Logger.severity.FATAL);
					return;
				}
				byte index = ByteConverter.GetByte(bytes, ref pos);
				dataGroup[index] = ByteConverter.GetOfType(bytes, code, ref pos);
				sb.m_logger.debugLog("Set value, type: " + code + ", index: " + index + ", value: " + dataGroup[index], "Receive()");
				StateSaver.NeedToSave();
			}
		}

		private readonly long entityId;
		private readonly Logger m_logger;

		private Dictionary<Type, IDictionary> SyncData = new Dictionary<Type, IDictionary>();

		public TerminalBlockSync(IMyCubeBlock block)
		{
			m_logger = new Logger(GetType().Namespace + '.' + GetType().Name, block);

			m_logger.debugLog(block == null, "block == null", "TerminalBlockSync()", Logger.severity.FATAL);
			m_logger.debugLog(MyAPIGateway.Multiplayer == null, "MyAPIGateway.Multiplayer == null", "TerminalBlockSync()", Logger.severity.FATAL);

			entityId = block.EntityId;
			Registrar.Add(block, (TerminalBlockSync)this);

			if (!MyAPIGateway.Multiplayer.IsServer)
				RequestAllFromServer();
			m_logger.debugLog("Initialized", "SyncBlock()");
		}

		/// <summary>
		/// Gets a value from the syncronized data.
		/// </summary>
		/// <typeparam name="T">The type of the value.</typeparam>
		/// <param name="index">The index of the value.</param>
		/// <returns>The value from the syncronized data.</returns>
		public T GetValue<T>(byte index)
		{
			Dictionary<byte, T> dataGroup = GetDataGroup<T>();
			T value;
			if (!dataGroup.TryGetValue(index, out value))
				value = default(T);
			else if (value.Equals(default(T)))
				dataGroup.Remove(index);
			return value;
		}

		/// <summary>
		/// Sets a value for the syncronized data. Generates network traffic.
		/// </summary>
		/// <typeparam name="T">The type of the value.</typeparam>
		/// <param name="index">The index of the value.</param>
		/// <param name="value">The value to be set.</param>
		public void SetValue<T>(byte index, T value)
		{
			if (value.Equals(default(T)))
				GetDataGroup<T>().Remove(index);
			else
				GetDataGroup<T>()[index] = value;
			SendToAll<T>(index, value);
			StateSaver.NeedToSave();
		}

		/// <summary>
		/// Gets the storage for all the data of a specific type.
		/// </summary>
		/// <typeparam name="T">The type of the data.</typeparam>
		/// <returns>The storage for all the data of a specific type.</returns>
		private Dictionary<byte, T> GetDataGroup<T>()
		{
			IDictionary gen;
			Dictionary<byte, T> dataGroup;
			if (SyncData.TryGetValue(typeof(T), out gen))
				dataGroup = gen as Dictionary<byte, T>;
			else
			{
				dataGroup = new Dictionary<byte, T>();
				SyncData.Add(typeof(T), dataGroup);
			}

			return dataGroup;
		}

		/// <summary>
		/// Gets the storage for all the data of a specific type.
		/// </summary>
		/// <param name="code">The TypeCode of the data, not all TypeCodes are valid.</param>
		/// <returns>The storage for all the data of a specific type.</returns>
		/// <exception cref="ArgumentException">Iff TypeCode is of an unacceptable value type.</exception>
		private IDictionary GetDataGroup(TypeCode code)
		{
			switch (code)
			{
				case TypeCode.Boolean:
					return GetDataGroup<bool>();
				case TypeCode.Byte:
					return GetDataGroup<byte>();
				case TypeCode.Int16:
					return GetDataGroup<short>();
				case TypeCode.UInt16:
					return GetDataGroup<ushort>();
				case TypeCode.Int32:
					return GetDataGroup<int>();
				case TypeCode.UInt32:
					return GetDataGroup<uint>();
				case TypeCode.Int64:
					return GetDataGroup<long>();
				case TypeCode.UInt64:
					return GetDataGroup<ulong>();
				case TypeCode.Single:
					return GetDataGroup<float>();
				case TypeCode.Double:
					return GetDataGroup<double>();
				case TypeCode.String:
					return GetDataGroup<string>();
			}

			m_logger.alwaysLog("Invalid TypeCode: " + code, "GetDataGroup()", Logger.severity.FATAL);
			return null;
		}

		/// <summary>
		/// Sends a message to the server requesting all the data (for this block).
		/// </summary>
		private void RequestAllFromServer()
		{
			m_logger.debugLog(byteList.Count != 0, "byteList was not cleared", "RequestAllFromServer()", Logger.severity.FATAL);

			ByteConverter.AppendBytes(byteList, (byte)NetworkClient.SubModule.TerminalBlockSync);
			ByteConverter.AppendBytes(byteList, entityId);
			ByteConverter.AppendBytes(byteList, ID_RequestAll);
			ByteConverter.AppendBytes(byteList, MyAPIGateway.Multiplayer.MyId);

			m_logger.debugLog("Message: " + NetworkClient.SubModule.TerminalBlockSync + ", " + entityId + ", " + ID_RequestAll + ", " + MyAPIGateway.Multiplayer.MyId, "RequestAllFromServer()");
			//m_logger.debugLog("Bytes: " + string.Join(", ", byteList), "RequestAllFromServer()");

			byte[] message = byteList.ToArray();
			byteList.Clear();

			if (MyAPIGateway.Multiplayer.SendMessageToServer(NetworkClient.ModId, message))
				m_logger.debugLog("Message sent", "RequestAllFromServer()");
			else
				m_logger.alwaysLog("Failed to send message", "RequestAllFromServer()", Logger.severity.ERROR);
		}

		/// <summary>
		/// Sends a message to a client for all the data of a type.
		/// </summary>
		/// <param name="client">The client to send the data to.</param>
		/// <param name="data">The data to send.</param>
		private void SendToClient(ulong client, IDictionary data)
		{
			m_logger.debugLog(byteList.Count != 0, "byteList was not cleared", "SendToClient()", Logger.severity.FATAL);

			ByteConverter.AppendBytes(byteList, (byte)NetworkClient.SubModule.TerminalBlockSync);
			ByteConverter.AppendBytes(byteList, entityId);

			ByteConverter.AppendBytes(byteList, (byte)(ID_Array + Convert.GetTypeCode(data[0])));
			foreach (byte key in data.Keys)
			{
				ByteConverter.AppendBytes(byteList, key);
				ByteConverter.AppendBytes(byteList, data[key]);
			}

			m_logger.debugLog("Message: " + NetworkClient.SubModule.TerminalBlockSync + ", " + entityId + ", " + (ID_Array + Convert.GetTypeCode(data[0])), "RequestAllFromServer()");
			//m_logger.debugLog("Bytes: " + string.Join(", ", byteList), "SendToClient()");

			byte[] message = byteList.ToArray();
			byteList.Clear();

			if (MyAPIGateway.Multiplayer.SendMessageTo(NetworkClient.ModId, message, client))
				m_logger.debugLog("Message sent", "SendToClient()");
			else
				m_logger.alwaysLog("Failed to send message", "SendToClient()", Logger.severity.ERROR);
		}

		/// <summary>
		/// Send a value to server and clients.
		/// </summary>
		/// <typeparam name="T">The type of the value to send.</typeparam>
		/// <param name="index">The index of the value to send.</param>
		/// <param name="value">The value to send.</param>
		private void SendToAll<T>(byte index, T value)
		{
			m_logger.debugLog(byteList.Count != 0, "byteList was not cleared", "SendToAll<T>()", Logger.severity.FATAL);

			ByteConverter.AppendBytes(byteList, (byte)NetworkClient.SubModule.TerminalBlockSync);
			ByteConverter.AppendBytes(byteList, entityId);

			ByteConverter.AppendBytes(byteList, (byte)Convert.GetTypeCode(value));
			ByteConverter.AppendBytes(byteList, index);
			ByteConverter.AppendBytes(byteList, value);

			m_logger.debugLog("Message: " + NetworkClient.SubModule.TerminalBlockSync + ", " + entityId + ", " + Convert.GetTypeCode(value) + ", " + index + ", " + value, "SendToAll<T>()");
			//m_logger.debugLog("Bytes: " + string.Join(", ", byteList), "SendToAll<T>()");

			byte[] message = byteList.ToArray();
			byteList.Clear();

			if (MyAPIGateway.Multiplayer.SendMessageToOthers(NetworkClient.ModId, message))
				m_logger.debugLog("Message sent", "SendToAll<T>()");
			else
				m_logger.alwaysLog("Failed to send message", "SendToAll<T>()", Logger.severity.ERROR);
		}

		/// <summary>
		/// Appends the entity id and all the data to the byte list.
		/// </summary>
		/// <param name="serial">Byte list to append data to.</param>
		public void SerializeData(List<byte> serial)
		{
			ByteConverter.AppendBytes(serial, entityId);
			ByteConverter.AppendBytes(serial, (byte)SyncData.Count);
			foreach (var pair in SyncData)
			{
				IDictionary dataGroup = pair.Value;
				ByteConverter.AppendBytes(serial, (byte)Type.GetTypeCode(pair.Key));
				ByteConverter.AppendBytes(serial, dataGroup.Count);
				foreach (byte key in dataGroup.Keys)
				{
					ByteConverter.AppendBytes(serial, key);
					ByteConverter.AppendBytes(serial, dataGroup[key]);
					m_logger.debugLog("key: " + key + ", value: " + dataGroup[key], "SerializeData()");
				}
			}
		}

		/// <summary>
		/// Retrieves all the data from the byte list.
		/// </summary>
		/// <param name="serial">Byte list to retrieve data from.</param>
		/// <param name="pos">Current position in the byte list.</param>
		public void DeserializeData(byte[] serial, ref int pos)
		{
			byte groups = ByteConverter.GetByte(serial, ref pos);
			for (int gi = 0; gi < groups; gi++)
			{
				TypeCode code = (TypeCode)ByteConverter.GetByte(serial, ref pos);
				IDictionary dataGroup = GetDataGroup(code);
				if (dataGroup == null)
				{
					m_logger.alwaysLog("Failed to get data group, code: " + code, "DeserializeData()", Logger.severity.FATAL);
					continue;
				}
				int items = ByteConverter.GetInt(serial, ref pos);
				for (int ii = 0; ii < items; ii++)
				{
					byte key = ByteConverter.GetByte(serial, ref pos);
					object value = ByteConverter.GetOfType(serial, code, ref pos);
					m_logger.debugLog("key: " + key + ", value: " + value, "DeserializeData()");
					dataGroup[key] = value;
				}
			}
		}

	}
}
