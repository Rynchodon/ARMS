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

		private static readonly Logger s_logger = new Logger("TerminalBlockSync");
		private static readonly List<byte> byteList = new List<byte>();

		static TerminalBlockSync()
		{
			// don't register through NetworkClient because we do not unregister on unload
			MyAPIGateway.Multiplayer.RegisterMessageHandler(NetworkClient.ModId, Receive);
		}

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
			if (!Registrar_GUI.TryGetValue(entityId, out sb))
			{
				s_logger.alwaysLog("Failed to get SyncBlock from Registrar: " + entityId, "Parse()", Logger.severity.ERROR);
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
				for (int index = 0; index < dataGroup.Count; index++)
				{
					dataGroup[index] = ByteConverter.GetOfType(bytes, code, ref pos);
					sb.m_logger.debugLog("Set value, type: " + code + ", index: " + index + ", value: " + dataGroup[index], "Receive()");
				}
			}
			else // not an array
			{
				TypeCode code = (TypeCode)typeCode;
				IDictionary dataGroup = sb.GetDataGroup(code);
				byte index = ByteConverter.GetByte(bytes, ref pos);
				dataGroup[index] = ByteConverter.GetOfType(bytes, code, ref pos);
				sb.m_logger.debugLog("Set value, type: " + code + ", index: " + index + ", value: " + dataGroup[index], "Receive()");
			}
		}

		private readonly long entityId;
		private readonly Logger m_logger;

		private Dictionary<Type, IDictionary> SyncData = new Dictionary<Type, IDictionary>();

		public TerminalBlockSync(IMyCubeBlock block)
		{
			m_logger = new Logger(GetType().Name, block);
			entityId = block.EntityId;
			Registrar_GUI.Add(block, (TerminalBlockSync)this);

			if (!MyAPIGateway.Multiplayer.IsServer)
				RequestAllFromServer();
			m_logger.debugLog("Initialized", "SyncBlock()");
		}

		public T GetValue<T>(byte index)
		{
			Dictionary<byte, T> dataGroup = GetDataGroup<T>();
			T value;
			if (!dataGroup.TryGetValue(index, out value))
			{
				value = default(T);
				dataGroup.Add(index, value);
			}
			return value;
		}

		public void SetValue<T>(byte index, T value)
		{
			GetDataGroup<T>()[index] = value;
			SendToAll<T>(index);
		}

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

			throw new ArgumentException("Invalid TypeCode: " + code);
		}

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

		private void SendToClient(ulong client, IDictionary data)
		{
			m_logger.debugLog(byteList.Count != 0, "byteList was not cleared", "SendToClient()", Logger.severity.FATAL);

			ByteConverter.AppendBytes(byteList, (byte)NetworkClient.SubModule.TerminalBlockSync);
			ByteConverter.AppendBytes(byteList, entityId);

			ByteConverter.AppendBytes(byteList, (byte)(ID_Array + Convert.GetTypeCode(data[0])));
			foreach (object obj in data.Values)
				ByteConverter.AppendBytes(byteList, obj);

			m_logger.debugLog("Message: " + NetworkClient.SubModule.TerminalBlockSync + ", " + entityId + ", " + (ID_Array + Convert.GetTypeCode(data[0])), "RequestAllFromServer()");
			//m_logger.debugLog("Bytes: " + string.Join(", ", byteList), "SendToClient()");

			byte[] message = byteList.ToArray();
			byteList.Clear();

			if (MyAPIGateway.Multiplayer.SendMessageTo(NetworkClient.ModId, message, client))
				m_logger.debugLog("Message sent", "SendToClient()");
			else
				m_logger.alwaysLog("Failed to send message", "SendToClient()", Logger.severity.ERROR);
		}

		private void SendToAll<T>(byte index)
		{
			m_logger.debugLog(byteList.Count != 0, "byteList was not cleared", "SendToAll<T>()", Logger.severity.FATAL);

			object data = GetValue<T>(index);

			ByteConverter.AppendBytes(byteList, (byte)NetworkClient.SubModule.TerminalBlockSync);
			ByteConverter.AppendBytes(byteList, entityId);

			ByteConverter.AppendBytes(byteList, (byte)Convert.GetTypeCode(data));
			ByteConverter.AppendBytes(byteList, index);
			ByteConverter.AppendBytes(byteList, data);

			m_logger.debugLog("Message: " + NetworkClient.SubModule.TerminalBlockSync + ", " + entityId + ", " + Convert.GetTypeCode(data) + ", " + index + ", " + data, "SendToAll<T>()");
			//m_logger.debugLog("Bytes: " + string.Join(", ", byteList), "SendToAll<T>()");

			byte[] message = byteList.ToArray();
			byteList.Clear();

			if (MyAPIGateway.Multiplayer.SendMessageToOthers(NetworkClient.ModId, message))
				m_logger.debugLog("Message sent", "SendToAll<T>()");
			else
				m_logger.alwaysLog("Failed to send message", "SendToAll<T>()", Logger.severity.ERROR);
		}

	}
}
