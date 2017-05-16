using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Rynchodon.Utility;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
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

		private readonly List<TextMonitor> m_monitors = new List<TextMonitor>();

		private bool m_displayNameDirty = true;
		private string m_displayName;
		private string m_instructions;

		public readonly IMyCubeBlock m_block;

		public bool HasInstructions { get; private set; }

		private Logable Log
		{ get { return new Logable(m_block); } }

		protected BlockInstructions(IMyCubeBlock block)
		{
			m_block = block;

			IMyTerminalBlock term = block as IMyTerminalBlock;
			if (term == null)
				throw new NullReferenceException("block is not an IMyTerminalBlock");
			
			term.CustomNameChanged += BlockChange;
		}

		/// <summary>
		/// Update instructions if they have changed.
		/// </summary>
		/// <return>true iff instructions were updated and block has instructions</return>
		protected bool UpdateInstructions(string fallback = null)
		{
			foreach (TextMonitor monitor in m_monitors)
				if (monitor.Changed())
				{
					Log.DebugLog("Monitor value changed");
					m_displayNameDirty = false;
					m_displayName = m_block.DisplayNameText;
					GetInstructions();
					if (!HasInstructions && fallback != null)
						GetFallbackInstructions(fallback);
					return HasInstructions;
				}

			if (!m_displayNameDirty)
				return false;
			m_displayNameDirty = false;

			if (m_displayName == m_block.DisplayNameText)
			{
				Log.DebugLog("no name change");
				return false;
			}

			Log.DebugLog("name changed to " + m_block.DisplayNameText);
			m_displayName = m_block.DisplayNameText;
			GetInstructions();
			if (!HasInstructions && fallback != null)
				GetFallbackInstructions(fallback);
			return HasInstructions;
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
			HasInstructions = false;

			//Log.DebugLog("Trying to get instructions from name: " + m_displayName, "GetInstructions()", Logger.severity.DEBUG);
			if (GetInstructions(m_displayName))
			{
				//Log.DebugLog("Got instructions from name", "GetInstructions()", Logger.severity.DEBUG);
				return;
			}

			Ingame.IMyTextPanel asPanel = m_block as Ingame.IMyTextPanel;
			if (asPanel != null)
			{
				//Log.DebugLog("Trying to get instructions from public title: " + asPanel.GetPublicTitle(), "GetInstructions()");
				AddMonitor(asPanel.GetPublicTitle);
				if (GetInstructions(asPanel.GetPublicTitle()))
				{
					//Log.DebugLog("Got instructions from public title", "GetInstructions()", Logger.severity.DEBUG);
					return;
				}
				//Log.DebugLog("Trying to get instructions from private title: " + asPanel.GetPrivateTitle(), "GetInstructions()");
#pragma warning disable CS0618
				AddMonitor(asPanel.GetPrivateTitle);
				if (GetInstructions(asPanel.GetPrivateTitle()))
#pragma warning restore CS0618
				{
					//Log.DebugLog("Got instructions from private title", "GetInstructions()", Logger.severity.DEBUG);
					return;
				}
			}
		}

		private bool GetInstructions(string source)
		{
			MatchCollection matches = InstructionSets.Matches(source);

			for (int i = 0; i < matches.Count; i++)
			{
				string instruct = matches[i].Value.Substring(1, matches[i].Value.Length - 2);
				if (ParseAll(instruct))
				{
					//Log.DebugLog("instructions: " + instruct, "GetInstrucions()");
					HasInstructions = true;
					m_instructions = instruct;
					return true;
				}
			}

			return false;
		}

		private void GetFallbackInstructions(string fallback)
		{
			if (fallback.StartsWith("["))
				fallback = fallback.Substring(1, fallback.Length - 2);
			if (ParseAll(fallback))
			{
				HasInstructions = true;
				m_instructions = fallback;
			}
		}

	}
}
