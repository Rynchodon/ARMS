using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;

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
		private readonly List<TextMonitor> m_monitors = new List<TextMonitor>();

		private bool m_displayNameDirty = true;
		private string m_displayName;
		private string m_instructions;

		protected readonly IMyCubeBlock m_block;

		/// <summary>Instructions that will be used iff there are none in the name.</summary>
		protected string FallBackInstruct;

		protected BlockInstructions(IMyCubeBlock block)
		{
			m_logger = new Logger("BlockInstructions", block as IMyCubeBlock);
			m_block = block;

			IMyTerminalBlock term = block as IMyTerminalBlock;
			if (term == null)
				throw new NullReferenceException("block is not an IMyTerminalBlock");
			
			term.CustomNameChanged += BlockChange;
		}

		/// <summary>
		/// Update instructions if they have changed.
		/// </summary>
		protected void UpdateInstructions()
		{
			foreach (TextMonitor monitor in m_monitors)
				if (monitor.Changed())
				{
					m_logger.debugLog("Monitor value changed", "Update()");
					m_displayNameDirty = false;
					m_displayName = m_block.DisplayNameText;
					GetInstructions();
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
			m_logger.debugLog("name changed to " + m_block.DisplayNameText, "Update()");
			m_displayName = m_block.DisplayNameText;
			GetInstructions();
		}

		/// <summary>
		/// Parse all the instructions. If false, BlockInstructions may find another set and invoke ParseAll() again.
		/// </summary>
		/// <param name="instructions">The instructions to be parsed, without brackets, with case and spacing.</param>
		/// <returns>True iff the instructions could be parsed.</returns>
		protected abstract bool ParseAll(string instructions);

		/// <summary>
		/// Update instructions when function result changes. Monitors are all removed before ParseAll() is invoked.
		/// </summary>
		protected void AddMonitor(Func<string> funcString)
		{
			m_monitors.Add(new TextMonitor(funcString));
		}

		private void BlockChange(IMyTerminalBlock obj)
		{
			m_displayNameDirty = true;
		}

		/// <summary>
		/// Gets instructions and feeds them to the parser. Stops if parser is happy with an instruction set.
		/// </summary>
		private void GetInstructions()
		{
			m_instructions = null;
			m_monitors.Clear();

			m_logger.debugLog("Trying to get instructions from name: " + m_displayName, "GetInstructions()", Logger.severity.DEBUG);
			if (GetInstructions(m_displayName))
			{
				m_logger.debugLog("Got instructions from name", "GetInstructions()", Logger.severity.DEBUG);
				return;
			}

			Ingame.IMyTextPanel asPanel = m_block as Ingame.IMyTextPanel;
			if (asPanel != null)
			{
				m_logger.debugLog("Trying to get instructions from public title: " + asPanel.GetPublicTitle(), "GetInstructions()");
				AddMonitor(asPanel.GetPublicTitle);
				if (GetInstructions(asPanel.GetPublicTitle()))
				{
					m_logger.debugLog("Got instructions from public title", "GetInstructions()", Logger.severity.DEBUG);
					return;
				}
				m_logger.debugLog("Trying to get instructions from private title: " + asPanel.GetPrivateTitle(), "GetInstructions()");
				AddMonitor(asPanel.GetPrivateTitle);
				if (GetInstructions(asPanel.GetPrivateTitle()))
				{
					m_logger.debugLog("Got instructions from private title", "GetInstructions()", Logger.severity.DEBUG);
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
			ParseAll(string.Empty);
		}

		private bool GetInstructions(string source)
		{
			MatchCollection matches = InstructionSets.Matches(source);

			for (int i = 0; i < matches.Count; i++)
			{
				string instruct = matches[i].Value.Substring(1, matches[i].Value.Length - 2);
				if (ParseAll(instruct))
				{
					m_logger.debugLog("instructions: " + instruct, "GetInstrucions()");
					m_instructions = instruct;
					return true;
				}
			}

			return false;
		}

	}
}
