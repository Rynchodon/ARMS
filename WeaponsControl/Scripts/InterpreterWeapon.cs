#define LOG_ENABLED // remove on build

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.ModAPI;
using Sandbox.Common.ObjectBuilders;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Autopilot.Weapons
{
	/// <summary>
	/// Interpreter for weapons
	/// </summary>
	public class InterpreterWeapon
	{
		private Logger myLogger;

		private IMyCubeBlock Block;
		private IMyCubeGrid Grid;

		private TargetingOptions Options;
		private List<string> Errors;
		private int CurrentIndex;

		/// <summary>
		/// Parse intstructions for a turret or engaging Autopilot.
		/// </summary>
		/// <param name="instructions">string to parse</param>
		/// <param name="Grid">where to search for text panels</param>
		/// <param name="Options">results of parse</param>
		/// <param name="Errors">indices of parsing errors</param>
		public void Parse(IMyCubeBlock forBlock, out TargetingOptions Options, out List<string> Errors, string instructions = null)
		{
			if (instructions == null)
				instructions = forBlock.DisplayNameText.getInstructions();

			Options = new TargetingOptions();
			Errors = new List<string>();

			this.Block = forBlock;
			this.Grid = forBlock.CubeGrid;

			this.Options = Options;
			this.Errors = Errors;
			this.CurrentIndex = -1;

			myLogger = new Logger("InterpreterWeapon", () => Grid.DisplayName, () => Block.DefinitionDisplayNameText, () => Block.getNameOnly());

			Parse(instructions);
		}

		/// <summary>
		/// Parse intructions for a turret or engaging Autopilot.
		/// </summary>
		/// <param name="instructions">string to parse</param>
		/// <param name="Options">results of parse</param>
		/// <param name="Errors">indices of parsing errors</param>
		private void Parse(string instructions)
		{
			if (CurrentIndex >= 1000)
			{
				myLogger.debugLog("Instruction limit", "Parse()", Logger.severity.WARNING);
				Errors.Add("limit");
				return;
			}

			string[] splitInstructions = instructions.RemoveWhitespace().ToLower().Split(new char[] { ';', ':' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (string instruct in splitInstructions)
			{
				CurrentIndex++;
				if (instruct.Contains('('))
				{
					if (!ParseBlockList(instruct))
					{
						myLogger.debugLog("failed to parse: " + instruct, "Parse()", Logger.severity.WARNING);
						Errors.Add(CurrentIndex.ToString());
					}
				}
				else
					if (!ParseTargetType(instruct))
						if (!GetFromPanel(instruct))
						{
							myLogger.debugLog("failed to parse: " + instruct, "Parse()", Logger.severity.WARNING);
							Errors.Add(CurrentIndex.ToString());
						}
			}
		}

		/// <summary>
		/// Add blocks to Options.blocksToTarget from blockList.
		/// </summary>
		/// <param name="blockList">string to parse for blocks</param>
		/// <param name="Options">to add blocks to</param>
		private bool ParseBlockList(string blockList)
		{
			if (blockList.StartsWith("(") && blockList.EndsWith(")"))
				blockList = blockList.Substring(1, blockList.Length - 2);
			else
				return false;

			string[] splitList = blockList.Split(new char[]{','}, StringSplitOptions.RemoveEmptyEntries);
			foreach (string block in splitList)
				Options.blocksToTarget.Add(block);

			return true;
		}

		/// <summary>
		/// If toParse can be parsed to TargetType, add that type to Options.
		/// </summary>
		/// <param name="toParse">string to parse</param>
		/// <param name="Options">TargetType will be added to</param>
		/// <returns>true iff parse succeeded</returns>
		private bool ParseTargetType(string toParse)
		{
			TargetType result;
			if (Enum.TryParse<TargetType>(toParse, out result))
			{
				Options.CanTarget |= result;
				return true;
			}
			return false;
		}

		/// <summary>
		/// Fetch instructions from a text panel.
		/// </summary>
		/// <param name="toParse">'t' + (name of text panel)</param>
		/// <param name="Options">to add instructions to</param>
		/// <returns>true iff a text panel was found</returns>
		private bool GetFromPanel(string toParse)
		{
			if (!toParse.StartsWith("t"))
			{
				myLogger.debugLog("does not start with t: " + toParse, "GetFromPanel()");
				return false;
			}
			myLogger.debugLog("Trying to parse: " + toParse, "GetFromPanel()");

			string[] split = toParse.Split(',');

			string panelName;
			if (split.Length == 2)
				panelName = split[0];
			else
				panelName = toParse;

			var TextPanels = CubeGridCache.GetFor(Grid).GetBlocksOfType(typeof(MyObjectBuilder_TextPanel));
			Ingame.IMyTerminalBlock bestMatch = null;
			int bestMatchLength = int.MaxValue;
			foreach (var panel in TextPanels)
				if (panel.DisplayNameText.Length < bestMatchLength && Block.canControlBlock(panel as IMyCubeBlock) && panel.DisplayNameText.looseContains(panelName))
				{
					bestMatch = panel;
					bestMatchLength = panel.DisplayNameText.Length;
				}

			if (bestMatch == null)
			{
				myLogger.debugLog("could not find " + panelName + " on " + Grid.DisplayName, "GetFromPanel()", Logger.severity.DEBUG);
				return false;
			}

			myLogger.debugLog("Found panel: " + bestMatch.DisplayNameText, "GetFromPanel()");

			string panelText = (bestMatch as Ingame.IMyTextPanel).GetPublicText();
			string lowerText = panelText.ToLower();

			string identifier;
			int identifierIndex, startOfCommands;

			if (split.Length == 2)
			{
				identifier = split[1];
				identifierIndex = lowerText.IndexOf(identifier);
				if (identifierIndex < 0)
				{
					myLogger.debugLog("could not find " + identifier + " in text of " + bestMatch.DisplayNameText, "addAction_textPanel()", Logger.severity.DEBUG);
					return false;
				}
				startOfCommands = panelText.IndexOf('[', identifierIndex + identifier.Length) + 1;
			}
			else
			{
				identifier = null;
				identifierIndex = -1;
				startOfCommands = panelText.IndexOf('[') + 1;
			}

			if (startOfCommands < 0)
			{
				myLogger.debugLog("could not find start of commands following " + identifier + " in text of " + bestMatch.DisplayNameText, "addAction_textPanel()", Logger.severity.DEBUG);
				return false;
			}

			int endOfCommands = panelText.IndexOf(']', startOfCommands + 1);
			if (endOfCommands < 0)
			{
				myLogger.debugLog("could not find end of commands following " + identifier + " in text of " + bestMatch.DisplayNameText, "addAction_textPanel()", Logger.severity.DEBUG);
				return false;
			}

			myLogger.debugLog("fetching commands from panel: " + bestMatch.DisplayNameText, "addAction_textPanel()", Logger.severity.TRACE);
			Parse(panelText.Substring(startOfCommands, endOfCommands - startOfCommands));

			return true; // this instruction was successfully executed, even if sub instructions were not
		}
	}
}
