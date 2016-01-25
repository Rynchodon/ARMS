using System;
using System.Collections.Generic;
using System.IO;
using Rynchodon.Utility;
using Sandbox.ModAPI;

namespace Rynchodon.GUI
{
	public class GuiStateSaver
	{

		private static GuiStateSaver Static;

		public static void NeedToSave()
		{
			if (Static.m_needSave)
				return;
			Static.m_needSave = true;
			Static.m_nextSave = DateTime.UtcNow + Static.m_interval;
		}

		private readonly Logger m_logger;
		private readonly TimeSpan m_interval = new TimeSpan(0, 5, 0);
		private readonly FileMaster m_master;
		private DateTime m_nextSave;
		private bool m_needSave;

		private readonly List<byte> data = new List<byte>();

		public GuiStateSaver()
		{
			m_logger = new Logger(GetType().Name);
			m_nextSave = DateTime.UtcNow + m_interval;
			m_master = new FileMaster("GuiState-Master", "GuiState-");
			Static = this;
			MyAPIGateway.Entities.OnCloseAll += Save;
		}

		public void CheckTime()
		{
			if (m_needSave && DateTime.UtcNow >= m_nextSave)
				Save();
		}

		public void Load()
		{
			BinaryReader reader = m_master.GetBinaryReader(MyAPIGateway.Session.Name);
			if (reader == null)
			{
				m_logger.alwaysLog("GUI state could not be loaded", "Load()", Logger.severity.INFO);
				return;
			}
			byte[] storedData = reader.ReadBytes((int)reader.BaseStream.Length);
			reader.Close();

			int pos = 0;
			while (pos < storedData.Length)
			{
				long entityId = ByteConverter.GetLong(storedData, ref pos);
				TerminalBlockSync block;
				if (Registrar_GUI.TryGetValue(entityId, out block))
				{
					m_logger.debugLog("deserializing data for " + entityId, "Load()", Logger.severity.DEBUG);
					block.DeserializeData(storedData, ref pos);
				}
				else
				{
					m_logger.alwaysLog("entity is missing from world: " + entityId, "Load()", Logger.severity.WARNING);
					SkipGo(storedData, ref pos);
				}
			}
		}

		private void Save()
		{
			Logger.debugNotify("Saving GUI state");
			m_logger.debugLog("Saving GUI state", "Save()");

			data.Clear();
			m_needSave = false;
			m_nextSave = DateTime.UtcNow + m_interval;
			Registrar_GUI.ForEach<TerminalBlockSync>(block => { block.SerializeData(data); });
			BinaryWriter writer = m_master.GetBinaryWriter(MyAPIGateway.Session.Name);
			MyAPIGateway.Parallel.StartBackground(() => {
				writer.Write(data.ToArray());
				writer.Close();
			});
		}

		private void SkipGo(byte[] serial, ref int pos)
		{
			byte groups = ByteConverter.GetByte(serial, ref pos);
			for (int gi = 0; gi < groups; gi++)
			{
				TypeCode code = (TypeCode)ByteConverter.GetByte(serial, ref pos);
				int items = ByteConverter.GetInt(serial, ref pos);
				for (int ii = 0; ii < items; ii++)
				{
					ByteConverter.GetByte(serial, ref pos);
					ByteConverter.GetOfType(serial, code, ref pos);
				}
			}
		}

	}
}
