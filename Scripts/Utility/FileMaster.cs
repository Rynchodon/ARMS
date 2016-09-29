using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sandbox.ModAPI;

namespace Rynchodon.Utility
{
	public class FileMaster
	{

		private readonly SortedList<DateTime, string> m_fileAgeName = new SortedList<DateTime, string>();
		private readonly string[] m_separator = { " - " };

		private readonly Logger m_logger;
		private readonly string m_masterName;
		private readonly string m_slaveName;
		private readonly int m_limit;

		public FileMaster(string masterName, string slaveName, int limit = 100)
		{
			this.m_logger = new Logger(() => m_masterName);
			this.m_masterName = masterName;
			this.m_slaveName = slaveName;
			this.m_limit = limit;

			ReadMaster();
		}

		public BinaryWriter GetBinaryWriter(string identifier)
		{
			string filename = m_slaveName + identifier;
			GetWriter(filename);
			return MyAPIGateway.Utilities.WriteBinaryFileInLocalStorage(filename, GetType());
		}

		public TextWriter GetTextWriter(string identifier)
		{
			string filename = m_slaveName + identifier;
			GetWriter(filename);
			return MyAPIGateway.Utilities.WriteFileInLocalStorage(filename, GetType());
		}

		public BinaryReader GetBinaryReader(string identifier)
		{
			string filename = m_slaveName + identifier;
			if (MyAPIGateway.Utilities.FileExistsInLocalStorage(filename, GetType()))
				return MyAPIGateway.Utilities.ReadBinaryFileInLocalStorage(m_slaveName + identifier, GetType());
			else
				return null;
		}

		public TextReader GetTextReader(string identifier)
		{
			string filename = m_slaveName + identifier;
			if (MyAPIGateway.Utilities.FileExistsInLocalStorage(filename, GetType()))
				return MyAPIGateway.Utilities.ReadFileInLocalStorage(m_slaveName + identifier, GetType());
			else
				return null;
		}

		public bool Delete(string identifier)
		{
			string fileName = m_slaveName + identifier;
			if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(fileName, GetType()))
				return false;

			try { MyAPIGateway.Utilities.DeleteFileInLocalStorage(fileName, GetType()); }
			catch (Exception) { m_logger.alwaysLog("failed to delete file:" + fileName, Logger.severity.INFO); }
			if (MyAPIGateway.Utilities.FileExistsInLocalStorage(fileName, GetType()))
				return false;

			m_fileAgeName.RemoveAt(m_fileAgeName.IndexOfValue(fileName));
			WriteMaster();
			return true;
		}

		public bool FileExists(string identifier)
		{
			string fileName = m_slaveName + identifier;
			return MyAPIGateway.Utilities.FileExistsInLocalStorage(fileName, GetType());
		}

		private void GetWriter(string filename)
		{
			int index = m_fileAgeName.IndexOfValue(filename);
			if (index >= 0)
				m_fileAgeName.RemoveAt(index);
			m_fileAgeName.Add(DateTime.UtcNow, filename);

			while (m_fileAgeName.Count >= m_limit)
			{
				string delete = m_fileAgeName.ElementAt(0).Value;
				m_logger.alwaysLog("At limit, deleting: " + delete, Logger.severity.INFO);
				try { MyAPIGateway.Utilities.DeleteFileInLocalStorage(delete, GetType()); }
				catch (Exception) { m_logger.alwaysLog("failed to delete file:" + delete, Logger.severity.INFO); }
				if (MyAPIGateway.Utilities.FileExistsInLocalStorage(delete, GetType()))
				{
					m_logger.alwaysLog("Failed to delete: " + delete, Logger.severity.INFO);
					break;
				}
				m_fileAgeName.RemoveAt(0);
			}

			WriteMaster();
		}

		private void WriteMaster()
		{
			TextWriter writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(m_masterName, GetType());
			foreach (var pair in m_fileAgeName)
			{
				writer.Write(pair.Key);
				writer.Write(m_separator[0]);
				writer.WriteLine(pair.Value);
			}
			writer.Close();
		}

		private void ReadMaster()
		{
			if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(m_masterName, GetType()))
			{
				m_logger.debugLog("No master file", Logger.severity.INFO);
				return;
			}

			TextReader reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(m_masterName, GetType());
			while (true)
			{
				string line = reader.ReadLine();
				if (line == null)
					break;
				string[] split = line.Split(m_separator, 2, StringSplitOptions.None);
				DateTime modified;
				if (!DateTime.TryParse(split[0], out modified))
				{
					m_logger.debugLog("Failed to parse: " + split[0] + " to DateTime");
					continue;
				}
				m_fileAgeName.Add(modified, split[1]);
			}
			reader.Close();
		}

	}
}
