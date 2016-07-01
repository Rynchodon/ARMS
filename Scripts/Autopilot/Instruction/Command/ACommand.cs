using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Rynchodon.Autopilot.Movement;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRageMath;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public abstract class ACommand
	{

		static ACommand()
		{
			Logger.SetFileName("ACommand");
		}

		private string m_displayString, m_error;

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
		/// <returns>If validation was successful, the action to execute, otherwise null.</returns>
		public Action<Mover> ValidateControls(out string message)
		{
			string termString = TermToString();
			if (termString == null)
			{
				termString = TermToString(out message);
				if (termString == null)
				{
					if (message == null)
						Logger.AlwaysLog("TermToString is not correctly implemented by command: " + Identifier, Logger.severity.ERROR);
					return null;
				}
			}
			return SetDisplayString(termString, out message);
		}

		/// <summary>
		/// Deep copy of a command.
		/// </summary>
		/// <returns>A new command, that is a deep copy.</returns>
		public abstract ACommand Clone();

		/// <summary>
		/// How the command is identified when it is a string, always lower-case.
		/// </summary>
		public abstract string Identifier { get; }

		/// <summary>
		/// The name of the command before adding.
		/// </summary>
		public abstract string AddName { get; }

		/// <summary>
		/// Description used before adding a command, before it has a value.
		/// </summary>
		public abstract string AddDescription { get; }

		/// <summary>
		/// Description used when command has a value.
		/// </summary>
		public abstract string Description { get; }

		/// <summary>
		/// Has terminal controls for player to play with.
		/// </summary>
		public virtual bool HasControls { get { return true; } }

		/// <summary>
		/// Aliases for the identifier.
		/// </summary>
		public virtual string[] Aliases { get { return null; } }

		/// <summary>
		/// Append custom info for a command that is being added/edited.
		/// </summary>
		/// <param name="sb">To append info to</param>
		public virtual void AppendCustomInfo(StringBuilder sb) { }

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
		/// Convert the terminal input to a string, which is then parsed. The string shall include the identifier.
		/// </summary>
		/// <returns>A string representing the terminal input.</returns>
		/// <remarks>
		/// Commands should override one or the other TermToString function.
		/// </remarks>
		protected virtual string TermToString() { return null; }

		/// <summary>
		/// Convert the terminal input to a string, which is then parsed. The string shall include the identifier.
		/// If the command is invalid, returns null and message explains the issue.
		/// </summary>
		/// <param name="message">The reason the string could not be created. Null on success.</param>
		/// <returns>The string representation of this command.</returns>
		/// <remarks>
		/// Commands should override one or the other TermToString function.
		/// </remarks>
		protected virtual string TermToString(out string message)
		{
			message = null;
			return null;
		}

		protected bool GetVectorFromGeneric(string[] parts, out Vector3 result)
		{
			if (parts.Length == 0)
			{
				result = default(Vector3);
				return false;
			}

			result = Vector3.Zero;
			foreach (string part in parts)
			{
				Vector3 partVector;
				if (!StringToVector3(part, out partVector))
				{
					return false;
				}
				result += partVector;
			}
			return true;
		}

		protected bool SplitNameDirections(string toSplit, out string blockName, out Base6Directions.Direction? forward, out Base6Directions.Direction? upward, out string message)
		{
			forward = null;
			upward = null;

			if (string.IsNullOrWhiteSpace(toSplit))
			{
				message = "No arguments";
				blockName = null;
				return false;
			}

			string[] splitComma = toSplit.Split(',');

			blockName = splitComma[0].RemoveWhitespace();

			switch (splitComma.Length)
			{
				case 1:
					message = null;
					return true;
				case 2:
					forward = StringToDirection(splitComma[1]);
					if (!forward.HasValue)
					{
						message = "Not a direction: " + splitComma[1];
						return false;
					}
					message = null;
					return true;
				case 3:
					upward = StringToDirection(splitComma[2]);
					if (!upward.HasValue)
					{
						message = "Not a direction: " + splitComma[2];
						return false;
					}
					goto case 2;
				default:
					message = "Too many arguments";
					return false;
			}
		}

		/// <param name="vectorString">takes the form "(number)(direction)"</param>
		private bool StringToVector3(string vectorString, out Vector3 result)
		{
			Match m = Regex.Match(vectorString, @"(.*)\s+(\w*)");

			Logger.DebugLog("ACommand", "vectorString: " + vectorString + ", group count: " + m.Groups.Count);

			if (m.Groups.Count != 3)
			{
				result = default(Vector3);
				return false;
			}

			double value;
			if (!PrettySI.TryParse(m.Groups[1].Value, out value))
			{
				result = default(Vector3);
				return false;
			}

			Base6Directions.Direction? direction = StringToDirection(m.Groups[2].Value);
			if (!direction.HasValue)
			{
				result = default(Vector3);
				return false;
			}

			result = Base6Directions.GetVector(direction.Value) * (float)value;
			return true;
		}

		private Base6Directions.Direction? StringToDirection(string str)
		{
			if (str.Length < 1)
				return null;
			switch (char.ToLower(str[0]))
			{
				case 'f':
					return Base6Directions.Direction.Forward;
				case 'b':
					return Base6Directions.Direction.Backward;
				case 'l':
					return Base6Directions.Direction.Left;
				case 'r':
					return Base6Directions.Direction.Right;
				case 'u':
					return Base6Directions.Direction.Up;
				case 'd':
					return Base6Directions.Direction.Down;
			}
			return null;
		}

	}

}
