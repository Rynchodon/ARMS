using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Sandbox.ModAPI;

namespace Rynchodon.Instructions
{
	public abstract class BlockInstructions
	{

		private class TextMonitor
		{

			private readonly Func<string> m_stringFunc;
			private readonly string m_lastString;

			public TextMonitor(Func<string> stringFunc)
			{
				m_stringFunc = stringFunc;
				m_lastString = m_stringFunc.Invoke();
			}

			public bool Changed()
			{
				return m_lastString != m_stringFunc.Invoke();
			}

		}

		private static readonly Regex InstructionSets = new Regex(@"\[.*?\]");

		private readonly Logger m_logger;
		private readonly IMyCubeBlock m_block;
		private readonly List<TextMonitor> m_monitors = new List<TextMonitor>();

		private bool m_displayNameDirty = true;
		private string m_displayName;
		private string m_instructions;

		/// <summary>Instructions that will be used iff there are none in the name.</summary>
		protected string FallBackInstruct;

		protected BlockInstructions(IMyTerminalBlock block)
		{
			m_logger = new Logger("BlockInstructions", block as IMyCubeBlock);
			m_block = block as IMyCubeBlock;

			block.CustomNameChanged += BlockChange;
		}

		/// <summary>
		/// Update instructions if they have changed.
		/// </summary>
		protected void Update()
		{
			foreach (TextMonitor monitor in m_monitors)
				if (monitor.Changed())
				{
					m_logger.debugLog("Monitor value changed", "Update()");
					m_displayNameDirty = false;
					m_displayName = m_block.DisplayNameText;
					GetInstrucions();
					return;
				}

			if (!m_displayNameDirty)
				return;
			m_displayNameDirty = false;

			if (m_displayName == m_block.DisplayNameText)
			{
				m_logger.debugLog("no name change", "Update()");
				return;
			}
			m_logger.debugLog("name changed", "Update()");
			m_displayName = m_block.DisplayNameText;
			GetInstrucions();
		}

		protected abstract bool ParseAll(string instructions);

		protected void AddMonitor(Func<string> funcString)
		{
			m_monitors.Add(new TextMonitor(funcString));
		}

		private void BlockChange(IMyTerminalBlock obj)
		{
			m_displayNameDirty = true;
		}

		/// <summary>
		/// Gets instructions and feeds them to the parser.
		/// </summary>
		/// <returns>True iff the parser is happy.</returns>
		private void GetInstrucions()
		{
			m_instructions = null;
			m_monitors.Clear();
			MatchCollection matches = InstructionSets.Matches(m_displayName);
			for (int i = 0; i < matches.Count; i++)
			{
				string instruct = matches[i].Value.Substring(1, matches[i].Value.Length - 2);
				if (ParseAll(instruct))
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
				if (ParseAll(FallBackInstruct))
				{
					m_logger.debugLog("Setting instructions to fallback: " + FallBackInstruct, "GetInstrucions()");
					m_instructions = FallBackInstruct;
					return;
				}
			}
		}

	}
}
