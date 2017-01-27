using System;
using System.Text.RegularExpressions;
using Rynchodon.Autopilot.Instruction.Command;
using Sandbox.ModAPI;

namespace Rynchodon.Autopilot.Instruction
{
	public class TextPanelMonitor
	{

		public readonly IMyTextPanel TextPanel;
		private readonly AutopilotCommands m_autoCommands;
		private string m_panelText, m_identifier;
		private bool m_private = false;

		private AutopilotActionList m_autopilotActions = new AutopilotActionList();

		public AutopilotActionList AutopilotActions
		{
			get
			{
				CheckCommandsChanged();
				return m_autopilotActions;
			}
		}

		public TextPanelMonitor(IMyTextPanel textPanel, AutopilotCommands autoCommands, string identifier)
		{
			this.TextPanel = textPanel;
			this.m_autoCommands = autoCommands;
			this.m_identifier = identifier;
		}

		private void CheckCommandsChanged()
		{
			string currentText = m_private ?
#pragma warning disable CS0618
				TextPanel.GetPrivateText() :
#pragma warning restore CS0618
				TextPanel.GetPublicText();
			if (currentText == m_panelText)
				return;

			m_panelText = currentText;
			GetAutopilotActions();
		}

		private void GetAutopilotActions()
		{
			string pattern = string.IsNullOrWhiteSpace(m_identifier) ? @"\[(.*?)\]" : m_identifier + @".*?\[(.*?)\]";

			Match m = Regex.Match(m_panelText, pattern, RegexOptions.Singleline);
			if (m.Success)
			{
				string commands = m.Groups[1].Value;
				if (!string.IsNullOrWhiteSpace(commands))
				{
					GetAutopilotActions(commands);
					return;
				}
			}

			m_private = !m_private;
			m_panelText = m_private ?
#pragma warning disable CS0618
				TextPanel.GetPrivateText() :
#pragma warning restore CS0618
				TextPanel.GetPublicText();
			m = Regex.Match(m_panelText, pattern, RegexOptions.Singleline);
			if (m.Success)
			{
				string commands = m.Groups[1].Value;
				if (!string.IsNullOrWhiteSpace(commands))
				{
					GetAutopilotActions(commands);
					return;
				}
			}

			AutopilotActions.Clear();
			return;
		}

		private void GetAutopilotActions(string commands)
		{
			Logger.AlwaysLog("Commands: " + commands.Replace('\n', ' '));
			AutopilotActions.Clear();
			m_autoCommands.GetActions(commands, AutopilotActions);
		}

	}
}
