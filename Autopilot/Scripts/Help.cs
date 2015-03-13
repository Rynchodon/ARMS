// skip file on build (different meaning for Help.cs)

//#define LOG_ENABLED //remove manually

using System;
using System.Collections.Generic;
//using System.Linq;
using System.Text;

using Sandbox.ModAPI;

namespace Rynchodon.Autopilot.Chat
{
	/// <summary>
	/// commands here are help commands / topics
	/// </summary>
	public static class Help
	{
		private const string designator = "/autopilot";

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
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private static void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ myLogger.log(level, method, toLog); }

		private static bool initialized = false;

		private static void initialize()
		{
			log("initializing", "initialize()", Logger.severity.TRACE);
			help_messages = new Dictionary<string, LinkedList<Command>>();
			List<Command> allCommands = new List<Command>();
			// fill commands by build
			foreach (Command current in allCommands)
			{
				LinkedList<Command> bucket;
				if (help_messages.TryGetValue(current.command, out bucket))
				{
					log("adding to existing bucket: " + current.command, "initialize()", Logger.severity.TRACE);
					bucket.AddLast(current);
				}
				else
				{
					log("creating new bucket for: " + current.command, "initialize()", Logger.severity.TRACE);
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
				if (!initialized)
					initialize();
				chatMessage = chatMessage.Trim();
				string[] chatSplit = chatMessage.Split(' ');
				if (chatSplit.Length == 0)
					return;
				if (!chatSplit[0].Equals(designator, StringComparison.OrdinalIgnoreCase))
					return;
				sendToOthers = false;
				if (chatSplit.Length == 1)
					printListCommands();
				else
					if (!printSingleCommand(chatSplit[1]))
						printListCommands();
			}
			catch (Exception e) {
				myLogger.log(Logger.severity.ERROR, "printCommand()", "Exception: " + e);
			}
		}

		private static void printListCommands()
		{
			log("entered printListCommands()", "printListCommands()", Logger.severity.TRACE);
			StringBuilder print = new StringBuilder();
			foreach (LinkedList<Command> bucket in help_messages.Values)
			{
				log("printing a bucket.", "printListCommands()", Logger.severity.TRACE);
				foreach (Command current in bucket)
				{
					log("sending message for command: " + current.command, "printListCommands()", Logger.severity.TRACE);
					//MyAPIGateway.Utilities.ShowNotification(current.commandExt);
					//MyAPIGateway.Utilities.ShowMissionScreen("Topics", string.Empty, string.Empty, 
					print.AppendLine(current.commandExt);
				}
			}
			log("showing mission screen for all", "printListCommands()", Logger.severity.TRACE);
			MyAPIGateway.Utilities.ShowMissionScreen("Autopilot Help", "Help Topics", string.Empty, print.ToString());
		}

		private static bool printSingleCommand(string command)
		{
			log("entered printSingleCommand()", "printSingleCommand()", Logger.severity.TRACE);
			StringBuilder print = new StringBuilder();
			if (command.Equals("direction", StringComparison.OrdinalIgnoreCase) || command.Equals("distance", StringComparison.OrdinalIgnoreCase))
				command += 's';
			command = command.ToUpper();

			LinkedList<Command> bucket;
			if (!help_messages.TryGetValue(command, out bucket))
			{
				myLogger.log("failed to get a bucket for: "+command, "printSingleCommand()", Logger.severity.WARNING);
				return false;
			}
			foreach (Command current in bucket)
			{
				log("sending message for command: " + current.command, "printSingleCommand()", Logger.severity.TRACE);
				//MyAPIGateway.Utilities.ShowNotification(current.description);
				print.AppendLine(current.description);
				print.AppendLine();
			}
			log("showing missing screen for command: " + command, "printSingleCommand()", Logger.severity.TRACE);
			MyAPIGateway.Utilities.ShowMissionScreen("Autopilot Help", command, string.Empty, print.ToString());
			return true;
		}
	}
}
