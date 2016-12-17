using System; // (partial) from mscorlib.dll
using System.Text; // from mscorlib.dll
using Rynchodon.Autopilot;
using Rynchodon.Settings;
using Sandbox.ModAPI; // from Sandbox.Common.dll

namespace Rynchodon.Utility
{
	public class ChatHandler
	{

		private char[] splitters = { ' ' };
		//private Help m_help = new Help();

		public ChatHandler()
		{
			MyAPIGateway.Utilities.MessageEntered += Utilities_MessageEntered;
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private void Entities_OnCloseAll()
		{
			MyAPIGateway.Utilities.MessageEntered -= Utilities_MessageEntered;
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
		}

		private void Utilities_MessageEntered(string messageText, ref bool sendToOthers)
		{
			messageText = messageText.ToLower();
			if (!messageText.StartsWith("/arms") && !messageText.StartsWith("/autopilot"))
				return;

			sendToOthers = false;

			string[] split = messageText.Split(splitters, 3, StringSplitOptions.RemoveEmptyEntries);

			if (split.Length < 2)
			{
				MyAPIGateway.Utilities.ShowMessage("ARMS", "Failed to parse: " + messageText);
				return;
			}

			string remainder = split.Length < 3 ? null : split[2];
			switch (split[1])
			{
				//case "help":
				//	m_help.printCommand(remainder);
				//	return;
				case "set":
					UserSettings.SetSetting(remainder);
					return;
				case "setting":
				case "settings":
					DisplayAllSettings();
					return;
				default:
					MyAPIGateway.Utilities.ShowMessage("ARMS", "Failed to parse: " + split[1]);
					return;
			}
		}

		private void DisplayAllSettings()
		{
			StringBuilder display = new StringBuilder();

			display.AppendLine("Server settings:");
			Append<bool>(display, ServerSettings.SettingName.bAirResistanceBeta);
			Append<bool>(display, ServerSettings.SettingName.bAllowAutopilot);
			Append<bool>(display, ServerSettings.SettingName.bAllowGuidedMissile);
			Append<bool>(display, ServerSettings.SettingName.bAllowHacker);
			Append<bool>(display, ServerSettings.SettingName.bAllowRadar);
			Append<bool>(display, ServerSettings.SettingName.bAllowWeaponControl);
			Append<bool>(display, ServerSettings.SettingName.bImmortalMiner);
			Append<bool>(display, ServerSettings.SettingName.bUseRemoteControl);
			Append<float>(display, ServerSettings.SettingName.fDefaultSpeed);
			Append<float>(display, ServerSettings.SettingName.fMaxSpeed);
			Append<float>(display, ServerSettings.SettingName.fMaxWeaponRange);

			display.AppendLine("\nUser settings:");
			Append(display, UserSettings.ByteSettingName.EnemiesOnHUD);
			Append(display, UserSettings.ByteSettingName.NeutralOnHUD);
			Append(display, UserSettings.ByteSettingName.FactionOnHUD);
			Append(display, UserSettings.ByteSettingName.OwnerOnHUD);
			Append(display, UserSettings.ByteSettingName.MissileOnHUD);
			Append(display, UserSettings.ByteSettingName.UpdateIntervalHUD);
			Append(display, UserSettings.BoolSettingName.MissileWarning);

			MyAPIGateway.Utilities.ShowMissionScreen("ARMS Settings", string.Empty, string.Empty, display.ToString());
		}

		private void Append<T>(StringBuilder builder, ServerSettings.SettingName setName) where T : struct
		{
			builder.Append(setName);
			builder.Append('=');
			builder.Append(ServerSettings.GetSetting<T>(setName));
			builder.AppendLine();
		}

		private void Append(StringBuilder builder, UserSettings.ByteSettingName setName)
		{
			builder.Append(setName);
			builder.Append('=');
			builder.Append(UserSettings.GetSetting(setName));
			builder.AppendLine();
		}

		private void Append(StringBuilder builder, UserSettings.BoolSettingName setName)
		{
			builder.Append(setName);
			builder.Append('=');
			builder.Append(UserSettings.GetSetting(setName));
			builder.AppendLine();
		}

	}
}
