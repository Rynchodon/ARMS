using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using Rynchodon.Utility;
using Rynchodon.Utility.Network;
using Sandbox.ModAPI;

namespace Rynchodon.Settings
{
	/// <summary>
	/// Some of the settings will be made available to clients.
	/// </summary>
	public class ServerSettings
	{
		public enum SettingName : byte
		{
			bAirResistanceBeta, bAllowAutopilot, bAllowGuidedMissile, bAllowHacker, bAllowRadar, bAllowWeaponControl, bImmortalMiner, bUseRemoteControl, 
			fDefaultSpeed, fMaxSpeed, fMaxWeaponRange
		}

		private static ushort ModID { get { return MessageHandler.ModId; } }
		private static ServerSettings value_Instance;

		private static ServerSettings Instance
		{
			get
			{
				if (Globals.WorldClosed)
					throw new Exception("World closed");
				if (value_Instance == null)
					value_Instance = new ServerSettings();
				return value_Instance;
			}
			set { value_Instance = value; }
		}

		public static Version CurrentVersion { get { return Instance.m_currentVersion; } }
		public static bool ServerSettingsLoaded { get { return Instance.m_settingsLoaded; } }

		/// <exception cref="NullReferenceException">if setting does not exist or is of a different type</exception>
		public static T GetSetting<T>(SettingName name) where T : struct
		{
			ServerSettings instance = Instance;
			SettingSimple<T> set = instance.AllSettings[name] as SettingSimple<T>;
			return set.Value;
		}

		private static void SetSetting<T>(SettingName name, T value) where T : struct
		{
			ServerSettings instance = Instance;
			SettingSimple<T> set = instance.AllSettings[name] as SettingSimple<T>;
			set.Value = value;
			Logger.AlwaysLog("Setting " + name + " = " + value, Rynchodon.Logger.severity.INFO);
		}

		/// <exception cref="NullReferenceException">if setting does not exist or is of a different type</exception>
		public static string GetSettingString(SettingName name)
		{
			ServerSettings instance = Instance;
			SettingString set = instance.AllSettings[name] as SettingString;
			return set.Value;
		}

		private const string modName = "ARMS";
		private const string settings_file_name = "ServerSettings.txt";
		private const string strVersion = "Version";

		private Dictionary<SettingName, Setting> AllSettings = new Dictionary<SettingName, Setting>();
		private System.IO.TextWriter settingsWriter;
		private Version fileVersion;
		private Version m_currentVersion, m_serverVersion;
		private bool m_settingsLoaded;

		[OnWorldClose]
		private static void Unload()
		{
			Instance = null;
		}

		private ServerSettings()
		{
			m_currentVersion = new Version(FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location));
			buildSettings();
			m_settingsLoaded = false;

			if (MyAPIGateway.Multiplayer.IsServer)
			{
				m_serverVersion = m_currentVersion;
				m_settingsLoaded = true;
				MessageHandler.SetHandler(MessageHandler.SubMod.ServerSettings, Server_ReceiveMessage);

				fileVersion = readAll();
				if (fileVersion.CompareTo(m_currentVersion) < 0)
					Rynchodon.Logger.DebugNotify(modName + " has been updated to version " + m_currentVersion, 10000, Rynchodon.Logger.severity.INFO);
				Logger.AlwaysLog("file version: " + fileVersion + ", latest version: " + m_currentVersion, Rynchodon.Logger.severity.INFO);

				writeAll(); // writing immediately decreases user errors & whining
			}
			else
			{
				MessageHandler.SetHandler(MessageHandler.SubMod.ServerSettings, Client_ReceiveMessage);
				RequestSettingsFromServer();
			}
		}

		private void Server_ReceiveMessage(byte[] message, int pos)
		{
			try
			{
				if (message == null)
				{
					Logger.DebugLog("Message is null");
					return;
				}
				if( message.Length < 8)
				{
					Logger.DebugLog("Message is too short: " + message.Length);
					return;
				}

				ulong SteamUserId = ByteConverter.GetUlong(message, ref pos);

				Logger.DebugLog("Received request from: " + SteamUserId);

				List<byte> send = new List<byte>();
				ByteConverter.AppendBytes(send, (byte)MessageHandler.SubMod.ServerSettings);

				ByteConverter.AppendBytes(send, m_serverVersion.Major);
				ByteConverter.AppendBytes(send, m_serverVersion.Minor);
				ByteConverter.AppendBytes(send, m_serverVersion.Build);
				ByteConverter.AppendBytes(send, m_serverVersion.Revision);

				ByteConverter.AppendBytes(send, GetSetting<bool>(SettingName.bAirResistanceBeta));
				ByteConverter.AppendBytes(send, GetSetting<bool>(SettingName.bAllowAutopilot));
				ByteConverter.AppendBytes(send, GetSetting<bool>(SettingName.bAllowGuidedMissile));
				ByteConverter.AppendBytes(send, GetSetting<bool>(SettingName.bAllowHacker));
				ByteConverter.AppendBytes(send, GetSetting<bool>(SettingName.bAllowRadar));
				ByteConverter.AppendBytes(send, GetSetting<bool>(SettingName.bAllowWeaponControl));
				ByteConverter.AppendBytes(send, GetSetting<bool>(SettingName.bImmortalMiner));
				ByteConverter.AppendBytes(send, GetSetting<bool>(SettingName.bUseRemoteControl));
				ByteConverter.AppendBytes(send, GetSetting<float>(SettingName.fDefaultSpeed));
				ByteConverter.AppendBytes(send, GetSetting<float>(SettingName.fMaxSpeed));
				ByteConverter.AppendBytes(send, GetSetting<float>(SettingName.fMaxWeaponRange));

				if (MyAPIGateway.Multiplayer.SendMessageTo(ModID, send.ToArray(), SteamUserId))
					Logger.DebugLog("Sent settings to " + SteamUserId, Rynchodon.Logger.severity.INFO);
				else
					Logger.AlwaysLog("Failed to send settings to " + SteamUserId, Rynchodon.Logger.severity.ERROR);
			}
			catch (Exception ex)
			{ Logger.AlwaysLog("Exception: " + ex, Rynchodon.Logger.severity.ERROR); }
		}

		private void Client_ReceiveMessage(byte[] message, int pos)
		{
			try
			{
				Logger.DebugLog("Received settings from server");

				m_serverVersion.Major = ByteConverter.GetInt(message, ref pos);
				m_serverVersion.Minor = ByteConverter.GetInt(message, ref pos);
				m_serverVersion.Build = ByteConverter.GetInt(message, ref pos);
				m_serverVersion.Revision = ByteConverter.GetInt(message, ref pos);
				
				if (m_currentVersion.Major == m_serverVersion.Major && m_currentVersion.Minor == m_serverVersion.Minor && m_currentVersion.Build == m_serverVersion.Build)
				{
					if (m_currentVersion.Revision != m_serverVersion.Revision)
						Logger.AlwaysLog("Server has different revision of ARMS. Server version: " + m_serverVersion + ", client version: " + m_currentVersion, Rynchodon.Logger.severity.WARNING);
				}
				else
				{
					Logger.AlwaysLog("Server has different version of ARMS. Server version: " + m_serverVersion + ", client version: " + m_currentVersion, Rynchodon.Logger.severity.FATAL);
					return;
				}

				SetSetting<bool>(SettingName.bAirResistanceBeta, ByteConverter.GetBool(message, ref pos));
				SetSetting<bool>(SettingName.bAllowAutopilot, ByteConverter.GetBool(message, ref pos));
				SetSetting<bool>(SettingName.bAllowGuidedMissile, ByteConverter.GetBool(message, ref pos));
				SetSetting<bool>(SettingName.bAllowHacker, ByteConverter.GetBool(message, ref pos));
				SetSetting<bool>(SettingName.bAllowRadar, ByteConverter.GetBool(message, ref pos));
				SetSetting<bool>(SettingName.bAllowWeaponControl, ByteConverter.GetBool(message, ref pos));
				SetSetting<bool>(SettingName.bImmortalMiner, ByteConverter.GetBool(message, ref pos));
				SetSetting<bool>(SettingName.bUseRemoteControl, ByteConverter.GetBool(message, ref pos));
				SetSetting<float>(SettingName.fDefaultSpeed, ByteConverter.GetFloat(message, ref pos));
				SetSetting<float>(SettingName.fMaxSpeed, ByteConverter.GetFloat(message, ref pos));
				SetSetting<float>(SettingName.fMaxWeaponRange, ByteConverter.GetFloat(message, ref pos));

				m_settingsLoaded = true;
			}
			catch (Exception ex)
			{ Logger.AlwaysLog("Exception: " + ex, Rynchodon.Logger.severity.ERROR); }
		}

		private void RequestSettingsFromServer()
		{
			if (MyAPIGateway.Session.Player == null)
			{
				Logger.AlwaysLog("Could not get player, not requesting server settings.", Rynchodon.Logger.severity.WARNING);
				m_settingsLoaded = true;
				return;
			}

			List<byte> bytes = new List<byte>();
			ByteConverter.AppendBytes(bytes, (byte)MessageHandler.SubMod.ServerSettings);
			ByteConverter.AppendBytes(bytes, MyAPIGateway.Session.Player.SteamUserId);

			if (MyAPIGateway.Multiplayer.SendMessageToServer(ModID, bytes.ToArray()))
				Logger.DebugLog("Sent request to server", Rynchodon.Logger.severity.INFO);
			else
				Logger.AlwaysLog("Failed to send request to server", Rynchodon.Logger.severity.ERROR);
		}

		/// <summary>
		/// put each setting into AllSettings with its default value
		/// </summary>
		private void buildSettings()
		{
			AllSettings.Add(SettingName.bAirResistanceBeta, new SettingSimple<bool>(false));
			AllSettings.Add(SettingName.bAllowAutopilot, new SettingSimple<bool>(true));
			AllSettings.Add(SettingName.bAllowGuidedMissile, new SettingSimple<bool>(true));
			AllSettings.Add(SettingName.bAllowHacker, new SettingSimple<bool>(true));
			AllSettings.Add(SettingName.bAllowRadar, new SettingSimple<bool>(true));
			AllSettings.Add(SettingName.bAllowWeaponControl, new SettingSimple<bool>(true));
			AllSettings.Add(SettingName.bImmortalMiner, new SettingSimple<bool>(false));
			AllSettings.Add(SettingName.bUseRemoteControl, new SettingSimple<bool>(false));

			AllSettings.Add(SettingName.fDefaultSpeed, new SettingMinMax<float>(1, float.MaxValue, 100));
			AllSettings.Add(SettingName.fMaxSpeed, new SettingMinMax<float>(10, float.MaxValue, float.MaxValue));
			AllSettings.Add(SettingName.fMaxWeaponRange, new SettingMinMax<float>(100, float.MaxValue, 800));
		}

		/// <summary>
		/// Read all settings from file
		/// </summary>
		/// <returns>version of file</returns>
		private Version readAll()
		{
			if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(settings_file_name, typeof(ServerSettings)))
				return new Version(-1); // no file

			TextReader settingsReader = null;
			try
			{
				settingsReader = MyAPIGateway.Utilities.ReadFileInLocalStorage(settings_file_name, typeof(ServerSettings));

				string[] versionLine = settingsReader.ReadLine().Split('=');
				if (versionLine.Length != 2 || !versionLine[0].Equals(strVersion))
					return new Version(-2); // first line is not version

				Version fileVersion = new Version(versionLine[1]);

				// read settings
				while (true)
				{
					string line = settingsReader.ReadLine();
					if (line == null)
						break;
					parse(line);
				}

				return fileVersion;
			}
			catch (Exception ex)
			{
				Logger.AlwaysLog("Failed to read settings from " + settings_file_name + ": " + ex, Rynchodon.Logger.severity.WARNING);
				return new Version(-4); // exception while reading
			}
			finally
			{
				if (settingsReader != null)
					settingsReader.Close();
			}
		}

		/// <summary>
		/// Write all settings to file. Only server should call this!
		/// </summary>
		private void writeAll()
		{
			try
			{
				settingsWriter = MyAPIGateway.Utilities.WriteFileInLocalStorage(settings_file_name, typeof(ServerSettings));

				write(strVersion, m_currentVersion.ToString()); // must be first line

				// write settings
				foreach (KeyValuePair<SettingName, Setting> pair in AllSettings)
					write(pair.Key.ToString(), pair.Value.ValueAsString());

				settingsWriter.Flush();
			}
			catch (Exception ex)
			{ Logger.AlwaysLog("Failed to write settings to " + settings_file_name + ": " + ex, Rynchodon.Logger.severity.WARNING); }
			finally
			{
				if (settingsWriter != null)
				{
					settingsWriter.Close();
					settingsWriter = null;
				}
			}
		}

		/// <summary>
		/// write a single setting to file, format is name=value
		/// </summary>
		/// <param name="name">name of setting</param>
		/// <param name="value">value of setting</param>
		private void write(string name, string value)
		{
			StringBuilder toWrite = new StringBuilder();
			toWrite.Append(name);
			toWrite.Append('=');
			toWrite.Append(value);
			settingsWriter.WriteLine(toWrite);
		}

		/// <summary>
		/// convert a line of format name=value into a setting and apply it
		/// </summary>
		private void parse(string line)
		{
			string[] split = line.Split('=');

			if (split.Length != 2)
			{
				Logger.AlwaysLog("split wrong length: " + split.Length + ", line: " + line, Rynchodon.Logger.severity.WARNING);
				return;
			}

			SettingName name;
			if (Enum.TryParse<SettingName>(split[0], out name))
				try
				{
					if (AllSettings[name].ValueFromString(split[1]))
						Logger.AlwaysLog("Setting " + name + " = " + split[1], Rynchodon.Logger.severity.INFO);
				}
				catch (Exception)
				{ Logger.AlwaysLog("failed to parse: " + split[1] + " for " + name, Rynchodon.Logger.severity.WARNING); }
			else
				Logger.AlwaysLog("Setting does not exist: " + split[0], Rynchodon.Logger.severity.WARNING);
		}

	}
}
