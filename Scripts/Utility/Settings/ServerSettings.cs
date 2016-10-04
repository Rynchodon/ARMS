using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using Sandbox.ModAPI;
using Rynchodon.Utility.Network;

namespace Rynchodon.Settings
{
	/// <summary>
	/// Some of the settings will be made available to clients.
	/// Clients should still not be able to affect balance in any way, only to update hud, GPS, etc.
	/// </summary>
	public static class ServerSettings
	{
		public enum SettingName : byte
		{
			bAllowAutopilot, bAllowGuidedMissile, bAllowHacker, bAllowRadar, bAllowWeaponControl, bImmortalMiner, bUseRemoteControl, 
			yParallelPathfinder,
			fDefaultSpeed, fMaxSpeed, fMaxWeaponRange
		}

		private static ushort ModID { get { return MessageHandler.ModId; } }

		private static Dictionary<SettingName, Setting> AllSettings = new Dictionary<SettingName, Setting>();

		/// <exception cref="NullReferenceException">if setting does not exist or is of a different type</exception>
		public static T GetSetting<T>(SettingName name) where T : struct
		{
			SettingSimple<T> set = AllSettings[name] as SettingSimple<T>;
			return set.Value;
		}

		private static void SetSetting<T>(SettingName name, T value) where T : struct
		{
			SettingSimple<T> set = AllSettings[name] as SettingSimple<T>;
			set.Value = value;
			myLogger.alwaysLog("Setting " + name + " = " + value, Logger.severity.INFO);
		}

		/// <exception cref="NullReferenceException">if setting does not exist or is of a different type</exception>
		public static string GetSettingString(SettingName name)
		{
			SettingString set = AllSettings[name] as SettingString;
			return set.Value;
		}

		private const string modName = "ARMS";
		private const string settings_file_name = "AutopilotSettings.txt";
		private const string strVersion = "Version";
		private static System.IO.TextWriter settingsWriter;

		public const int latestVersion = 76; // in sequence of updates on steam

		public static readonly int fileVersion;

		private static Logger myLogger = new Logger();

		private static bool SettingsLoaded;

		public static bool ServerSettingsLoaded { get { return SettingsLoaded; } }

		static ServerSettings()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
			buildSettings();

			if (MyAPIGateway.Multiplayer.IsServer)
			{
				SettingsLoaded = true;
				MessageHandler.Handlers.Add(MessageHandler.SubMod.ServerSettings, Server_ReceiveMessage);

				fileVersion = readAll();
				if (fileVersion != latestVersion)
					Logger.DebugNotify(modName + " has been updated to version " + latestVersion, 10000, Logger.severity.INFO);
				myLogger.alwaysLog("file version: " + fileVersion + ", latest version: " + latestVersion, Logger.severity.INFO);

				writeAll(); // writing immediately decreases user errors & whining
			}
			else
			{
				MessageHandler.Handlers.Add(MessageHandler.SubMod.ServerSettings, Client_ReceiveMessage);
				RequestSettingsFromServer();
			}
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			AllSettings = null;
			myLogger = null;
		}

		private static void Server_ReceiveMessage(byte[] message, int pos)
		{
			try
			{
				if (message == null)
				{
					myLogger.debugLog("Message is null");
					return;
				}
				if( message.Length < 8)
				{
					myLogger.debugLog("Message is too short: " + message.Length);
					return;
				}

				ulong SteamUserId = ByteConverter.GetUlong(message, ref pos);

				myLogger.debugLog("Received request from: " + SteamUserId);

				List<byte> send = new List<byte>();
				ByteConverter.AppendBytes(send, (byte)MessageHandler.SubMod.ServerSettings);
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
					myLogger.debugLog("Sent settings to " + SteamUserId, Logger.severity.INFO);
				else
					myLogger.alwaysLog("Failed to send settings to " + SteamUserId, Logger.severity.ERROR);
			}
			catch (Exception ex)
			{ myLogger.alwaysLog("Exception: " + ex, Logger.severity.ERROR); }
		}

		private static void Client_ReceiveMessage(byte[] message, int pos)
		{
			try
			{
				myLogger.debugLog("Received settings from server");

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

				SettingsLoaded = true;
			}
			catch (Exception ex)
			{ myLogger.alwaysLog("Exception: " + ex, Logger.severity.ERROR); }
		}

		private static void RequestSettingsFromServer()
		{
			if (MyAPIGateway.Session.Player == null)
			{
				myLogger.alwaysLog("Could not get player, not requesting server settings.", Logger.severity.WARNING);
				SettingsLoaded = true;
				return;
			}

			List<byte> bytes = new List<byte>();
			ByteConverter.AppendBytes(bytes, (byte)MessageHandler.SubMod.ServerSettings);
			ByteConverter.AppendBytes(bytes, MyAPIGateway.Session.Player.SteamUserId);

			if (MyAPIGateway.Multiplayer.SendMessageToServer(ModID, bytes.ToArray()))
				myLogger.debugLog("Sent request to server", Logger.severity.INFO);
			else
				myLogger.alwaysLog("Failed to send request to server", Logger.severity.ERROR);
		}

		/// <summary>
		/// put each setting into AllSettings with its default value
		/// </summary>
		private static void buildSettings()
		{
			AllSettings.Add(SettingName.bAllowAutopilot, new SettingSimple<bool>(true));
			AllSettings.Add(SettingName.bAllowGuidedMissile, new SettingSimple<bool>(true));
			AllSettings.Add(SettingName.bAllowHacker, new SettingSimple<bool>(true));
			AllSettings.Add(SettingName.bAllowRadar, new SettingSimple<bool>(true));
			AllSettings.Add(SettingName.bAllowWeaponControl, new SettingSimple<bool>(true));
			AllSettings.Add(SettingName.bImmortalMiner, new SettingSimple<bool>(false));
			AllSettings.Add(SettingName.bUseRemoteControl, new SettingSimple<bool>(false));

			AllSettings.Add(SettingName.yParallelPathfinder, new SettingMinMax<byte>(1, 100, 4));

			AllSettings.Add(SettingName.fDefaultSpeed, new SettingMinMax<float>(1, float.MaxValue, 100));
			AllSettings.Add(SettingName.fMaxSpeed, new SettingMinMax<float>(10, float.MaxValue, float.MaxValue));
			AllSettings.Add(SettingName.fMaxWeaponRange, new SettingMinMax<float>(100, float.MaxValue, 800));
		}

		/// <summary>
		/// Read all settings from file
		/// </summary>
		/// <returns>version of file</returns>
		private static int readAll()
		{
			if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(settings_file_name, typeof(ServerSettings)))
				return -1; // no file

			TextReader settingsReader = null;
			try
			{
				settingsReader = MyAPIGateway.Utilities.ReadFileInLocalStorage(settings_file_name, typeof(ServerSettings));

				string[] versionLine = settingsReader.ReadLine().Split('=');
				if (versionLine.Length != 2 || !versionLine[0].Equals(strVersion))
					return -2; // first line is not version
				int fileVersion;
				if (!int.TryParse(versionLine[1], out fileVersion))
					return -3; // could not parse version

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
				myLogger.alwaysLog("Failed to read settings from " + settings_file_name + ": " + ex, Logger.severity.WARNING);
				return -4; // exception while reading
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
		private static void writeAll()
		{
			try
			{
				settingsWriter = MyAPIGateway.Utilities.WriteFileInLocalStorage(settings_file_name, typeof(ServerSettings));

				write(strVersion, latestVersion.ToString()); // must be first line

				// write settings
				foreach (KeyValuePair<SettingName, Setting> pair in AllSettings)
					write(pair.Key.ToString(), pair.Value.ValueAsString());

				settingsWriter.Flush();
			}
			catch (Exception ex)
			{ myLogger.alwaysLog("Failed to write settings to " + settings_file_name + ": " + ex, Logger.severity.WARNING); }
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
		private static void write(string name, string value)
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
		private static void parse(string line)
		{
			string[] split = line.Split('=');

			if (split.Length != 2)
			{
				myLogger.alwaysLog("split wrong length: " + split.Length + ", line: " + line, Logger.severity.WARNING);
				return;
			}

			SettingName name;
			if (Enum.TryParse<SettingName>(split[0], out name))
				try
				{
					if (AllSettings[name].ValueFromString(split[1]))
						myLogger.alwaysLog("Setting " + name + " = " + split[1], Logger.severity.INFO);
				}
				catch (Exception)
				{ myLogger.alwaysLog("failed to parse: " + split[1] + " for " + name, Logger.severity.WARNING); }
			else
				myLogger.alwaysLog("Setting does not exist: " + split[0], Logger.severity.WARNING);
		}

	}
}
