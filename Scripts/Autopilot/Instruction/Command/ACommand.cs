using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Rynchodon.Attached;
using Rynchodon.Autopilot.Movement;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public abstract class ACommand
	{

		static ACommand()
		{
			Logger.SetFileName("ACommand");
		}

		private string m_displayString;

		/// <summary>The full string that reflects this command, including Identifier.</summary>
		public string DisplayString { get { return m_displayString; } }

		public Action<Mover> Action { get; private set; }

		/// <summary>
		/// Sets DisplayString and parses to get Execute action.
		/// </summary>
		/// <param name="value">The full string that reflects this command, including identifier.</param>
		/// <param name="message">If Execute action could not be created, the reason. Otherwise, null.</param>
		/// <returns>True iff Execute action could be created by parsing value.</returns>
		public bool SetDisplayString(IMyCubeBlock autopilot, string value, out string message)
		{
			value = value.Replace('\n', ' ').Replace('\r', ' ');
			value = value.Trim();
			m_displayString = value;

			foreach (string idOrAlias in IdAndAliases())
			{
				Match m = Regex.Match(value, idOrAlias + @"\s*,?\s*(.*)", RegexOptions.IgnoreCase);
				if (m.Success)
				{
					Action = Parse(autopilot, m.Groups[1].Value, out message);
					return Action != null;
				}
			}

			message = value + " does not start with " + Identifier + " or any alias";
			Logger.AlwaysLog(message, Logger.severity.ERROR);
			Action = null;
			return false;
		}

		/// <summary>
		/// Attempt to create Execute from the terminal control values.
		/// </summary>
		/// <param name="message">If controls are valid, null. Otherwise, an error message for the player.</param>
		/// <returns>If validation was successful, the action to execute, otherwise null.</returns>
		public bool ValidateControls(IMyCubeBlock autopilot, out string message)
		{
			string termString = TermToString();
			if (termString == null)
			{
				termString = TermToString(out message);
				if (termString == null)
				{
					if (message == null)
						Logger.AlwaysLog("TermToString is not correctly implemented by command: " + Identifier + '/' + GetType().Name, Logger.severity.ERROR);
					Action = null;
					return false;
				}
			}
			return SetDisplayString(autopilot, termString, out message);
		}

		public IEnumerable<string> IdAndAliases()
		{
			yield return Identifier;
			string[] aliases = Aliases;
			if (aliases != null)
				foreach (string alias in aliases)
					yield return alias;
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
		public virtual void AppendCustomInfo(StringBuilder sb)
		{
			sb.AppendLine(AddDescription);
		}

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
		protected abstract Action<Mover> Parse(IMyCubeBlock autopilot, string command, out string message);

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

		#region GetVector

		protected bool GetVector(string command, out Vector3D result)
		{
			string[] parts = command.Split(',');
			return GetVectorXYZ(parts, out result) || GetVectorFromGeneric(parts, out result);
		}

		protected bool GetVector(string command, out Vector3 result)
		{
			Vector3D inner;
			bool success = GetVector(command, out inner);
			result = inner;
			return success;
		}

		private bool GetVectorXYZ(string[] parts, out Vector3D result)
		{
			if (parts.Length != 3)
			{
				result = Vector3.Invalid;
				return false;
			}

			double[] coords = new double[3];
			for (int index = 0; index < 3; index++)
				if (!PrettySI.TryParse(parts[index], out coords[index]))
				{
					result = Vector3.Invalid;
					return false;
				}

			result = new Vector3D(coords[0], coords[1], coords[2]);
			return true;
		}

		private bool GetVectorFromGeneric(string[] parts, out Vector3D result)
		{
			if (parts.Length == 0)
			{
				result = Vector3.Invalid;
				return false;
			}

			result = Vector3.Zero;
			foreach (string part in parts)
			{
				Vector3 partVector;
				if (!StringToVector3(part, out partVector))
				{
					result = Vector3.Invalid;
					return false;
				}
				result += partVector;
			}
			return true;
		}

		/// <param name="vectorString">takes the form "(number)(direction)"</param>
		private bool StringToVector3(string vectorString, out Vector3 result)
		{
			Match m = Regex.Match(vectorString, @"(.*)\s+(\w*)");

			Logger.DebugLog("vectorString: " + vectorString + ", group count: " + m.Groups.Count);

			if (m.Groups.Count != 3)
			{
				result = Vector3.Invalid;
				return false;
			}

			double value;
			if (!PrettySI.TryParse(m.Groups[1].Value, out value))
			{
				result = Vector3.Invalid;
				return false;
			}

			Base6Directions.Direction? direction = StringToDirection(m.Groups[2].Value);
			if (!direction.HasValue)
			{
				result = Vector3.Invalid;
				return false;
			}

			result = Base6Directions.GetVector(direction.Value) * (float)value;
			return true;
		}

		private Base6Directions.Direction? StringToDirection(string str)
		{
			int index;
			for (index = 0; true; index++)
				if (index < str.Length)
				{
					if (!char.IsWhiteSpace(str[index]))
						break;
				}
				else
					return null;

			switch (char.ToLower(str[index]))
			{
				case 'f':
					return Base6Directions.Direction.Forward;
				case 'b':
				case 'z':
					return Base6Directions.Direction.Backward;
				case 'l':
					return Base6Directions.Direction.Left;
				case 'r':
				case 'x':
					return Base6Directions.Direction.Right;
				case 'u':
				case 'y':
					return Base6Directions.Direction.Up;
				case 'd':
					return Base6Directions.Direction.Down;
			}
			return null;
		}

		#endregion GetVector

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

		/// <summary>
		/// Finds a block that is attached to Controller
		/// </summary>
		/// <remarks>
		/// Search is performed immediately.
		/// For a navigating block, always use AttachedGrid.AttachmentKind.None.
		/// </remarks>
		protected bool GetLocalBlock(IMyCubeBlock autopilot, string searchFor, out IMyCubeBlock localBlock, out string message, AttachedGrid.AttachmentKind allowedAttachments = AttachedGrid.AttachmentKind.None)
		{
			searchFor = searchFor.RemoveWhitespace();
			IMyCubeBlock foundBlock = null;
			int bestNameLength = int.MaxValue;

			foreach (IMyCubeBlock block in AttachedGrid.AttachedCubeBlocks(autopilot.CubeGrid, allowedAttachments, true))
				if (autopilot.canControlBlock(block))
				{
					string blockName = block.DisplayNameText.RemoveWhitespace();
					if (blockName.Length < bestNameLength && blockName.Contains(searchFor, StringComparison.InvariantCultureIgnoreCase))
					{
						foundBlock = block;
						bestNameLength = blockName.Length;
						if (searchFor.Length == bestNameLength)
							break;
					}
				}

			if (foundBlock == null)
			{
				message = "Not found: " + searchFor;
				localBlock = null;
				return false;
			}

			message = null;
			localBlock = foundBlock;
			return true;
		}

	}

}
