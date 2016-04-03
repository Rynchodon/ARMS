using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Sandbox.ModAPI;

namespace Rynchodon.Settings
{
	/// <summary>
	/// <para>Per-user settings that will be saved locally and can be changed at any time.</para>
	/// </summary>
	public class UserSettings
	{
		public enum ByteSettingName : byte
		{
			None,
			EnemiesOnHUD,
			NeutralOnHUD,
			FactionOnHUD,
			OwnerOnHUD,
			MissileOnHUD,
			UpdateIntervalHUD,
		}

		public enum BoolSettingName : byte
		{
			MissileWarning
		}

		private const string userSettings_fileName = "UserSettings.txt";
		private const string chatDesignator = "/arms set";
		private const string alt_chatDesignator = "/autopilot set";

		private static UserSettings Instance;

		static UserSettings()
		{
			Instance = new UserSettings();
		}

		public static byte GetSetting(ByteSettingName name)
		{
			return Instance.ByteSettings[name].Value;
		}

		public static bool GetSetting(BoolSettingName name)
		{
			return Instance.BoolSettings[name].Value;
		}

		public static void SetSetting(ByteSettingName name, byte value)
		{
			if (Instance.ByteSettings[name].Value == value)
				return;

			Instance.myLogger.debugLog("Setting " + name + " to " + value, "SetSetting()", Logger.severity.DEBUG);
			Instance.ByteSettings[name].Value = value;
			Instance.writeAll();
		}

		public static void SetSetting(BoolSettingName name, bool value)
		{
			if (Instance.BoolSettings[name].Value == value)
				return;

			Instance.myLogger.debugLog("Setting " + name + " to " + value, "SetSetting()", Logger.severity.DEBUG);
			Instance.BoolSettings[name].Value = value;
			Instance.writeAll();
		}

		public static void SetSetting(string nameValue)
		{
			Instance.SetSetting_FromString(nameValue);
		}

		private readonly Dictionary<ByteSettingName, SettingSimple<byte>> ByteSettings = new Dictionary<ByteSettingName, SettingSimple<byte>>();
		private readonly Dictionary<BoolSettingName, SettingSimple<bool>> BoolSettings = new Dictionary<BoolSettingName, SettingSimple<bool>>();
		private readonly Logger myLogger;

		private UserSettings()
		{
			this.myLogger = new Logger(GetType().Name);

			buildSettings();
			readAll();
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			Instance = null;
		}

		/// <summary>
		/// put each setting into AllSettings with its default value
		/// </summary>
		private void buildSettings()
		{
			ByteSettings.Add(ByteSettingName.EnemiesOnHUD, new SettingSimple<byte>(5));
			ByteSettings.Add(ByteSettingName.NeutralOnHUD, new SettingSimple<byte>(5));
			ByteSettings.Add(ByteSettingName.FactionOnHUD, new SettingSimple<byte>(5));
			ByteSettings.Add(ByteSettingName.OwnerOnHUD, new SettingSimple<byte>(5));
			ByteSettings.Add(ByteSettingName.MissileOnHUD, new SettingSimple<byte>(5));
			ByteSettings.Add(ByteSettingName.UpdateIntervalHUD, new SettingSimple<byte>(100));

			BoolSettings.Add(BoolSettingName.MissileWarning, new SettingSimple<bool>(true));
		}

		/// <summary>
		/// Read all settings from file
		/// </summary>
		/// <returns>version of file</returns>
		private void readAll()
		{
			if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(userSettings_fileName, typeof(UserSettings)))
				return;

			TextReader settingsReader = null;
			try
			{
				settingsReader = MyAPIGateway.Utilities.ReadFileInLocalStorage(userSettings_fileName, typeof(ServerSettings));

				// read settings
				while (true)
				{
					string line = settingsReader.ReadLine();
					if (line == null)
						break;
					parse(line);
				}
			}
			catch (Exception ex)
			{
				myLogger.alwaysLog("Failed to read settings from " + userSettings_fileName + ": " + ex, "writeAll()", Logger.severity.WARNING);
				Logger.debugNotify("Failed to read user settings from file", 10000, Logger.severity.WARNING);
			}
			finally
			{
				if (settingsReader != null)
					settingsReader.Close();
			}
		}

		/// <summary>
		/// write all settings to file
		/// </summary>
		private void writeAll()
		{
			TextWriter settingsWriter = null;
			try
			{
				settingsWriter = MyAPIGateway.Utilities.WriteFileInLocalStorage(userSettings_fileName, typeof(ServerSettings));

				// write settings
				foreach (KeyValuePair<ByteSettingName, SettingSimple<byte>> pair in ByteSettings)
					write(settingsWriter, pair.Key.ToString(), pair.Value.ValueAsString());

				settingsWriter.Flush();
			}
			catch (Exception ex)
			{ myLogger.alwaysLog("Failed to write settings to " + userSettings_fileName + ":\n" + ex, "writeAll()", Logger.severity.WARNING); }
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
		private void write(TextWriter settingsWriter, string name, string value)
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
				myLogger.alwaysLog("split wrong length: " + split.Length + ", line: " + line, "parse()", Logger.severity.WARNING);
				return;
			}

			ByteSettingName name;
			if (Enum.TryParse<ByteSettingName>(split[0], out name))
				try
				{
					if (ByteSettings[name].ValueFromString(split[1]))
						myLogger.alwaysLog("Set " + name + " to " + split[1], "parse()", Logger.severity.INFO);
				}
				catch (Exception)
				{ myLogger.alwaysLog("failed to parse: " + split[1] + " for " + name, "parse()", Logger.severity.WARNING); }
			else
				myLogger.alwaysLog("Setting does not exist: " + split[0], "parse()", Logger.severity.WARNING);
		}

		private void SetSetting_FromString(string nameValue)
		{
			string[] split = nameValue.Split(new char[] { ' ' });
			ByteSettingName name = ByteSettingName.None;

			// variations
			if (split[0].Contains("hud"))
			{
				if (split[0].Contains("enemy") || split[0].Contains("enemies"))
				{
					myLogger.debugLog("EnemiesOnHUD variation: " + split[0], "ChatHandler()");
					name = ByteSettingName.EnemiesOnHUD;
				}
				else if (split[0].Contains("neutral"))
				{
					myLogger.debugLog("NeutralOnHUD variation: " + split[0], "ChatHandler()");
					name = ByteSettingName.NeutralOnHUD;
				}
				else if (split[0].Contains("faction"))
				{
					myLogger.debugLog("FactionOnHUD variation: " + split[0], "ChatHandler()");
					name = ByteSettingName.FactionOnHUD;
				}
				else if (split[0].Contains("owner"))
				{
					myLogger.debugLog("OwnerOnHUD variation: " + split[0], "ChatHandler()");
					name = ByteSettingName.OwnerOnHUD;
				}
				else if (split[0].Contains("missile"))
				{
					myLogger.debugLog("MissileOnHUD variation: " + split[0], "ChatHandler()");
					name = ByteSettingName.MissileOnHUD;
				}
				else if (split[0].Contains("interval") || split[0].Contains("update"))
				{
					myLogger.debugLog("UpdateIntervalHUD variation: " + split[0], "ChatHandler()");
					name = ByteSettingName.UpdateIntervalHUD;
				}
			}
			else if (split[0].Contains("missile") || split[0].Contains("warning"))
			{
				myLogger.debugLog("IntervalMissileWarning variation: " + split[0], "ChatHandler()");
				bool warn;
				if (!bool.TryParse(split[1], out warn))
				{
					myLogger.debugLog("failed to parse: " + split[1], "ChatHandler()", Logger.severity.INFO);
					MyAPIGateway.Utilities.ShowMessage("ARMS", "Failed to parse: \"" + split[1] + '"');
				}
				else
				{
					SetSetting(BoolSettingName.MissileWarning, warn);
					MyAPIGateway.Utilities.ShowMessage("ARMS", "Set " + name + " to " + warn);
				}
				return;
			}

			if (name == ByteSettingName.None)
			{
				myLogger.debugLog("failed to find enum for " + split[0], "ChatHandler()", Logger.severity.INFO);
				MyAPIGateway.Utilities.ShowMessage("ARMS", "Failed, not a setting: \"" + split[0] + '"');
				return;
			}

			byte value;
			if (!byte.TryParse(split[1], out value))
			{
				myLogger.debugLog("failed to parse: " + split[1], "ChatHandler()", Logger.severity.INFO);
				MyAPIGateway.Utilities.ShowMessage("ARMS", "Failed to parse: \"" + split[1] + '"');
				return;
			}

			SetSetting(name, value);
			MyAPIGateway.Utilities.ShowMessage("ARMS", "Set " + name + " to " + value);
		}

	}
}
