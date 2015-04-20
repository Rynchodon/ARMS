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
		 * 
		 * To send detected grid information to a Programmable block, use the command
		 *     [ Transmit Detected to <Programmable block name> ]
		 * in the name of a TextPanel.
		 */

		/// <summary>
		/// Name of this block. 
		/// Case sensitive, must match exactly. 
		/// First matching block will be used.
		/// </summary>
		const string ThisBlockName = "Programmable block";

		/// <summary>
		/// Set to true to send the "Lights : On" message
		/// </summary>
		bool sendLightsOn = false;

		/// <summary>
		/// The name of the sound blocks that are proximity alarms. 
		/// Case sensitive, must match exactly. 
		/// Can match more than one block.
		/// </summary>
		const string ProximityAlarmName = "Proximity Alarm";

		/// <summary>
		/// Set to true to use proximity alarm.
		/// </summary>
		const bool useProximityAlarm = false;

		/// <summary>
		/// This value is set later in the program.
		/// </summary>
		bool alarmIsSounding;


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
			if (senderGrid == "Platform A" && message == "Lights : On")
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
			alarmIsSounding = false; // reset every run or alarm will only sound once
		}

		/// <summary>
		/// This function is called for every detected grid; put all your detected grid code here.
		/// </summary>
		/// <param name="grid">information about a grid</param>
		void handle_detectedGrid(Detected grid)
		{
			if (grid.relations == "Enemy")
				addToOutput("Placeholder", grid); // this will add a grid to the output panels

			// sound an alarm if an enemy is near
			if (useProximityAlarm // proximity alarm is enabled
				&& !alarmIsSounding // have not found an enemy this run

				&& grid.distance < 10000 // closer than 10km
				&& grid.volume > 100 // larger than 100m³
				&& grid.seconds < 60 // seen in the past minute
				&& (grid.relations == "Enemy")) // grid is enemy
			{
				soundProximityAlarm(); // sounds the alarm
			}
		}


		// Do not edit past this line. Browsing is encouraged, however.


		IMyTerminalBlock ThisBlock;
		Dictionary<string, StringBuilder> outputText;

		const string startOfSend = "[.[", endOfSend = "].]", startOfReceive = "<.<", endOfReceive = ">.>"; // has to match MessageParser.cs
		const char separator = ':'; // has to match MessageParser.cs and TextPanel.cs
		const string fetchFromTextPanel = "Fetch Detected from Text Panel"; // has to match TextPanel.cs
		const string blockName_fromProgram = "from Program"; // has to match TextPanel.cs

		readonly string[] newLine = { "\n", "\r", "\r\n" };
		const string tab = "    ";

		/// <summary>
		/// Autopilot will run Programmable block when writing a message.
		/// </summary>
		void Main()
		{
			if (ThisBlock == null)
				fillThisBlock();

			outputText = new Dictionary<string, StringBuilder>();

			beforeHandleDetected();
			receiveMessage();
			sendMessage();
			writeOutput();
		}

		/// <summary>
		/// Search string for collect_BlockName
		/// </summary>
		string search_collect_BlockName;

		/// <summary>
		/// collect blocks whose names match search_collect_BlockName
		/// </summary>
		/// <param name="block">block to test</param>
		/// <returns>true if the name matches</returns>
		bool collect_BlockName(IMyTerminalBlock block)
		{ return block.DisplayNameText.Contains(search_collect_BlockName); }

		/// <summary>
		/// fills ThisBlock with first Programmable block found that matches collect_ThisBlock
		/// </summary>
		void fillThisBlock()
		{
			List<IMyTerminalBlock> progBlocks = new List<IMyTerminalBlock>();
			search_collect_BlockName = ThisBlockName;
			GridTerminalSystem.GetBlocksOfType<IMyProgrammableBlock>(progBlocks, collect_BlockName);
			ThisBlock = progBlocks[0];
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
			for (int lineIndex = 0; lineIndex < splitByLine.Length; lineIndex++)
				if (!string.IsNullOrWhiteSpace(splitByLine[lineIndex]))
				{
					//string distanceString = splitByLine[lineIndex].Split(separator)[4];
					//debug("distance = "+distanceString+" => "+);
					handle_detectedGrid(new Detected(splitByLine[lineIndex]));
				}
		}

		/// <summary>
		/// Append a grid's Id to outputText
		/// </summary>
		/// <param name="whichOutput">which set of output panels to use</param>
		/// <param name="grid">detected grid to add</param>
		void addToOutput(string whichOutput, Detected grid)
		{
			StringBuilder addTo;
			if (!outputText.TryGetValue(whichOutput, out addTo))
			{
				addTo = new StringBuilder('[' + blockName_fromProgram);
				outputText.Add(whichOutput, addTo);
			}

			addTo.Append(separator);
			addTo.Append(grid.entityId);
		}

		/// <summary>
		/// if a textpanel's name is in outputText, write the detected grids.
		/// </summary>
		/// <param name="block">the IMyTextPanel to test</param>
		/// <returns>false, this is a fake collector</returns>
		bool action_outputPanel(IMyTerminalBlock block)
		{
			StringBuilder writeToPanel;
			if (outputText.TryGetValue(block.DisplayNameText, out writeToPanel))
			{
				IMyTextPanel panel = block as IMyTextPanel;
				panel.SetCustomName(panel.DisplayNameText + writeToPanel + ']');
			}

			return false; // fake collector
		}

		/// <summary>
		/// Write outputText to each panel name
		/// </summary>
		void writeOutput()
		{
			List<IMyTerminalBlock> neverFilled = new List<IMyTerminalBlock>();
			GridTerminalSystem.GetBlocksOfType<IMyTextPanel>(neverFilled, action_outputPanel);
		}

		/// <summary>
		/// Sounds the proximity alarm(s)
		/// </summary>
		void soundProximityAlarm()
		{
			if (alarmIsSounding)
				return;
			alarmIsSounding = true;

			List<IMyTerminalBlock> proximityAlarms = new List<IMyTerminalBlock>();
			search_collect_BlockName = ProximityAlarmName;
			GridTerminalSystem.GetBlocksOfType<IMySoundBlock>(proximityAlarms, collect_BlockName);
			for (int index = 0; index < proximityAlarms.Count; index++)
				proximityAlarms[index].ApplyAction("PlaySound");
		}

		#region Debugging

		//List<IMyTerminalBlock> DebugPanels;

		//void debug(string line, bool append = true)
		//{
		//	if (DebugPanels == null)
		//	{
		//		DebugPanels = new List<IMyTerminalBlock>();
		//		search_collect_BlockName = "Debug Panel for Block Communication";
		//		GridTerminalSystem.GetBlocksOfType<IMyTextPanel>(DebugPanels, collect_BlockName);
		//	}

		//	for (int index = 0; index < DebugPanels.Count; index++)
		//		((IMyTextPanel)DebugPanels[index]).WritePublicText(line + '\n', append);
		//}

		#endregion

		/// <summary>
		/// Represents a detected grid
		/// </summary>
		class Detected
		{
			public long entityId;
			public string relations;
			public string name;
			public bool hasRadar;
			public double distance;
			public int seconds;
			public float volume = -1;

			public Detected(string line)
			{
				string[] split = line.Split(separator);

				long.TryParse(split[0], out entityId);
				relations = split[1];
				name = split[2];
				bool.TryParse(split[3], out hasRadar);
				double.TryParse(split[4], out distance);
				int.TryParse(split[5], out seconds);
				float.TryParse(split[6], out volume);
			}

			public override string ToString()
			{ return name + separator + relations + separator + entityId; }
		}



		// end of Programmable block code
	}
}
