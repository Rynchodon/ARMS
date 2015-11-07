using System;
using System.Text.RegularExpressions;
using Sandbox.ModAPI;

namespace Rynchodon
{
	public class BlockInstructions
	{

		private static readonly Regex InstructionSets = new Regex(@"\[.*?\]");

		private readonly Logger m_logger;
		private readonly IMyTerminalBlock m_block;
		private readonly Func<string, bool> m_onInstruction;

		private string m_displayName;
		private bool m_displayNameDirty = true;
		private string m_instructions;

		/// <summary>Instructions that will be used iff there are none in the name.</summary>
		public string FallBackInstruct;

		public BlockInstructions(IMyTerminalBlock block, Func<string, bool> onInstruction)
		{
			m_logger = new Logger("BlockInstructions", block as IMyCubeBlock);
			m_block = block;
			m_onInstruction = onInstruction;

			m_block.CustomNameChanged += BlockChange;
		}

		private void BlockChange(IMyTerminalBlock obj)
		{
			m_displayNameDirty = true;
		}

		/// <summary>
		/// Update instructions if they have changed.
		/// </summary>
		/// <returns>True iff instructions were updated.</returns>
		public bool Update()
		{
			if (!m_displayNameDirty)
				return false;
			m_displayNameDirty = false;

			if (m_displayName == m_block.DisplayNameText)
			{
				m_logger.debugLog("no name change", "Update()");
				return false;
			}
			m_logger.debugLog("name changed", "Update()");
			m_displayName = m_block.DisplayNameText;
			GetInstrucions();
			return true;
		}

		/// <summary>
		/// Invokes OnInstructions with current instructions.
		/// </summary>
		public void RunOnInstructions()
		{
			m_onInstruction(m_instructions);
		}

		private void GetInstrucions()
		{
			m_instructions = null;
			MatchCollection matches = InstructionSets.Matches(m_displayName);
			for (int i = 0; i < matches.Count; i++)
			{
				string instruct = matches[i].Value.Substring(1, matches[i].Value.Length - 2);
				if (m_onInstruction(instruct))
				{
					m_logger.debugLog("instructions: " + instruct, "GetInstrucions()");
					m_instructions = instruct;
					return;
				}
			}
			if (FallBackInstruct != null)
			{
				if (FallBackInstruct.StartsWith("["))
					FallBackInstruct = FallBackInstruct.Substring(1, FallBackInstruct.Length - 2);
				if (m_onInstruction(FallBackInstruct))
				{
					m_logger.debugLog("Setting instructions to fallback: " + FallBackInstruct, "GetInstrucions()");
					m_instructions = FallBackInstruct;
				}
			}
		}

	}
}
