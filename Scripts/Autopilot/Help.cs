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

		private bool initialized = false;

		private void initialize()
		{
			Log.DebugLog("initializing", Logger.severity.TRACE);
			help_messages = new Dictionary<string, LinkedList<Command>>();
			List<Command> allCommands = new List<Command>();

			// fill commands by build
			
			foreach (Command current in allCommands)
			{
				LinkedList<Command> bucket;
				if (help_messages.TryGetValue(current.command, out bucket))
				{
					Log.DebugLog("adding to existing bucket: " + current.command, Logger.severity.TRACE);
					bucket.AddLast(current);
				}
				else
				{
					Log.DebugLog("creating new bucket for: " + current.command, Logger.severity.TRACE);
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
			{ Log.AlwaysLog("Exception: " + e, Logger.severity.ERROR); }
		}

		private void printListCommands()
		{
			Log.DebugLog("entered printListCommands()", Logger.severity.TRACE);
			StringBuilder print = new StringBuilder();
			foreach (LinkedList<Command> bucket in help_messages.Values)
			{
				Log.DebugLog("printing a bucket.", Logger.severity.TRACE);
				foreach (Command current in bucket)
				{
					Log.DebugLog("sending message for command: " + current.command, Logger.severity.TRACE);
					print.AppendLine(current.commandExt);
				}
			}
			Log.DebugLog("showing mission screen for all", Logger.severity.TRACE);
			MyAPIGateway.Utilities.ShowMissionScreen("Autopilot Help", "Help Topics", string.Empty, print.ToString());
		}

		private bool printSingleCommand(string command)
		{
			Log.DebugLog("entered printSingleCommand()", Logger.severity.TRACE);
			StringBuilder print = new StringBuilder();
			if (command.Equals("direction", StringComparison.InvariantCultureIgnoreCase) || command.Equals("distance", StringComparison.InvariantCultureIgnoreCase))
				command += 's';
			command = command.ToUpper();

			LinkedList<Command> bucket;
			if (!help_messages.TryGetValue(command, out bucket))
			{
				Log.AlwaysLog("failed to get a bucket for: " + command, Logger.severity.WARNING);
				return false;
			}
			foreach (Command current in bucket)
			{
				Log.DebugLog("sending message for command: " + current.command, Logger.severity.TRACE);
				print.AppendLine(current.description);
				print.AppendLine();
			}
			Log.DebugLog("showing mission screen for command: " + command, Logger.severity.TRACE);
			MyAPIGateway.Utilities.ShowMissionScreen("Autopilot Help", command, string.Empty, print.ToString());
			return true;
		}
	}
}
