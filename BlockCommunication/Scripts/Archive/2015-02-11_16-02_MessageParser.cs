#define LOG_ENABLED // remove on build

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Sandbox.Common;
using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Engine;
using Sandbox.Game;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRageMath;

namespace Rynchodon.BlockCommunication
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
		public static Message getFromName(IMyTerminalBlock sender)
		{
			string name = sender.DisplayNameText;

			int start = name.IndexOf(startOfSend) + 1;
			int length = name.LastIndexOf(endOfSend) - start;
			if (start == 0 || length < 1)
				return null; // nothing to send

			// clear message from name
			string NameNew = sender.CustomName.Remove(start - 1);
			sender.SetCustomName(NameNew);

			string[] sent = name.Substring(start, length).Split(separator);
			string message;
			if (sent.Length < 3)
				return null; // not possible to send
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
			return new Message(sender, sent[0], sent[1], message);
		}

		/// <summary>
		/// writes the message from mod to display name
		/// </summary>
		public static bool writeToName(IMyTerminalBlock recipient, Message message)
		{
			if (recipient.DisplayNameText.Contains(startOfSend) || recipient.DisplayNameText.Contains(endOfSend)
				|| recipient.DisplayNameText.Contains(startOfReceive) || recipient.DisplayNameText.Contains(endOfReceive))
				return false; // cannot write at this moment

			recipient.SetCustomName(recipient.CustomName + startOfReceive + message.SourceGrid.DisplayName + separator + message.SourceBlock.DisplayNameText + separator + message + endOfReceive);
			return true;
		}
	}
}
