using System;
using System.Collections;
using System.Collections.Generic;
using Sandbox.ModAPI;

namespace Rynchodon.Utility.Network
{
	public abstract class SyncBlock
	{

		private const byte ID_Array = 100; // + TypeCode
		private const byte ID_RequestAll = 200;

		private static readonly Logger s_logger = new Logger("SyncBlock");
		private static readonly List<byte> byteList = new List<byte>();

		static SyncBlock()
		{
			NetworkClient.Handlers[NetworkClient.SubModule.BlockSync] = Receive;
		}

		private static void Receive(byte[] bytes)
		{
			int pos = 0;
			byte subMod = ByteConverter.GetByte(bytes, ref pos);
			s_logger.debugLog(subMod != (byte)NetworkClient.SubModule.BlockSync, "Wrong SubModule: " + subMod, "Parse()", Logger.severity.FATAL);
			long entityId = ByteConverter.GetLong(bytes, ref pos);
			SyncBlock sb;
			if (!Registrar.TryGetValue(entityId, out sb))
			{
				s_logger.alwaysLog("Failed to get SyncBlock from Registrar: " + entityId, "Parse()", Logger.severity.ERROR);
				return;
			}

			byte typeCode = ByteConverter.GetByte(bytes, ref pos);
			if (typeCode == ID_RequestAll)
			{
				ulong client = ByteConverter.GetUlong(bytes, ref pos);
				foreach (IList list in sb.SyncData.Values)
					sb.SendToClient(client, list);
			}
			else if (typeCode >= ID_Array) // is an array
			{
				TypeCode code = (TypeCode)(typeCode - ID_Array);
				IList list = sb.GetList(code);
				for (int index = 0; index < list.Count; index++)
				{
					list[index] = ByteConverter.GetOfType(bytes, code, ref pos);
					sb.m_logger.debugLog("Set value, type: " + code + ", index: " + index + ", value: " + list[index], "Receive()");
				}
			}
			else // not an array
			{
				TypeCode code = (TypeCode)typeCode;
				IList list = sb.GetList(code);
				byte index = ByteConverter.GetByte(bytes, ref pos);
				list[index] = ByteConverter.GetOfType(bytes, code, ref pos);
				sb.m_logger.debugLog("Set value, type: " + code + ", index: " + index + ", value: " + list[index], "Receive()");
			}
		}

		private readonly long entityId;
		private readonly Logger m_logger;

		private Dictionary<Type, IList> SyncData = new Dictionary<Type, IList>();

		protected SyncBlock(IMyCubeBlock block)
		{
			m_logger = new Logger(GetType().Name, block);
			entityId = block.EntityId;
			Registrar.Add(block, (SyncBlock)this);

			m_logger.debugLog("Initialized", "SyncBlock()");
		}

		protected List<T> GetList<T>()
		{
			IList gen;
			List<T> list;
			if (SyncData.TryGetValue(typeof(T), out gen))
				list = gen as List<T>;
			else
			{
				list = new List<T>();
				SyncData.Add(typeof(T), list);
			}

			return list;
		}

		protected IList GetList(TypeCode code)
		{
			switch (code)
			{
				case TypeCode.Boolean:
					return GetList<bool>();
				case TypeCode.Byte:
					return GetList<byte>();
				case TypeCode.Int16:
					return GetList<short>();
				case TypeCode.UInt16:
					return GetList<ushort>();
				case TypeCode.Int32:
					return GetList<int>();
				case TypeCode.UInt32:
					return GetList<uint>();
				case TypeCode.Int64:
					return GetList<long>();
				case TypeCode.UInt64:
					return GetList<ulong>();
				case TypeCode.Single:
					return GetList<float>();
				case TypeCode.Double:
					return GetList<double>();
				case TypeCode.String:
					return GetList<string>();
			}

			throw new ArgumentException("Invalid TypeCode: " + code);
		}

		protected void RequestAllFromServer()
		{
			m_logger.debugLog(byteList.Count != 0, "byteList was not cleared", "RequestAllFromServer()", Logger.severity.FATAL);

			ByteConverter.AppendBytes(byteList, (byte)NetworkClient.SubModule.BlockSync);
			ByteConverter.AppendBytes(byteList, entityId);
			ByteConverter.AppendBytes(byteList, ID_RequestAll);
			ByteConverter.AppendBytes(byteList, MyAPIGateway.Multiplayer.MyId);

			m_logger.debugLog("Message: " + NetworkClient.SubModule.BlockSync + ", " + entityId + ", " + ID_RequestAll + ", " + MyAPIGateway.Multiplayer.MyId, "RequestAllFromServer()");

			byte[] message = byteList.ToArray();
			byteList.Clear();

			if (MyAPIGateway.Multiplayer.SendMessageToServer(NetworkClient.ModId, message))
				m_logger.debugLog("Message sent", "RequestAllFromServer()");
			else
				m_logger.alwaysLog("Failed to send message", "RequestAllFromServer()", Logger.severity.ERROR);
		}

		protected void SendToServer<T>(byte index)
		{
			m_logger.debugLog(byteList.Count != 0, "byteList was not cleared", "SendToServer()", Logger.severity.FATAL);

			object data = GetList<T>()[index];

			ByteConverter.AppendBytes(byteList, (byte)NetworkClient.SubModule.BlockSync);
			ByteConverter.AppendBytes(byteList, entityId);

			ByteConverter.AppendBytes(byteList, Convert.GetTypeCode(data));
			ByteConverter.AppendBytes(byteList, index);
			ByteConverter.AppendBytes(byteList, data);

			m_logger.debugLog("Message: " + NetworkClient.SubModule.BlockSync + ", " + entityId + ", " + Convert.GetTypeCode(data) + ", " + index + ", " + data, "SendToServer()");

			byte[] message = byteList.ToArray();
			byteList.Clear();

			if (MyAPIGateway.Multiplayer.SendMessageToServer(NetworkClient.ModId, message))
				m_logger.debugLog("Message sent", "SendToServer()");
			else
				m_logger.alwaysLog("Failed to send message", "SendToServer()", Logger.severity.ERROR);
		}

		private void SendToClient(ulong client, IList data)
		{
			m_logger.debugLog(byteList.Count != 0, "byteList was not cleared", "SendToClient()", Logger.severity.FATAL);

			ByteConverter.AppendBytes(byteList, (byte)NetworkClient.SubModule.BlockSync);
			ByteConverter.AppendBytes(byteList, entityId);

			ByteConverter.AppendBytes(byteList, (byte)(ID_Array + Convert.GetTypeCode(data[0])));
			foreach (object obj in data)
				ByteConverter.AppendBytes(byteList, obj);

			byte[] message = byteList.ToArray();
			byteList.Clear();

			if (MyAPIGateway.Multiplayer.SendMessageTo(NetworkClient.ModId, message, client))
				m_logger.debugLog("Message sent", "SendToClient()");
			else
				m_logger.alwaysLog("Failed to send message", "SendToClient()", Logger.severity.ERROR);
		}

	}
}
