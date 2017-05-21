using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using VRage.Utils;

namespace Rynchodon.Utility
{
	/// <summary>
	/// A file stored in SE Mod Storage.
	/// Affixes a timestamp to the filename if maxVersions > 1.
	/// Deletes all but the maxVersions most recent files with the same baseName and extension.
	/// </summary>
	/// <remarks>
	/// Used by Logger. Don't use logging.
	/// </remarks>
	class ModFile
	{
		private static readonly string SEStoragePath = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpaceEngineers", "Storage"
		);

		private readonly string m_assemblyName, m_baseName, m_dirPath, m_extension, m_fileName, m_filePath;
		private readonly uint m_maxVersions;

		public ModFile(string baseName, string extension, Assembly modAssembly, uint maxVersions = 10)
		{
			if (String.IsNullOrEmpty(baseName))
				throw new ArgumentNullException("baseName");
			if (String.IsNullOrEmpty(extension))
				throw new ArgumentNullException("extension");

			m_assemblyName = modAssembly.GetName().Name;
			m_baseName = baseName;
			m_dirPath = Path.Combine(SEStoragePath, m_assemblyName);
			m_extension = extension;
			m_fileName = $"{baseName}{(maxVersions > 1 ? "_" + DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss") : "")}.{extension}";
			m_filePath = Path.Combine(m_dirPath, m_fileName);
			m_maxVersions = maxVersions;

			Rotate();
		}

		public void Delete()
		{
			TryDeleteAtPath(m_filePath);
		}

		public bool Exists()
		{
			return File.Exists(m_filePath);
		}

		public BinaryReader GetBinaryReader()
		{
			return File.Exists(m_filePath) ? new BinaryReader(File.Open(m_filePath, FileMode.Open)) : null;
		}

		public TextReader GetTextReader()
		{
			return File.Exists(m_filePath) ? File.OpenText(m_filePath) : null;
		}

		public TextWriter GetTextWriter()
		{
			return File.CreateText(m_filePath);
		}

		public BinaryWriter GetBinaryWriter()
		{
			return new BinaryWriter(File.Create(m_filePath));
		}

		private void Rotate()
		{
			if (!Directory.Exists(m_dirPath))
				return;

			try
			{
				var paths = Directory.GetFiles(m_dirPath, "*." + m_extension)
					.Where(path => Path.GetFileName(path).StartsWith(m_baseName))
					.OrderByDescending(path => File.GetLastWriteTime(path));

				uint i = 1; // includes current version
				foreach (var path in paths)
				{
					if (++i > m_maxVersions)
						TryDeleteAtPath(path);
				}
			}
			catch (Exception e)
			{
				MyLog.Default.WriteLine("ARMS ModFile ERROR while rotating files: " + e);
			}

		}

		private void TryDeleteAtPath(string path)
		{
			if (!File.Exists(path))
				return;
			try
			{
				File.Delete(path);
			}
			catch (Exception e)
			{
				MyLog.Default.WriteLine($"ARMS Logger ERROR deleting file at {path}: " + e);
			}
		}
	}
}
