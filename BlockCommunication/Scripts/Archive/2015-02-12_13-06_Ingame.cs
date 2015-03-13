using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using Sandbox.Common.ObjectBuilders;
using VRageMath;
using VRage;

namespace Programmable
{
	class Block
	{
		IMyGridTerminalSystem GridTerminalSystem;
		string Storage;
		// start of Programmable block code



		string MyBlockName = "Programmable block"; // case sensitive
		bool sendLightsOn = false;

		void Main()
		{
			if (ThisBlock == null)
				fillThisBlock();

			if (sendLightsOn)
				if (sendMessage("Platform B", "Programmable block", "Lights : On"))
					sendLightsOn = false;

			receiveMessage();
		}

		void parseReceivedMessage(string senderGrid, string senderBlock, string message)
		{
			if (message == "Lights : On")
			{
				List<IMyTerminalBlock> lightBlocks = new List<IMyTerminalBlock>();
				GridTerminalSystem.GetBlocksOfType<IMyInteriorLight>(lightBlocks);
				for (int i = 0; i < lightBlocks.Count; i++)
					lightBlocks[i].ApplyAction("OnOff_On");
			}
		}


		// do not have to edit past this line 
		IMyTerminalBlock ThisBlock; // will be filled by first Programmable block found that contains MyBlockName
		const string startOfSend = "[.[", endOfSend = "].]", startOfReceive = "<.<", endOfReceive = ">.>"; // has to match MessageParser.cs
		const char separator = ':'; // has to match MessageParser.cs

		void fillThisBlock()
		{
			List<IMyTerminalBlock> progBlocks = new List<IMyTerminalBlock>();
			GridTerminalSystem.GetBlocksOfType<IMyProgrammableBlock>(progBlocks);
			for (int i = 0; i < progBlocks.Count; i++)
				if (progBlocks[i].DisplayNameText.Trim().Contains(MyBlockName.Trim()))
				{
					ThisBlock = progBlocks[i];
					return;
				}
		}

		bool canWrite()
		{
			if (ThisBlock == null)
				return false;
			return !(ThisBlock.DisplayNameText.Contains(startOfSend) || ThisBlock.DisplayNameText.Contains(endOfSend)
				|| ThisBlock.DisplayNameText.Contains(startOfReceive) || ThisBlock.DisplayNameText.Contains(endOfReceive));
		}

		bool sendMessage(string recipientGrid, string recipientBlock, string message)
		{
			if (ThisBlock == null)
				return false;
			if (!canWrite())
				return false; // cannot send right now 

			ThisBlock.SetCustomName(ThisBlock.CustomName + startOfSend + recipientGrid + separator + recipientBlock + separator + message + endOfSend);
			return true; // successfully sent the message 
		}

		void receiveMessage()
		{
			if (ThisBlock == null)
				return;
			int start = ThisBlock.CustomName.IndexOf(startOfReceive) + startOfReceive.Length;
			int length = ThisBlock.CustomName.LastIndexOf(endOfReceive) - start;
			if (start <= startOfSend.Length || length < 1)
				return; // have not received a message 
			string[] received = ThisBlock.CustomName.Substring(start, length).Split(separator);

			// clear received from name 
			string NameNew = ThisBlock.CustomName.Remove(start - startOfSend.Length);
			ThisBlock.SetCustomName(NameNew);

			string message;
			if (received.Length < 3)
				return; // cannot parse, throw out 
			else if (received.Length == 3)
				message = received[2];
			else // senderAndMessage.Length > 3 
			{
				int index = 2;
				StringBuilder messageSB = new StringBuilder();
				while (index < received.Length)
				{
					if (index != 2) // skip on first 
						messageSB.Append(separator);
					messageSB.Append(received[index]);
					index++;
				}
				message = messageSB.ToString();
			}

			parseReceivedMessage(received[0], received[1], message); 
		} 



		// end of Programmable block code
	}
}