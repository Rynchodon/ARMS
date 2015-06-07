#define LOG_ENABLED // remove on build

using System.Collections.Generic;
using System.Text;

using Sandbox.ModAPI;

namespace Rynchodon.AntennaRelay
{
	// sent messages shall be written "DisplayName[.[grid:block:message].]"
	// received messages shall be written "DisplayName<.<grid:block:message>.>"
	public static class MessageParser
	{
		private const string startOfSend = "[.[", endOfSend = "].]", startOfReceive = "<.<", endOfReceive = ">.>";
		private const char separator = ':';

		/// <summary>
		/// gets the message that was sent from player / ingame programming
		/// </summary>
		public static List<Message> getFromName(IMyTerminalBlock sender)
		{
			string name = sender.DisplayNameText;

			int start = name.IndexOf(startOfSend) + startOfSend.Length;
			int length = name.LastIndexOf(endOfSend) - start;
			if (start <= startOfSend.Length || length < 1) // nothing to send
			{
				myLogger.debugLog("nothing to send for " + name, "getFromName()", Logger.severity.TRACE);
				return null;
			}

			// clear message from name
			string NameNew = name.Remove(start - startOfSend.Length);
			sender.SetCustomName(NameNew);

			string[] sent = name.Substring(start, length).Split(separator);
			string message;
			if (sent.Length < 3) // not possible to send
			{
				myLogger.debugLog("cannot send while split has length " + sent.Length + ", for " + name, "getFromName()", Logger.severity.DEBUG);
				return null;
			}
			else if (sent.Length == 3)
				message = sent[2];
			else // sent.Length > 3
			{
				StringBuilder messageSB = new StringBuilder();
				for (int index = 2; index < sent.Length; index++)
				{
					if (index != 2)
						messageSB.Append(separator);
					messageSB.Append(sent[index]);
				}
				message = messageSB.ToString();
			}
			return Message.buildMessages(message, sent[0], sent[1], sender as IMyCubeBlock, NameNew);
		}

		public static bool canWriteTo(IMyTerminalBlock recipient)
		{
			return !(recipient.DisplayNameText.Contains(startOfSend) || recipient.DisplayNameText.Contains(endOfSend)
				|| recipient.DisplayNameText.Contains(startOfReceive) || recipient.DisplayNameText.Contains(endOfReceive));
		}

		/// <summary>
		/// writes the message from mod to display name
		/// </summary>
		public static void writeToName(IMyTerminalBlock recipient, Message message)
		{
			recipient.SetCustomName(recipient.CustomName + startOfReceive + message.SourceGridName + separator + message.SourceBlockName + separator + message.Content + endOfReceive);
			return;
		}

		private static Logger myLogger = new Logger(null, "MessageParser");
	}
}
