using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Sandbox.ModAPI;
using VRageMath;

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
			None,
			MissileWarning,
			HologramShowBoundary
		}

		public enum IntSettingName : byte
		{
			None,
			IntegrityFull,
			IntegrityFunctional,
			IntegrityDamaged,
			IntegrityZero
		}

		private const string userSettings_fileName = "UserSettings.txt";

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

		public static uint GetSetting(IntSettingName name)
		{
			return Instance.IntSettings[name].Value;
		}

		public static void SetSetting(ByteSettingName name, byte value, bool notify = false)
		{
			if (notify)
				MyAPIGateway.Utilities.ShowMessage("ARMS", "Set " + name + " to " + value);

			if (Instance.ByteSettings[name].Value == value)
				return;

			Instance.myLogger.debugLog("Setting " + name + " to " + value, Logger.severity.DEBUG);
			Instance.ByteSettings[name].Value = value;
		}

		public static void SetSetting(BoolSettingName name, bool value, bool notify = false)
		{
			if (notify)
				MyAPIGateway.Utilities.ShowMessage("ARMS", "Set " + name + " to " + value);

			if (Instance.BoolSettings[name].Value == value)
				return;

			Instance.myLogger.debugLog("Setting " + name + " to " + value, Logger.severity.DEBUG);
			Instance.BoolSettings[name].Value = value;
		}

		public static void SetSetting(IntSettingName name, uint value)
		{
			Instance.IntSettings[name].Value = value;
		}

		public static void SetSetting(string nameValue)
		{
			Instance.SetSetting_FromString(nameValue);
		}

		private readonly Dictionary<ByteSettingName, SettingSimple<byte>> ByteSettings = new Dictionary<ByteSettingName, SettingSimple<byte>>();
		private readonly Dictionary<BoolSettingName, SettingSimple<bool>> BoolSettings = new Dictionary<BoolSettingName, SettingSimple<bool>>();
		private readonly Dictionary<IntSettingName, SettingSimple<uint>> IntSettings = new Dictionary<IntSettingName, SettingSimple<uint>>();
		
		private readonly Logger myLogger;

		private UserSettings()
		{
			this.myLogger = new Logger();

			buildSettings();
			readAll();
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			Instance = null;
			writeAll();
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
			BoolSettings.Add(BoolSettingName.HologramShowBoundary, new SettingSimple<bool>(true));

			IntSettings.Add(IntSettingName.IntegrityFull, new SettingSimple<uint>(Color.DarkGreen.PackedValue));
			IntSettings.Add(IntSettingName.IntegrityFunctional, new SettingSimple<uint>(Color.Yellow.PackedValue));
			IntSettings.Add(IntSettingName.IntegrityDamaged, new SettingSimple<uint>(Color.Red.PackedValue));
			IntSettings.Add(IntSettingName.IntegrityZero, new SettingSimple<uint>(Color.Gray.PackedValue));
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
				myLogger.alwaysLog("Failed to read settings from " + userSettings_fileName + ": " + ex, Logger.severity.WARNING);
				Logger.DebugNotify("Failed to read user settings from file", 10000, Logger.severity.WARNING);
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

				foreach (KeyValuePair<BoolSettingName, SettingSimple<bool>> pair in BoolSettings)
					write(settingsWriter, pair.Key.ToString(), pair.Value.ValueAsString());

				foreach (KeyValuePair<IntSettingName, SettingSimple<uint>> pair in IntSettings)
					write(settingsWriter, pair.Key.ToString(), pair.Value.ValueAsString());

				settingsWriter.Flush();
			}
			catch (Exception ex)
			{ myLogger.alwaysLog("Failed to write settings to " + userSettings_fileName + ":\n" + ex, Logger.severity.WARNING); }
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
				myLogger.alwaysLog("split wrong length: " + split.Length + ", line: " + line, Logger.severity.WARNING);
				return;
			}

			ByteSettingName byteSet;
			if (Enum.TryParse<ByteSettingName>(split[0], out byteSet))
			{	try
				{
					if (ByteSettings[byteSet].ValueFromString(split[1]))
						myLogger.alwaysLog("Set " + byteSet + " to " + split[1], Logger.severity.INFO);
				}
				catch (Exception)
				{ myLogger.alwaysLog("failed to parse: " + split[1] + " for " + byteSet, Logger.severity.WARNING); }
				return;
			}

			BoolSettingName boolSet;
			if (Enum.TryParse<BoolSettingName>(split[0], out boolSet))
			{
				try
				{
					if (BoolSettings[boolSet].ValueFromString(split[1]))
						myLogger.alwaysLog("Set " + boolSet + " to " + split[1], Logger.severity.INFO);
				}
				catch (Exception)
				{ myLogger.alwaysLog("failed to parse: " + split[1] + " for " + boolSet, Logger.severity.WARNING); }
				return;
			}

			IntSettingName uintSet;
			if (Enum.TryParse<IntSettingName>(split[0], out uintSet))
			{
				try
				{
					if (IntSettings[uintSet].ValueFromString(split[1]))
						myLogger.alwaysLog("Set " + uintSet + " to " + split[1], Logger.severity.INFO);
				}
				catch (Exception)
				{ myLogger.alwaysLog("failed to parse: " + split[1] + " for " + uintSet, Logger.severity.WARNING); }
				return;
			}

			myLogger.alwaysLog("Setting does not exist: " + split[0], Logger.severity.WARNING);
		}

		private void SetSetting_FromString(string nameValue)
		{
			try
			{
				if (nameValue == null)
				{
					MyAPIGateway.Utilities.ShowMessage("ARMS", "Failed to parse: " + nameValue);
					return;
				}

				string[] split = nameValue.Split(new char[] { ' ' });

				if (split.Length < 2)
				{
					MyAPIGateway.Utilities.ShowMessage("ARMS", "Failed to parse: " + nameValue);
					return;
				}

				string name = split[0];
				string value = split[1];

				ByteSettingName byteSet;
				if (Enum.TryParse(name, out byteSet))
				{
					byte y;
					if (byte.TryParse(value, out y))
						SetSetting(byteSet, y, true);
					else
					{
						myLogger.debugLog("failed to parse as byte: " + value, Logger.severity.INFO);
						MyAPIGateway.Utilities.ShowMessage("ARMS", "Not a byte: \"" + value + '"');
					}
					return;
				}

				BoolSettingName boolSet;
				if (Enum.TryParse(name, out boolSet))
				{
					bool b;
					if (bool.TryParse(value, out b))
						SetSetting(boolSet, b, true);
					else
					{
						myLogger.debugLog("failed to parse as bool: " + value, Logger.severity.INFO);
						MyAPIGateway.Utilities.ShowMessage("ARMS", "Not a bool: \"" + value + '"');
					}
					return;
				}

				if (SetSetting_FuzzyByte(name, value))
					return;

				if (SetSetting_FuzzyBool(name, value))
					return;

				myLogger.debugLog("failed to find enum for " + name, Logger.severity.INFO);
				MyAPIGateway.Utilities.ShowMessage("ARMS", "Failed, not a setting: \"" + name + '"');
				return;

			}
			catch (Exception ex)
			{
				myLogger.debugLog("Exception: " + ex, Logger.severity.ERROR);
				Logger.Notify("Error while parsing set command", 10000, Logger.severity.ERROR);
			}
		}

		private bool SetSetting_FuzzyByte(string name, string value)
		{
			if (!name.Contains("hud"))
				return false;

			ByteSettingName setting = ByteSettingName.None;
			if (name.Contains("enemy") || name.Contains("enemies"))
				setting = ByteSettingName.EnemiesOnHUD;
			else if (name.Contains("neutral"))
				setting = ByteSettingName.NeutralOnHUD;
			else if (name.Contains("faction"))
				setting = ByteSettingName.FactionOnHUD;
			else if (name.Contains("owner"))
				setting = ByteSettingName.OwnerOnHUD;
			else if (name.Contains("missile"))
				setting = ByteSettingName.MissileOnHUD;
			else if (name.Contains("interval") || name.Contains("update"))
				setting = ByteSettingName.UpdateIntervalHUD;

			if (setting == ByteSettingName.None)
				return false;

			myLogger.debugLog(setting + " variation: " + name);

			byte y;
			if (byte.TryParse(value, out y))
				SetSetting(setting, y, true);
			else
			{
				myLogger.debugLog("failed to parse as byte: " + value, Logger.severity.INFO);
				MyAPIGateway.Utilities.ShowMessage("ARMS", "Not a byte: \"" + value + '"');
			}

			return true;
		}

		private bool SetSetting_FuzzyBool(string name, string value)
		{
			BoolSettingName setting = BoolSettingName.None;
			if (name.Contains("missile") || name.Contains("warning"))
				setting = BoolSettingName.MissileWarning;

			if (setting == BoolSettingName.None)
				return false;

			myLogger.debugLog(setting + " variation: " + name);

			bool b;
			if (bool.TryParse(value, out b))
				SetSetting(setting, b, true);
			else
			{
				myLogger.debugLog("failed to parse: " + value, Logger.severity.INFO);
				MyAPIGateway.Utilities.ShowMessage("ARMS", "Not a bool: \"" + value + '"');
			}

			return true;
		}

	}
}
