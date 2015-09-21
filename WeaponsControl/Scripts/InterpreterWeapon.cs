#define LOG_ENABLED // remove on build

using System;
using System.Collections.Generic;
using Rynchodon.Settings;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Weapons
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

		public static bool allowedNPC { get; private set; }
		private static TargetingOptions ForNPC_Options = null;
		private static List<string> ForNPC_Errors = null;

		static InterpreterWeapon()
		{ allowedNPC = true; }

		private InterpreterWeapon()
		{ myLogger = new Logger("InterpreterWeapon", null, () => { return "For NPC"; }); }

		public InterpreterWeapon(IMyCubeBlock block)
		{
			this.Block = block;
			this.Grid = block.CubeGrid;

			myLogger = new Logger("InterpreterWeapon", () => Grid.DisplayName, () => Block.DefinitionDisplayNameText, () => Block.getNameOnly());
		}

		/// <summary>
		/// Parse intstructions for a turret or engaging Autopilot.
		/// </summary>
		/// <param name="instructions">string to parse</param>
		/// <param name="Grid">where to search for text panels</param>
		/// <param name="Options">results of parse</param>
		/// <param name="Errors">indices of parsing errors</param>
		public void Parse(out TargetingOptions Options, out List<string> Errors, string instructions = null)
		{
			if (Block != null && Block.OwnedNPC())
			{
				myLogger.debugLog(Block.DisplayNameText + " is owned by a N.P.C.", "Parse()");
				if (ForNPC_Options == null)
				{
					//myLogger.debugLog("ForNPC_Options = " + ForNPC_Options, "Parse()");
					InterpreterWeapon ForNPC_IW = new InterpreterWeapon();
					//myLogger.debugLog("ForNPC_IW = " + ForNPC_IW, "Parse()");
					instructions = ServerSettings.GetSettingString(ServerSettings.SettingName.sWeaponCommandsNPC);
					myLogger.debugLog("instructions = " + instructions, "Parse()");

					if (instructions != null)
						instructions = instructions.getInstructions();

					if (string.IsNullOrWhiteSpace(instructions))
					{
						myLogger.debugLog("No settings for N.P.C. turrets", "Parse()", Logger.severity.INFO);
						allowedNPC = false;
						ForNPC_Options = new TargetingOptions();
						ForNPC_Errors = new List<string>();
					}
					else
					{
						ForNPC_IW.Parse(out ForNPC_Options, out ForNPC_Errors, instructions);
						myLogger.debugLog("Parsed NPC OK, Options: " + ForNPC_Options, "Parse()");
					}
				}
				Options = ForNPC_Options.Clone();
				Errors = ForNPC_Errors;
				return;
			}

			if (instructions == null)
			{
				if (Block == null)
					throw new NullReferenceException("Block");
				instructions = Block.DisplayNameText.getInstructions();
			}

			Options = new TargetingOptions();
			Errors = new List<string>();

			this.Options = Options;
			this.Errors = Errors;
			this.CurrentIndex = -1;

			Parse(instructions);

			//myLogger.debugLog("CanTarget = " + Options.CanTarget, "Parse()");
		}

		/// <summary>
		/// Parse intructions for a turret or engaging Autopilot.
		/// </summary>
		/// <param name="instructions">string to parse</param>
		/// <param name="Options">results of parse</param>
		/// <param name="Errors">indices of parsing errors</param>
		private void Parse(string instructions)
		{
			if (instructions == null)
			{
				myLogger.debugLog("no instructions", "Parse()");
				return;
			}

			if (CurrentIndex >= 1000)
			{
				myLogger.debugLog("Instruction limit", "Parse()", Logger.severity.WARNING);
				Errors.Add("limit");
				return;
			}

			string[] splitInstructions = instructions.RemoveWhitespace().ToLower().Split(new char[] { ';', ':' }, StringSplitOptions.RemoveEmptyEntries);

			if (splitInstructions.Length == 0)
			{
				myLogger.debugLog("No instructions after split: " + instructions, "Parse()", Logger.severity.WARNING);
				return;
			}

			foreach (string instruct in splitInstructions)
			{
				//myLogger.debugLog("instruct = " + instruct, "Parse()");
				CurrentIndex++;
				if (instruct.StartsWith("(") && instruct.EndsWith(")"))
				{
					string blockList = instruct.Substring(1, instruct.Length - 2);
					ParseBlockList(blockList);
				}
				else
					if (!(ParseTargetType(instruct)
						|| ParseTargetFlag(instruct)
						|| ParseRange(instruct)
						|| GetFromPanel(instruct)))
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
		private void ParseBlockList(string blockList)
		{
			string[] splitList = blockList.Split(new char[]{','}, StringSplitOptions.RemoveEmptyEntries);
			foreach (string block in splitList)
				Options.blocksToTarget.Add(block);

			return;
		}

		/// <summary>
		/// If toParse can be parsed to TargetType, add that type to Options.
		/// </summary>
		/// <param name="toParse">string to parse</param>
		/// <returns>true iff parse succeeded</returns>
		private bool ParseTargetType(string toParse)
		{
			TargetType result;
			if (Enum.TryParse<TargetType>(toParse, true, out result))
			{
				//myLogger.debugLog("Adding target type: " + toParse, "ParseTargetType()");
				Options.CanTarget |= result;
				return true;
			}
			return false;
		}

		/// <summary>
		/// If toParse can be parsed to TargetingFlags, add that flag to Options.
		/// </summary>
		/// <param name="toParse">string to parse</param>
		/// <returns>true iff parse succeeded</returns>
		private bool ParseTargetFlag(string toParse)
		{
			TargetingFlags result;
			if (Enum.TryParse<TargetingFlags>(toParse, true, out result))
			{
				Options.Flags |= result;
				return true;
			}
			return false;
		}

		/// <summary>
		/// Tries to get radius/range from toParse
		/// </summary>
		/// <param name="toParse">string to parse</param>
		/// <returns>true iff parse succeeded</returns>
		private bool ParseRange(string toParse)
		{
			const string word_radius = "radius", word_range = "range";
			string rangeString;

			int index = toParse.IndexOf(word_radius);
			//myLogger.debugLog("in " + toParse + " index of " + word_radius + " is " + index, "ParseRadius()");
			if (index == 0)
				rangeString = toParse.Remove(index, word_radius.Length);
			else
			{
				index = toParse.IndexOf(word_range);
				//myLogger.debugLog("in " + toParse + " index of " + word_range + " is " + index, "ParseRadius()");
				if (index == 0)
					rangeString = toParse.Remove(index, word_range.Length);
				else
					return false;
			}

			int range;
			if (int.TryParse(rangeString, out range))
			{
				//myLogger.debugLog("setting TargetingRange to " + range + " from " + rangeString, "ParseRadius()");
				Options.TargetingRange = range;
				return true;
			}
			myLogger.debugLog("failed to parse:" + rangeString, "ParseRadius()", Logger.severity.WARNING);
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
			if (Block == null)
				throw new NullReferenceException("Block");
			if (Grid == null)
				throw new NullReferenceException("Grid");

			if (!toParse.StartsWith("t"))
			{
				//myLogger.debugLog("does not start with t: " + toParse, "GetFromPanel()");
				return false;
			}
			//myLogger.debugLog("Trying to parse: " + toParse, "GetFromPanel()");

			toParse = toParse.Substring(1);

			string[] split = toParse.Split(',');

			string panelName;
			if (split.Length == 2)
				panelName = split[0];
			else
				panelName = toParse;

			var TextPanels = CubeGridCache.GetFor(Grid).GetBlocksOfType(typeof(MyObjectBuilder_TextPanel));

			if (TextPanels == null)
			{
				myLogger.debugLog("no text panels", "GetFromPanel()");
				return false;
			}
			myLogger.debugLog(TextPanels.Count+" text panels", "GetFromPanel()");

			IMyCubeBlock bestMatch = null;
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

			//myLogger.debugLog("Found panel: " + bestMatch.DisplayNameText, "GetFromPanel()");

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

			//myLogger.debugLog("fetching commands from panel: " + bestMatch.DisplayNameText, "addAction_textPanel()", Logger.severity.TRACE);
			Parse(panelText.Substring(startOfCommands, endOfCommands - startOfCommands));

			return true; // this instruction was successfully executed, even if sub instructions were not
		}
	}
}
