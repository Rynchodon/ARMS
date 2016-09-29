// skip file on build (different meaning for Help.cs)

using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;

namespace Rynchodon.Autopilot
{
	/// <summary>
	/// commands here are help commands / topics
	/// </summary>
	public class Help
	{
		private const string pri_designator = "/arms help";
		private const string alt_designator = "/autopilot help";

		private struct Command
		{
			public string command;
			public string commandExt;
			public string description;

			public Command(string fromReadme)
			{
				fromReadme = fromReadme.Replace("[b]", string.Empty);
				fromReadme = fromReadme.Replace("[/b]", string.Empty);

				this.command = fromReadme.Split(' ')[0].Trim().ToUpper();
				this.commandExt = fromReadme.Split(':')[0].Trim();
				this.description = fromReadme.Trim();
			}
		}

		private Dictionary<string, LinkedList<Command>> help_messages;

		private Logger myLogger = new Logger() { MinimumLevel = Logger.severity.DEBUG };

		private bool initialized = false;

		private void initialize()
		{
			myLogger.debugLog("initializing", Logger.severity.TRACE);
			help_messages = new Dictionary<string, LinkedList<Command>>();
			List<Command> allCommands = new List<Command>();

			// fill commands by build
			
			foreach (Command current in allCommands)
			{
				LinkedList<Command> bucket;
				if (help_messages.TryGetValue(current.command, out bucket))
				{
					myLogger.debugLog("adding to existing bucket: " + current.command, Logger.severity.TRACE);
					bucket.AddLast(current);
				}
				else
				{
					myLogger.debugLog("creating new bucket for: " + current.command, Logger.severity.TRACE);
					bucket = new LinkedList<Command>();
					bucket.AddLast(current);
					help_messages.Add(current.command, bucket);
				}
			}
			initialized = true;
		}

		public void printCommand(string message)
		{
			try
			{
				if (!initialized)
					initialize();

				if (string.IsNullOrWhiteSpace(message))
					printListCommands();
				else
					printSingleCommand(message);
			}
			catch (Exception e)
			{ myLogger.alwaysLog("Exception: " + e, Logger.severity.ERROR); }
		}

		private void printListCommands()
		{
			myLogger.debugLog("entered printListCommands()", Logger.severity.TRACE);
			StringBuilder print = new StringBuilder();
			foreach (LinkedList<Command> bucket in help_messages.Values)
			{
				myLogger.debugLog("printing a bucket.", Logger.severity.TRACE);
				foreach (Command current in bucket)
				{
					myLogger.debugLog("sending message for command: " + current.command, Logger.severity.TRACE);
					print.AppendLine(current.commandExt);
				}
			}
			myLogger.debugLog("showing mission screen for all", Logger.severity.TRACE);
			MyAPIGateway.Utilities.ShowMissionScreen("Autopilot Help", "Help Topics", string.Empty, print.ToString());
		}

		private bool printSingleCommand(string command)
		{
			myLogger.debugLog("entered printSingleCommand()", Logger.severity.TRACE);
			StringBuilder print = new StringBuilder();
			if (command.Equals("direction", StringComparison.OrdinalIgnoreCase) || command.Equals("distance", StringComparison.OrdinalIgnoreCase))
				command += 's';
			command = command.ToUpper();

			LinkedList<Command> bucket;
			if (!help_messages.TryGetValue(command, out bucket))
			{
				myLogger.alwaysLog("failed to get a bucket for: " + command, Logger.severity.WARNING);
				return false;
			}
			foreach (Command current in bucket)
			{
				myLogger.debugLog("sending message for command: " + current.command, Logger.severity.TRACE);
				print.AppendLine(current.description);
				print.AppendLine();
			}
			myLogger.debugLog("showing mission screen for command: " + command, Logger.severity.TRACE);
			MyAPIGateway.Utilities.ShowMissionScreen("Autopilot Help", command, string.Empty, print.ToString());
			return true;
		}
	}
}
