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
	class Program
	{
		IMyGridTerminalSystem GridTerminalSystem;
		string Storage;
		// start of Programmable block code



		/*
		 * Script for block communication and filtering detected grids.
		 * To send detected grid information to a Programmable block, use the command
		 *     [ Transmit Detected to <Programmable block name> ]
		 * in the name of a TextPanel.
		 */

		/// <summary>
		/// name of this block, case sensitive, first matching block will be used
		/// </summary>
		const string ThisBlockName = "Programmable block";
		/// <summary>
		/// name of output text panels, can match more than one block
		/// </summary>
		const string OutputPanelName = "LCD Panel Detected Output";

		bool sendLightsOn = false;
		bool detectedEnemy;


		// Handling of Messages


		/// <summary>
		/// Put your code for sending messages here.
		/// </summary>
		void sendMessage()
		{
			// this is an example of how to send a message
			if (sendLightsOn) // need to use a bool or we will keep sending the message
				if (sendOneMessage("Platform B", "Programmable block", "Lights : On"))
					sendLightsOn = false;
		}

		/// <summary>
		/// This functions is called when receiveMessage is executed; put all your message receiving code here.
		/// </summary>
		/// <param name="senderGrid">The grid where the message originated</param>
		/// <param name="senderBlock">The block where the message originated</param>
		/// <param name="message">the content of the message</param>
		void parseReceivedMessage(string senderGrid, string senderBlock, string message)
		{
			// this is an example of how to receive a message
			if (message == "Lights : On")
			{
				List<IMyTerminalBlock> lightBlocks = new List<IMyTerminalBlock>();
				GridTerminalSystem.GetBlocksOfType<IMyInteriorLight>(lightBlocks);
				for (int index = 0; index < lightBlocks.Count; index++)
					lightBlocks[index].ApplyAction("OnOff_On");
			}
		}


		// Handling of Detected Grids


		/// <summary>
		/// Called before any detected grids are retrieved.
		/// </summary>
		void beforeHandleDetected()
		{
			detectedEnemy = false; // reset every run or will alarm will only sound once
		}

		/// <summary>
		/// This function is called for every detected grid; put all your detected grid code here.
		/// </summary>
		/// <param name="grid">information about a grid</param>
		void handle_detectedGrid(Detected grid)
		{
			if (!detectedEnemy // have not found an enemy this run
				&& grid.distance < 10000 // closer than 10km
				&& grid.volume > 100 // larger than 100m³
				&& grid.seconds < 60 // seen in the past minute
				&& (grid.relations == "Enemy")) // grid is enemy
				{
					detectedEnemy = true;

					// sound an alarm
					List<IMyTerminalBlock> alarmBlocks = new List<IMyTerminalBlock>();
					GridTerminalSystem.GetBlocksOfType<IMySoundBlock>(alarmBlocks); // this gets every sound block, it could be improved by checking block names
					for (int b = 0; b < alarmBlocks.Count; b++)
						alarmBlocks[b].ApplyAction("PlaySound");
				}

			if (grid.relations == "Enemy" || grid.relations == "Neutral")
				addToOutput(grid); // this will add a grid to all the output panels
		}


		// Do not edit past this line. Browsing is encouraged, however.


		IMyTerminalBlock ThisBlock;
		List<IMyTerminalBlock> outputPanels;
		StringBuilder outputText;

		const string startOfSend = "[.[", endOfSend = "].]", startOfReceive = "<.<", endOfReceive = ">.>"; // has to match MessageParser.cs
		const char separator = ':'; // has to match MessageParser.cs and TextPanel.cs
		const string fetchFromTextPanel = "Fetch Detected from Text Panel"; // has to match TextPanel.cs
		const string blockName_fromProgram = "from Program"; // has to match TextPanel.cs

		readonly string[] newLine = { "\n", "\r\n" };
		const string tab = "    ";

		/// <summary>
		/// Autopilot will run Programmable block when writing a message.
		/// </summary>
		void Main()
		{
			if (ThisBlock == null)
				fillThisBlock();
			fillOutputPanel();

			beforeHandleDetected();
			outputText = new StringBuilder('[' + blockName_fromProgram);

			receiveMessage();
			sendMessage();
			writeOutput();
		}

		/// <summary>
		/// Determines whether or not a block is this block.
		/// </summary>
		/// <param name="block">block to test</param>
		/// <returns>true iff block is this block</returns>
		bool collect_ThisBlock(IMyTerminalBlock block)
		{ return block.DisplayNameText.Contains(ThisBlockName); }

		/// <summary>
		/// fills ThisBlock with first Programmable block found that matches collect_ThisBlock
		/// </summary>
		void fillThisBlock()
		{
			List<IMyTerminalBlock> progBlocks = new List<IMyTerminalBlock>();
			GridTerminalSystem.GetBlocksOfType<IMyProgrammableBlock>(progBlocks, collect_ThisBlock);
			ThisBlock = progBlocks[0];
		}

		/// <summary>
		/// Determines whether or not a block will be used as an output panel.
		/// </summary>
		/// <param name="block">the IMyTextPanel to test</param>
		/// <returns>true iff the block should be used as an output panel</returns>
		bool collect_outputPanel(IMyTerminalBlock block)
		{ return block.DisplayNameText == OutputPanelName; }

		/// <summary>
		/// fills outputPanels with IMyTextPanel that match collect_outputPanel
		/// </summary>
		void fillOutputPanel()
		{
			outputPanels = new List<IMyTerminalBlock>();
			GridTerminalSystem.GetBlocksOfType<IMyTextPanel>(outputPanels, collect_outputPanel);
		}

		/// <summary>
		/// test if the block's name can be written to i.e. there are no messages waiting there
		/// </summary>
		/// <returns>true iff the block's name can be written to</returns>
		bool canWrite()
		{
			if (ThisBlock == null)
				return false;
			return !(ThisBlock.DisplayNameText.Contains(startOfSend) || ThisBlock.DisplayNameText.Contains(endOfSend)
				|| ThisBlock.DisplayNameText.Contains(startOfReceive) || ThisBlock.DisplayNameText.Contains(endOfReceive));
		}

		/// <summary>
		/// Sends a message to all programmable blocks that match recipientBlock on a grid that matches recipientGrid.
		/// Matching is case insensitive and ignores spaces.
		/// </summary>
		/// <param name="recipientGrid">The grid where the destination programmable block resides</param>
		/// <param name="recipientBlock">The programmable block to send to</param>
		/// <param name="message">the content of the message</param>
		/// <returns>true iff the message was successfully sent</returns>
		bool sendOneMessage(string recipientGrid, string recipientBlock, string message)
		{
			if (ThisBlock == null)
				return false;
			if (!canWrite())
				return false; // cannot send right now 

			ThisBlock.SetCustomName(ThisBlock.CustomName + startOfSend + recipientGrid + separator + recipientBlock + separator + message + endOfSend);
			return true; // successfully sent the message 
		}

		/// <summary>
		/// Gets a message from ThisBlock's name and calls parseReceivedMessage().
		/// </summary>
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

			if (message == fetchFromTextPanel)
				createListDetected(received[1]);
			else
				parseReceivedMessage(received[0], received[1], message);
		}

		/// <summary>
		/// Builds detectedGrids from a text panel.
		/// </summary>
		/// <param name="textPanelName">text panel name to lookup</param>
		void createListDetected(string textPanelName)
		{
			// get panel
			IMyTextPanel panel = null;
			List<IMyTerminalBlock> textPanels = new List<IMyTerminalBlock>();
			GridTerminalSystem.GetBlocksOfType<IMyTextPanel>(textPanels);
			for (int index = 0; index < textPanels.Count; index++)
				if (textPanels[index].DisplayNameText == textPanelName)
				{
					panel = textPanels[index] as IMyTextPanel;
					break;
				}

			// assume panel has been filled, otherwise something went horribly wrong

			// get data
			string[] splitByLine = panel.GetPublicText().Split(newLine, StringSplitOptions.RemoveEmptyEntries);

			// erase panel
			panel.WritePublicText("");

			// build from data and invoke handler
			for (int l = 0; l < splitByLine.Length; l++)
				if (!string.IsNullOrWhiteSpace(splitByLine[l]))
					handle_detectedGrid(new Detected(splitByLine[l]));
		}

		/// <summary>
		/// Append a grid's Id to outputText
		/// </summary>
		/// <param name="grid">detected grid to add</param>
		void addToOutput(Detected grid)
		{
			outputText.Append(separator);
			outputText.Append(grid.entityId);
		}

		/// <summary>
		/// Write outputText to each panel name
		/// </summary>
		void writeOutput()
		{
			outputText.Append(']');
			for (int p = 0; p < outputPanels.Count; p++)
			{
				IMyTextPanel panel = outputPanels[p] as IMyTextPanel;
				//panel.WritePublicText(outputString);
				panel.SetCustomName(panel.DisplayNameText + outputText);
			}
		}

		/// <summary>
		/// Represents a detected grid
		/// </summary>
		class Detected
		{
			public long entityId;
			public string relations;
			public string name;
			public bool hasRadar;
			public long distance;
			public int seconds;
			public float volume = -1;

			public Detected(string line)
			{
				string[] split = line.Split(separator);

				long.TryParse(split[0], out entityId);
				relations = split[1];
				name = split[2];
				bool.TryParse(split[3], out hasRadar);
				long.TryParse(split[4], out distance);
				int.TryParse(split[5], out seconds);
				float.TryParse(split[6], out volume);
			}
		}



		// end of Programmable block code
	}
}