#define LOG_ENABLED // remove on build

using System;
using System.Collections.Generic;
using Rynchodon.Attached;
using Rynchodon.Settings;
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
		private BlockInstructions m_instructions;
		private List<PanelInstructions> m_monitor = new List<PanelInstructions>();

		private int CurrentIndex;
		private bool InstructFound;

		public TargetingOptions Options;
		public List<string> Errors = new List<string>();

		private InterpreterWeapon()
		{ myLogger = new Logger("InterpreterWeapon", null, () => { return "For NPC"; }); }

		public InterpreterWeapon(IMyCubeBlock block)
		{
			this.Block = block;
			this.Grid = block.CubeGrid;
			this.m_instructions = new BlockInstructions(block as IMyTerminalBlock, OnInstruction);

			myLogger = new Logger("InterpreterWeapon", () => Grid.DisplayName, () => Block.DefinitionDisplayNameText, () => Block.getNameOnly());
		}

		/// <summary>
		/// Updates instructions if necessary.
		/// </summary>
		/// <returns>True iff instructions were updated.</returns>
		public bool UpdateInstruction()
		{
			if (Block.OwnedNPC())
			{
				if (m_instructions.FallBackInstruct == null)
					m_instructions.FallBackInstruct = ServerSettings.GetSettingString(ServerSettings.SettingName.sWeaponCommandsNPC);
			}
			else
				m_instructions.FallBackInstruct = null;

			if (m_instructions.Update())
				myLogger.debugLog("display name changed", "UpdateInstruction()", Logger.severity.INFO);
			else
				foreach (PanelInstructions panel in m_monitor)
					if (panel.PublicTextChanged())
					{
						myLogger.debugLog("Text panel changed: " + panel.Block.DisplayNameText, "UpdateInstruction()", Logger.severity.INFO);
						m_instructions.RunOnInstructions();
						break;
					}
				
			return InstructFound;
		}

		private bool OnInstruction(string instructions)
		{
			CurrentIndex = -1;
			InstructFound = false;
			Options = new TargetingOptions();
			Errors.Clear();
			m_monitor.Clear();

			Parse(instructions);

			return InstructFound;
		}

		/// <summary>
		/// Parse intructions for a weapon.
		/// </summary>
		/// <param name="instructions">string to parse</param>
		/// <param name="Options">results of parse</param>
		/// <param name="Errors">indices of parsing errors</param>
		private void Parse(string instructions)
		{
			if (string.IsNullOrWhiteSpace(instructions))
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
					InstructFound = true;
				}
				else
					if (!(ParseTargetType(instruct)
						|| ParseTargetFlag(instruct)
						|| ParseRange(instruct)
						|| ParseEntityId(instruct)
						|| GetFromPanel(instruct)))
					{
						myLogger.debugLog("failed to parse: " + instruct, "Parse()", Logger.severity.WARNING);
						Errors.Add(CurrentIndex.ToString());
					}
					else
						InstructFound = true;
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
		/// Tries to get an entity id from toParse
		/// </summary>
		private bool ParseEntityId(string toParse)
		{
			if (!toParse.StartsWith("id"))
				return false;

			long value;
			if (long.TryParse(toParse.Substring(2), out value))
			{
				Options.TargetEntityId = value;
				return true;
			}
			return false;
		}

		/// <summary>
		/// Fetch instructions from a text panel.
		/// </summary>
		/// <param name="toParse">'t' + (name of text panel)</param>
		/// <returns>true iff a text panel was found</returns>
		private bool GetFromPanel(string toParse)
		{
			if (Block == null)
				throw new NullReferenceException("Block");
			if (Grid == null)
				throw new NullReferenceException("Grid");

			if (!toParse.StartsWith("t"))
			{
				myLogger.debugLog("not starts with t: " + toParse, "GetFromPanel()");
				return false;
			}

			toParse = toParse.Substring(1);

			string[] split = toParse.Split(',');

			string panelName;
			if (split.Length == 2)
				panelName = split[0];
			else
				panelName = toParse;

			PanelInstructions panel;
			if (!GetTextPanel(panelName, out panel))
			{
				myLogger.debugLog("Panel not found: " + panelName, "GetFromPanel()");
				return false;
			}

			myLogger.debugLog("Found panel: " + panel.Block.DisplayNameText, "GetFromPanel()");

			string panelText = panel.PublicText;

			if (string.IsNullOrWhiteSpace(panelText))
			{
				myLogger.debugLog("Panel has no text: " + panel.Block.DisplayNameText, "GetFromPanel()");
				return true;
			}

			string lowerText = panel.PublicText.ToLower();

			string identifier;
			int identifierIndex, startOfCommands;

			if (split.Length == 2)
			{
				identifier = split[1];
				identifierIndex = lowerText.IndexOf(identifier);
				if (identifierIndex < 0)
				{
					myLogger.debugLog("could not find " + identifier + " in text of " + panel.Block.DisplayNameText, "addAction_textPanel()", Logger.severity.DEBUG);
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
				myLogger.debugLog("could not find start of commands following " + identifier + " in text of " + panel.Block.DisplayNameText, "addAction_textPanel()", Logger.severity.DEBUG);
				return false;
			}

			int endOfCommands = panelText.IndexOf(']', startOfCommands + 1);
			if (endOfCommands < 0)
			{
				myLogger.debugLog("could not find end of commands following " + identifier + " in text of " + panel.Block.DisplayNameText, "addAction_textPanel()", Logger.severity.DEBUG);
				return false;
			}

			//myLogger.debugLog("fetching commands from panel: " + bestMatch.DisplayNameText, "addAction_textPanel()", Logger.severity.TRACE);
			Parse(panelText.Substring(startOfCommands, endOfCommands - startOfCommands));

			return true; // this instruction was successfully executed, even if sub instructions were not
		}

		private bool GetTextPanel(string name, out PanelInstructions panel)
		{
			IMyCubeBlock foundBlock = null;
			int bestNameLength = int.MaxValue;

			AttachedGrid.RunOnAttachedBlock(Grid, AttachedGrid.AttachmentKind.Permanent, block => {
				IMyCubeBlock Fatblock = block.FatBlock;
				if (Fatblock != null && Block.canControlBlock(Fatblock))
				{
					string blockName = Fatblock.DisplayNameText.LowerRemoveWhitespace();

					if (blockName.Length < bestNameLength && blockName.Contains(name))
					{
						foundBlock = Fatblock;
						bestNameLength = blockName.Length;
						if (name.Length == bestNameLength)
							return true;
					}
				}
				return false;
			}, true);

			Ingame.IMyTextPanel textPanel = foundBlock as Ingame.IMyTextPanel;
			if (textPanel != null)
			{
				panel = new PanelInstructions(textPanel);
				m_monitor.Add(panel);
				return true;
			}
			panel = null;
			return false;
		}

	}
}
