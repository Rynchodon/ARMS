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
allCommands.Add(new Command(@"C <x>, <y>, <z> : for flying to specific world coordinates.
Example - [ C 0, 0, 0 : C 500, 500, 500 ] - fly to {0, 0, 0} then fly to {500, 500, 500}, will keep flying back and forth
"));
allCommands.Add(new Command(@"E <range> : Any time after E is set, fly towards the nearest enemy grid. While no enemy is in range, continue following commands. Use 0 for infinite range. Use OFF to disable.
Example - [ E 0 ] - move towards any detected enemy
"));
allCommands.Add(new Command(@"EXIT : stop the Autopilot, do not loop. Useful for one-way autopilot
Example - [ E 0 : C 0, 0, 0 : EXIT ] - will target any enemy that comes into range while flying to {0, 0, 0}. Upon reaching {0, 0, 0}, will stop searching for a target
"));
allCommands.Add(new Command(@"G <name> : fly towards a specific friendly grid, by name of grid (Ship Name)
Example - [ G Platform : EXIT ] - Fly to a friendly grid that has ""Platform"" in its name, then stop
"));
allCommands.Add(new Command(@"M <range> : same as E but for a missile
Example - [ M 0 ] - Attempt to crash into any enemy that can be detected.
"));
allCommands.Add(new Command(@"W <seconds> : wait before travelling to the next destination
Example - [ C 0, 0 , 0 : W 60 : C 500, 500, 500 : EXIT ] - Will wait for 60 seconds after reaching {0, 0, 0}
"));
allCommands.Add(new Command(@"A <block>, <action> : Run an action on one or more blocks. <action> is case-sensitive. Autopilot will find every block that contains <block>, find the ITerminalAction that matches <action>, and apply it. Block must have faction share with remote's owner.
Example - [ A Thrust, OnOff_On ] - turn all the thrusters on
"));
allCommands.Add(new Command(@"B <name> : for navigating to a specific block on a grid, will only affect the next use of G, E, or M. For friendly grids uses the display name; for hostile grids the definition name. Target block must be working.
Example - [ B Antenna : G Platform ] - fly to Antenna on Platform
Example - [ B Reactor : E 0 ] - will only target an enemy with a working reactor
"));
allCommands.Add(new Command(@"B <name>, <direction> : <direction> indicates which direction to approach block from when landing
Example - [ L Landing Gear : B Beacon, Rightward : G Platform : W 60 ] - Attach landing gear to the right side of beacon on Platform
"));
allCommands.Add(new Command(@"F <r>, <u>, <b> : fly a distance relative to self. coordinates are rightward, upward, backwards
Example - [ F 0, 0, -1000 ] - fly 1000m forward
"));
allCommands.Add(new Command(@"F <distance> <direction>, ... : generic form is a comma separated list of distances and directions
Example - [ F 1000 forward ] - fly 1000m forward
Example - [ F 1000 forward, 200 downward ] - fly to a point 1000m ahead and 200m below remote control
"));
allCommands.Add(new Command(@"L : landing block. the block on the same grid as the Remote that will be used to land. To land there must be a landing block, a target block, and a target grid [ L <localBlock> : B <targetBlock> : G <targetGrid> ]. If there is a wait command before the next destination, the grid will wait while attached. If there is a LOCK command before the next destination, the grid will not separate. If there is an EXIT command before the next destination, the grid will stay attached.
Example - [ L Connector : B Connector : G Platform : W60 : C 0,0,0 ] - attach local connector to connector on Platform, wait 60 seconds, detach, fly to {0,0,0}
"));
allCommands.Add(new Command(@"LOCK : leave landing block in locked state ( do not disconnect )
Example - [ L Connector : B Connector : G Pickup : LOCK : F 0, 0, 0 ] - connect with Pickup, fly to {0, 0, 0}
"));
allCommands.Add(new Command(@"O <r>, <u>, <b> : destination offset, only works if there is a block target, cleared after reaching destination. coordinates are right, up, back. NO_PATH if offset destination is inside the boundaries of any grid (including destination).
Example - [ O 0, 500, 0 : B Antenna : G Platform ] - fly to 500m above Antenna on Platform
"));
allCommands.Add(new Command(@"O <distance> <direction>, ... : generic form is a comma separated list of distances and directions
Example - [ O 500 upward : B Antenna : G Platform ] - fly to 500m above Antenna on Platform
Example - [ O 100 forward, 500 upward : B Antenna : G Platform ] - fly to 100m ahead of and 500m above Antenna
"));
allCommands.Add(new Command(@"P <range> : how close the grid needs to fly to the destination, default is 100m. Ignored for M.
Example - [ P 10 : F 0, 0, -100 ] - Fly 100m forward
"));
allCommands.Add(new Command(@"R <f> : match direction, needs a target block. <f> is the target block's direction that the Remote Control will face
Example - [ R Forward : B Antenna : G Platform ] - fly to Antenna on Platform, then face Remote Control to antenna forward
"));
allCommands.Add(new Command(@"R <f>, <u> : match orientation (direction and roll). <f> as above. <u> which block direction will be Remote Control's Up
Example - [ R Forward, Upward : B Antenna : G Platform ] - fly to Antenna on Platform, then face Remote Control to antenna forward, then roll so that Antenna's Upward will match Remote Control's upward.
"));
allCommands.Add(new Command(@"V <cruise> : when travelling faster than <cruise> reduce thrust (zero or very little thrust)
Example - [ V 10 : C 0, 0, 0 : C 500, 500, 500 ] - fly back and forth between {0, 0, 0} and {500, 500, 500}, cruising when above 10m/s. The default for <cruise> is set in the settings file.
"));
allCommands.Add(new Command(@"V <cruise>, <slow> : when speed is below <cruise>, accelerate; when speed is between <cruise> and <slow>, cruise; when speed is above <slow>, decelerate. The default for <cruise> is set in the settings file, the default for <slow> is infinity (practically).
Example - [ V 10, 20 : C 0, 0, 0 : C 500, 500, 500 ] - fly back and forth between {0, 0, 0} and {500, 500, 500}, staying between 10m/s and 20m/s.
"));
allCommands.Add(new Command(@"[b]Directions[/b] : can be { Forward, Backward, Leftward, Rightward, Upward, Downward }. Autopilot only checks the first letter, so abbreviations will work. For example, ""Forward"" can be ""Fore"" or ""F""
"));
allCommands.Add(new Command(@"[b]Distances[/b] : for F, O, and P can be modified by km(kilometres) or Mm(megametres). For example, ""3.5M"" or ""3.5Mm"" is 3.5 megametres or 3 500 kilometres.
"));
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
