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

		public BlockInstructions(IMyTerminalBlock block, Func<string, bool> onInstruction)
		{
			m_logger = new Logger("BlockInstructions", block as IMyCubeBlock);
			m_block = block;
			m_onInstruction = onInstruction;

			m_block.CustomNameChanged += CustomNameChanged;
		}

		private void CustomNameChanged(IMyTerminalBlock obj)
		{
			m_displayNameDirty = true;
		}

		public void Update()
		{
			if (!m_displayNameDirty)
			{
				m_onInstruction(m_instructions);
				return;
			}
			m_displayNameDirty = false;

			if (m_displayName == m_block.DisplayNameText)
			{
				m_logger.debugLog("no name change", "Update()");
				m_onInstruction(m_instructions);
				return;
			}
			m_logger.debugLog("name changed", "Update()");
			m_displayName = m_block.DisplayNameText;
			GetInstrucions();
		}

		private void GetInstrucions()
		{
			Match sets = InstructionSets.Match(m_displayName);
			m_instructions = null;
			foreach (string instruct in sets.Groups)
				if (m_onInstruction(instruct))
				{
					m_logger.debugLog("instructions: " + instruct, "GetInstrucions()");
					m_instructions = instruct;
					return;
				}
			m_onInstruction(null);
		}

	}
}
