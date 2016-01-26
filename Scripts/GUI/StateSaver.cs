using System;
using System.Collections.Generic;
using System.IO;
using Rynchodon.Utility;
using Sandbox.ModAPI;

namespace Rynchodon.GUI
{
	/// <summary>
	/// Saves and loads the state of all GUI settings to/from disk.
	/// </summary>
	public class StateSaver
	{

		private static StateSaver Static;

		/// <summary>
		/// Flags the state as in need of saving, should be invoked every time state changes.
		/// </summary>
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

		public StateSaver()
		{
			m_logger = new Logger(GetType().Namespace + '.' + GetType().Name);
			m_nextSave = DateTime.UtcNow + m_interval;
			m_master = new FileMaster("GuiState-Master", "GuiState-");
			Static = this;
			MyAPIGateway.Entities.OnCloseAll += Save;
		}

		/// <summary>
		/// Saves the GUI state if it has changed and enough time has passed since the last save.
		/// </summary>
		public void CheckTime()
		{
			if (m_needSave && DateTime.UtcNow >= m_nextSave)
				Save();
		}

		/// <summary>
		/// Loads the GUI data from the disk. The data will be sent to DeserializeData method of each block.
		/// </summary>
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
				if (Registrar.TryGetValue(entityId, out block))
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

		/// <summary>
		/// Saves the GUI state to the disk, if m_needSave.
		/// </summary>
		private void Save()
		{
			if (!m_needSave)
				return;

			Logger.debugNotify("Saving GUI state");
			m_logger.debugLog("Saving GUI state", "Save()");

			data.Clear();
			m_needSave = false;
			m_nextSave = DateTime.UtcNow + m_interval;
			Registrar.ForEach<TerminalBlockSync>(block => { block.SerializeData(data); });
			BinaryWriter writer = m_master.GetBinaryWriter(MyAPIGateway.Session.Name);
			MyAPIGateway.Parallel.StartBackground(() => {
				writer.Write(data.ToArray());
				writer.Close();
			});
		}

		/// <summary>
		/// Skips an entity in the loading process, used when the entity does not exist.
		/// </summary>
		/// <remarks>
		/// Must parallel DeserializeData method of TerminalBlockSync.
		/// </remarks>
		/// <param name="serial">Bytes that are being loaded.</param>
		/// <param name="pos">Current position in the byte array.</param>
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
