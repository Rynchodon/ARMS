using System;
using System.Collections.Generic;
using Rynchodon.Autopilot.Movement;
using Sandbox.ModAPI.Interfaces.Terminal;

namespace Rynchodon.Autopilot.Instruction
{
	public abstract class ACommand
	{

		private string m_displayString;

		/// <summary>The full string that reflects this command, including Identifier.</summary>
		public string DisplayString { get { return m_displayString; } }

		/// <summary>
		/// Sets DisplayString and parses to get Execute action.
		/// </summary>
		/// <param name="value">The full string that reflects this command, including identifier.</param>
		/// <param name="message">If Execute action could not be created, the reason. Otherwise, null.</param>
		/// <returns>True iff Execute action could be created by parsing value.</returns>
		public Action<Mover> SetDisplayString(string value, out string message)
		{
			value = value.Trim();
			m_displayString = value;

			if (!value.StartsWith(Identifier, StringComparison.InvariantCultureIgnoreCase))
			{
				message = value + " does not start with " + Identifier;
				return null;
			}

			value = value.Substring(Identifier.Length).TrimStart();
			if (value.StartsWith(","))
				value = value.Substring(1);
			value = value.TrimStart();

			return Parse(value, out message);
		}

		/// <summary>
		/// Attempt to create Execute from the terminal control values.
		/// </summary>
		/// <param name="message">If controls are valid, null. Otherwise, an error message for the player.</param>
		/// <returns>If validtion was successful, the action to execute, otherwise null.</returns>
		public Action<Mover> ValidateControls(out string message)
		{
			return SetDisplayString(TermToString(), out message);
		}

		/// <summary>
		/// Create a command using no-arg constructor.
		/// </summary>
		/// <returns>A new commnad, created with no-arg constructor.</returns>
		public abstract ACommand CreateCommand();

		/// <summary>
		/// How the command is identified when it is a string, always lower-case.
		/// </summary>
		public abstract string Identifier { get; }

		/// <summary>
		/// Tooltip that explains the command a bit.
		/// </summary>
		public abstract string Tooltip { get; }

		/// <summary>
		/// Adds terminal controls, specific to the command, to the end of the list for players to interact with.
		/// </summary>
		/// <param name="controls">The current list of controls.</param>
		public abstract void AddControls(List<IMyTerminalControl> controls);

		/// <summary>
		/// Attempt to parse a string to generate an action to execute.
		/// </summary>
		/// <param name="command">The command, without Identifier, leading comma or trailing or leading whitespace.</param>
		/// <param name="message">If parsing was successful, null. Otherwise, an error message for the player.</param>
		/// <returns>If parsing was successful, the action to execute, otherwise null.</returns>
		protected abstract Action<Mover> Parse(string command, out string message);

		/// <summary>
		/// Convert the terminal input to a string, which is then parsed.
		/// </summary>
		/// <returns>A string representing the terminal input.</returns>
		protected abstract string TermToString();

	}

}
