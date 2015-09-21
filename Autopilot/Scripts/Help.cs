// skip file on build (different meaning for Help.cs)

using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;

namespace Rynchodon.Autopilot.Chat
{
	/// <summary>
	/// commands here are help commands / topics
	/// </summary>
	public static class Help
	{
		private const string designator = "/autopilot help";

		private struct Command
		{
			public string command;
			public string commandExt;
			public string description;

			public Command(string fromReadme)
			{
				fromReadme = fromReadme.Replace("[b]", string.Empty);
				fromReadme = fromReadme.Replace("[/b]", string.Empty);

				this.command = fromReadme.Split(' ')[0].Trim().ToUpper(); // should already be upper
				this.commandExt = fromReadme.Split(':')[0].Trim();
				this.description = fromReadme.Trim();
			}
		}

		private static Dictionary<string, LinkedList<Command>> help_messages;

		private static Logger myLogger = new Logger(null, "Help");

		private static bool initialized = false;

		private static void initialize()
		{
			myLogger.debugLog("initializing", "initialize()", Logger.severity.TRACE);
			help_messages = new Dictionary<string, LinkedList<Command>>();
			List<Command> allCommands = new List<Command>();

			// fill commands by build
			
			foreach (Command current in allCommands)
			{
				LinkedList<Command> bucket;
				if (help_messages.TryGetValue(current.command, out bucket))
				{
					myLogger.debugLog("adding to existing bucket: " + current.command, "initialize()", Logger.severity.TRACE);
					bucket.AddLast(current);
				}
				else
				{
					myLogger.debugLog("creating new bucket for: " + current.command, "initialize()", Logger.severity.TRACE);
					bucket = new LinkedList<Command>();
					bucket.AddLast(current);
					help_messages.Add(current.command, bucket);
				}
			}
			initialized = true;
		}

		public static void printCommand(string chatMessage, ref bool sendToOthers)
		{
			try
			{
				chatMessage = chatMessage.ToLower();

				if (!chatMessage.StartsWith(designator))
					return;

				string sub = chatMessage.Replace(designator, "").Trim();
				sendToOthers = false;

				if (!initialized)
					initialize();

				if (string.IsNullOrWhiteSpace(sub) || !printSingleCommand(sub))
					printListCommands();
			}
			catch (Exception e)
			{ myLogger.alwaysLog("Exception: " + e, "printCommand()", Logger.severity.ERROR); }
		}

		private static void printListCommands()
		{
			myLogger.debugLog("entered printListCommands()", "printListCommands()", Logger.severity.TRACE);
			StringBuilder print = new StringBuilder();
			foreach (LinkedList<Command> bucket in help_messages.Values)
			{
				myLogger.debugLog("printing a bucket.", "printListCommands()", Logger.severity.TRACE);
				foreach (Command current in bucket)
				{
					myLogger.debugLog("sending message for command: " + current.command, "printListCommands()", Logger.severity.TRACE);
					print.AppendLine(current.commandExt);
				}
			}
			myLogger.debugLog("showing mission screen for all", "printListCommands()", Logger.severity.TRACE);
			MyAPIGateway.Utilities.ShowMissionScreen("Autopilot Help", "Help Topics", string.Empty, print.ToString());
		}

		private static bool printSingleCommand(string command)
		{
			myLogger.debugLog("entered printSingleCommand()", "printSingleCommand()", Logger.severity.TRACE);
			StringBuilder print = new StringBuilder();
			if (command.Equals("direction", StringComparison.OrdinalIgnoreCase) || command.Equals("distance", StringComparison.OrdinalIgnoreCase))
				command += 's';
			command = command.ToUpper();

			LinkedList<Command> bucket;
			if (!help_messages.TryGetValue(command, out bucket))
			{
				myLogger.alwaysLog("failed to get a bucket for: " + command, "printSingleCommand()", Logger.severity.WARNING);
				return false;
			}
			foreach (Command current in bucket)
			{
				myLogger.debugLog("sending message for command: " + current.command, "printSingleCommand()", Logger.severity.TRACE);
				print.AppendLine(current.description);
				print.AppendLine();
			}
			myLogger.debugLog("showing mission screen for command: " + command, "printSingleCommand()", Logger.severity.TRACE);
			MyAPIGateway.Utilities.ShowMissionScreen("Autopilot Help", command, string.Empty, print.ToString());
			return true;
		}
	}
}
