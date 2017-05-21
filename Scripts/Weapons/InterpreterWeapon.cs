using System;
using System.Collections.Generic;
using Rynchodon.Attached;
using Rynchodon.Instructions;
using Rynchodon.Utility;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace Rynchodon.Weapons
{
	/// <summary>
	/// Interpreter for weapons
	/// </summary>
	public class InterpreterWeapon : BlockInstructions
	{
		private IMyCubeBlock Block;
		private IMyCubeGrid Grid;

		private int CurrentIndex;
		private bool InstructFound;

		public TargetingOptions Options;
		public List<string> Errors = new List<string>();

		private Logable Log
		{ get { return new Logable(Block); } }

		public InterpreterWeapon(IMyCubeBlock block)
			: base(block)
		{
			this.Block = block;
			this.Grid = block.CubeGrid;
		}

		/// <summary>
		/// Updates instructions if necessary.
		/// </summary>
		public void UpdateInstruction()
		{
			base.UpdateInstructions(null);
			if (!HasInstructions)
				Options = new TargetingOptions();
		}

		protected override bool ParseAll(string instructions)
		{
			CurrentIndex = -1;
			InstructFound = false;
			Options = new TargetingOptions();
			Errors.Clear();

			Parse(instructions);

			Log.TraceLog("leaving, instruct found: " + InstructFound + ", error count: " + Errors.Count);
			return InstructFound || Errors.Count == 0;
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
				Log.TraceLog("no instructions");
				return;
			}

			if (CurrentIndex >= 1000)
			{
				Log.DebugLog("Instruction limit", Logger.severity.WARNING);
				Errors.Add("limit");
				return;
			}

			string[] splitInstructions = instructions.RemoveWhitespace().ToLower().Split(new char[] { ';', ':' }, StringSplitOptions.RemoveEmptyEntries);

			if (splitInstructions.Length == 0)
			{
				Log.DebugLog("No instructions after split: " + instructions, Logger.severity.WARNING);
				return;
			}

			foreach (string instruct in splitInstructions)
			{
				Log.TraceLog("instruct = " + instruct);
				CurrentIndex++;
				if (instruct.StartsWith("(") && instruct.EndsWith(")"))
				{
					string blockList = instruct.Substring(1, instruct.Length - 2);
					ParseBlockList(blockList);
					Log.TraceLog("good instruct: " + instruct);
					InstructFound = true;
				}
				else
					if (ParseTargetType(instruct)
						|| ParseTargetFlag(instruct)
						|| ParseRange(instruct)
						|| ParseEntityId(instruct)
						|| GetFromPanel(instruct))
					{
						Log.TraceLog("good instruct: " + instruct);
						InstructFound = true;
					}
					else
					{
						Log.DebugLog("failed to parse: " + instruct, Logger.severity.WARNING);
						Errors.Add(instruct);
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
			string[] splitList = blockList.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
			Options.blocksToTarget = splitList;
			return;
		}

		/// <summary>
		/// If toParse can be parsed to TargetType, add that type to Options.
		/// </summary>
		/// <param name="toParse">string to parse</param>
		/// <returns>true iff parse succeeded</returns>
		private bool ParseTargetType(string toParse)
		{
			float f;
			if (float.TryParse(toParse, out f))
				return false;

			TargetType result;
			if (Enum.TryParse<TargetType>(toParse, true, out result))
			{
				Log.TraceLog("Adding target type: " + toParse + "/" + result);
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
			float f;
			if (float.TryParse(toParse, out f))
				return false;

			TargetingFlags result;
			if (Enum.TryParse<TargetingFlags>(toParse, true, out result))
			{
				Log.TraceLog("Adding target flag: " + toParse);
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
			//Log.DebugLog("in " + toParse + " index of " + word_radius + " is " + index, "ParseRadius()");
			if (index == 0)
				rangeString = toParse.Remove(index, word_radius.Length);
			else
			{
				index = toParse.IndexOf(word_range);
				//Log.DebugLog("in " + toParse + " index of " + word_range + " is " + index, "ParseRadius()");
				if (index == 0)
					rangeString = toParse.Remove(index, word_range.Length);
				else
					return false;
			}

			int range;
			if (int.TryParse(rangeString, out range))
			{
				Log.TraceLog("setting TargetingRange to " + range + " from " + rangeString);
				Options.TargetingRange = range;
				return true;
			}
			Log.DebugLog("failed to parse:" + rangeString, Logger.severity.WARNING);
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
				//Log.DebugLog("not starts with t: " + toParse, "GetFromPanel()");
				return false;
			}

			toParse = toParse.Substring(1);

			string[] split = toParse.Split(',');

			string panelName;
			if (split.Length == 2)
				panelName = split[0];
			else
				panelName = toParse;

			IMyTextPanel panel;
			if (!GetTextPanel(panelName, out panel))
			{
				Log.TraceLog("Panel not found: " + panelName);
				return false;
			}

			Log.TraceLog("Found panel: " + panel.DisplayNameText);

			string panelText = panel.GetPublicText();
			AddMonitor(panel.GetPublicText);

			if (string.IsNullOrWhiteSpace(panelText))
			{
				Log.TraceLog("Panel has no text: " + panel.DisplayNameText);
				return true;
			}

			string lowerText = panelText.ToLower();

			string identifier;
			int identifierIndex, startOfCommands;

			if (split.Length == 2)
			{
				identifier = split[1];
				identifierIndex = lowerText.IndexOf(identifier);
				if (identifierIndex < 0)
				{
					Log.TraceLog("could not find " + identifier + " in text of " + panel.DisplayNameText, Logger.severity.DEBUG);
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
				Log.TraceLog("could not find start of commands following " + identifier + " in text of " + panel.DisplayNameText, Logger.severity.DEBUG);
				return false;
			}

			int endOfCommands = panelText.IndexOf(']', startOfCommands + 1);
			if (endOfCommands < 0)
			{
				Log.TraceLog("could not find end of commands following " + identifier + " in text of " + panel.DisplayNameText, Logger.severity.DEBUG);
				return false;
			}

			//Log.DebugLog("fetching commands from panel: " + bestMatch.DisplayNameText, "addAction_textPanel()", Logger.severity.TRACE);
			Parse(panelText.Substring(startOfCommands, endOfCommands - startOfCommands));
			return true; // this instruction was successfully executed, even if sub instructions were not
		}

		private bool GetTextPanel(string name, out IMyTextPanel panel)
		{
			IMyCubeBlock foundBlock = null;
			int bestNameLength = int.MaxValue;

			foreach (IMyCubeBlock Fatblock in AttachedGrid.AttachedCubeBlocks(Grid, AttachedGrid.AttachmentKind.Permanent, true))
				if (Block.canControlBlock(Fatblock))
				{
					string blockName = Fatblock.DisplayNameText.LowerRemoveWhitespace();

					if (blockName.Length < bestNameLength && blockName.Contains(name))
					{
						foundBlock = Fatblock;
						bestNameLength = blockName.Length;
						if (name.Length == bestNameLength)
							break;
					}
				}

			panel = foundBlock as IMyTextPanel;
			return panel != null;
		}

	}
}
