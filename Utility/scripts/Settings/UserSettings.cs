using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Sandbox.ModAPI;

namespace Rynchodon.Settings
{
	/// <summary>
	/// <para>Per-user settings that will be saved locally and can be changed at any time.</para>
	/// </summary>
	public class UserSettings
	{
		// if more types of settings are added, it is important that the names do not have the same byte value
		public enum ByteSettingName : byte
		{
			EnemiesOnHUD = 0,
			NeutralOnHUD = 1,
			FactionOnHUD = 2,
			OwnerOnHUD = 3
		}

		private const ushort ModID = 54309; // no idea what this is supposed to be
		private const string userSettings_fileName = "UserSettings.txt";
		private const string chatDesignator = "/autopilot set";

		private static readonly Dictionary<long, UserSettings> User = new Dictionary<long, UserSettings>();
		private static readonly Logger staticLogger = new Logger("static", "UserSettings");

		static UserSettings()
		{
			MyAPIGateway.Multiplayer.RegisterMessageHandler(UserSettings.ModID, ReceiveMessageUserSetting);
		}

		/// <summary>
		/// Creates a UserSettings for the local user.
		/// </summary>
		public static UserSettings CreateLocal()
		{
			if (MyAPIGateway.Session.Player == null)
				return null;

			return new UserSettings(MyAPIGateway.Session.Player.PlayerID, true);
		}

		/// <summary>
		/// Creates a UserSettings for the server.
		/// </summary>
		public static UserSettings CreateServer(long playerID)
		{
			return new UserSettings(playerID, false);
		}

		public static UserSettings GetForPlayer(long playerID)
		{
			UserSettings useSet;
			if (User.TryGetValue(playerID, out useSet))
				return useSet;
			useSet = CreateServer(playerID);
			User.Add(playerID, useSet);
			return useSet;
		}

		public static void OnPlayerLeave(IMyPlayer player)
		{
			if (User.Remove(player.PlayerID))
				staticLogger.debugLog("removed settings for: " + player.DisplayName + '(' + player.PlayerID + ')', "OnPlayerLeave()");
		}

		private static void ReceiveMessageUserSetting(byte[] message)
		{
			int pos = 0;
			long playerID = ByteConverter.GetLong(message, ref pos);
			UserSettings useSet;
			if (!User.TryGetValue(playerID, out useSet))
			{
				useSet = UserSettings.CreateServer(playerID);
				User.Add(playerID, useSet);
			}
			useSet.SetSetting(message, ref pos);
		}

		public readonly long PlayerID;
		public readonly bool Local;

		private readonly Dictionary<ByteSettingName, SettingSimple<byte>> ByteSettings = new Dictionary<ByteSettingName, SettingSimple<byte>>();
		private readonly Logger myLogger;

		private UserSettings(long playerID, bool local)
		{
			this.PlayerID = playerID;
			this.Local = local;

			if (Local)
				this.myLogger = new Logger("Local (" + playerID + ")", "UserSettings");
			else
				this.myLogger = new Logger("Server (" + playerID + ")", "UserSettings");

			buildSettings();
			if (Local)
			{
				readAll();
				MyAPIGateway.Utilities.MessageEntered += ChatHandler;
				MyAPIGateway.Entities.OnCloseAll += () => { MyAPIGateway.Utilities.MessageEntered -= ChatHandler; };
			}
		}

		public byte GetSetting(ByteSettingName name)
		{
			return ByteSettings[name].Value;
		}

		public void SetSetting(ByteSettingName name, byte value)
		{
			if (ByteSettings[name].Value == value)
				return;

			myLogger.debugLog("Setting " + name + " to " + value, "SetSetting()", Logger.severity.DEBUG);
			ByteSettings[name].Value = value;
			if (Local)
			{
				SendSettingToServer(name);
				writeAll();
			}
		}

		public void SetSetting(byte[] message, ref int pos)
		{
			ByteSettingName name = (ByteSettingName)ByteConverter.GetByte(message, ref pos);
			SetSetting(name, message[pos]);
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
			{ myLogger.alwaysLog("Failed to write settings to " + userSettings_fileName + ": " + ex, "writeAll()", Logger.severity.WARNING); }
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
					{
						myLogger.alwaysLog("Set " + name + " to " + split[1], "parse()", Logger.severity.INFO);
						SendSettingToServer(name);
					}
				}
				catch (Exception)
				{ myLogger.alwaysLog("failed to parse: " + split[1] + " for " + name, "parse()", Logger.severity.WARNING); }
			else
				myLogger.alwaysLog("Setting does not exist: " + split[0], "parse()", Logger.severity.WARNING);
		}

		private void SendSettingToServer(ByteSettingName name)
		{
			byte[] message = new byte[11];
			int pos = 0;
			ByteConverter.AppendBytes(PlayerID, message, ref pos);
			ByteConverter.AppendBytes((byte)name, message, ref pos);
			ByteConverter.AppendBytes(GetSetting(name), message, ref pos);

			if (!MyAPIGateway.Multiplayer.SendMessageToServer(ModID, message, true))
				myLogger.debugLog("message failed", "SendSettingToServer()", Logger.severity.ERROR);
			else
				myLogger.debugLog("sent setting to server", "SendSettingToServer()");
		}

		private void ChatHandler(string message, ref bool sendToOthers)
		{
			message = message.ToLower();
			if (!message.StartsWith(chatDesignator))
				return;

			string[] nameValue = message.Replace(chatDesignator, "").Trim().Split(' ');
			sendToOthers = false;

			ByteSettingName? name = null;
			string nameLower = nameValue[0].ToLower();

			// variations
			if (nameLower.Contains("hud"))
			{
				if (nameLower.Contains("enemy") || nameLower.Contains("enemies"))
				{
					myLogger.debugLog("EnemiesOnHUD variation: " + nameValue[0], "ChatHandler()");
					name = ByteSettingName.EnemiesOnHUD;
				}
				else if (nameLower.Contains("neutral"))
				{
					myLogger.debugLog("NeutralOnHUD variation: " + nameValue[0], "ChatHandler()");
					name = ByteSettingName.NeutralOnHUD;
				}
				else if (nameLower.Contains("faction"))
				{
					myLogger.debugLog("FactionOnHUD variation: " + nameValue[0], "ChatHandler()");
					name = ByteSettingName.FactionOnHUD;
				}
				else if (nameLower.Contains("owner"))
				{
					myLogger.debugLog("OwnerOnHUD variation: " + nameValue[0], "ChatHandler()");
					name = ByteSettingName.OwnerOnHUD;
				}
			}

			if (name == null)
			{
				myLogger.debugLog("failed to find enum for " + nameValue[0], "ChatHandler()", Logger.severity.INFO);
				MyAPIGateway.Utilities.ShowMessage("Autopilot", "Failed, not a setting: \"" + nameValue[0] + '"');
				return;
			}

			byte value;
			if (!byte.TryParse(nameValue[1], out value))
			{
				myLogger.debugLog("failed to parse: " + nameValue[1], "ChatHandler()", Logger.severity.INFO);
				MyAPIGateway.Utilities.ShowMessage("Autopilot", "Failed to parse: \"" + nameValue[1] + '"');
				return;
			}

			SetSetting(name.Value, value);
			MyAPIGateway.Utilities.ShowMessage("Autopilot", "Set " + name.Value + " to " + value);
		}

	}
}
