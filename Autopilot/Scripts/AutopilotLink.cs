// skip file on build
#define LOG_ENABLED

using System;
//using System.Collections.Generic;
//using System.Linq;
using System.Text;
using VRageMath;

using Sandbox.ModAPI.Interfaces;

namespace Rynchodon.Autopilot
{
	/// <summary>
	/// For giving allInstructions to Autopilot
	/// </summary>
	public class AutopilotLink
	{
		private Logger myLogger = null;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null)
				try{
				myLogger = new Logger(myNav.myGrid.DisplayName, "AutopilotLink");
				} catch (NullReferenceException) {
					// use a temporary logger
					(new Logger(null, "AutopilotLink")).log(level, method, toLog);
					return;
				}
			myLogger.log(level, method, toLog);
		}

		/// <summary>
		/// Write a message to Autopilot's log. If logging is disabled or priority is not high enough, does nothing.
		/// </summary>
		/// <param name="toLog">message to write to the log</param>
		/// <param name="priority">how important the message is</param>
		public void writeToLog(string toLog, string method = "", Logger.severity priority = Logger.severity.INFO){
			log(toLog, "external-"+method, priority);
		}

		private Navigator myNav;
		/// <summary>
		/// may not be set
		/// </summary>
		private Sandbox.ModAPI.IMyCubeBlock myRC;

		private AutopilotLink() { }

		/// <summary>
		/// Get an AutopilotLink object for the given remoteControl
		/// </summary>
		/// <param name="remoteControl">the remote control that will fly the grid</param>
		/// <returns>a new AutopilotLink if creation was successful, null otherwise</returns>
		public static AutopilotLink getAutopilotLink(Sandbox.ModAPI.IMyCubeBlock remoteControl)
		{
			if (remoteControl == null)
				return null;

			AutopilotLink result = new AutopilotLink();
			if (Core.getNavForGrid(remoteControl.CubeGrid, out result.myNav))
				result.myRC = remoteControl;
			return result;
		}

		/// <summary>
		/// Get an AutopilotLink object for the given grid, will use any available remote control
		/// </summary>
		/// <param name="grid">the grid to fly</param>
		/// <returns>a new AutopilotLink if creation was successful, null otherwise</returns>
		public static AutopilotLink getAutopilotLink(Sandbox.ModAPI.IMyCubeGrid grid)
		{
			if (grid == null)
				return null;

			AutopilotLink result = new AutopilotLink();
			Core.getNavForGrid(grid, out result.myNav);
			return result;
		}

		/// <summary>
		/// Stop the Autopilot from using commands from the Remote Control's name. Control will also be taken when addCommand() is used.
		/// </summary>
		public void takeControl()
		{
			if (myNav.AIOverride)
				return;

			log("taking control", "takeControl()", Logger.severity.TRACE);
			myNav.AIOverride = true;
			myNav.reset();
			myNav.currentRCblock = myRC;
		}

		/// <summary>
		/// Allow the Autopilot to use commands from the Remote Control's name.
		/// </summary>
		public void releaseControl()
		{
			if (!myNav.AIOverride)
				return;

			log("release control", "releaseControl()", Logger.severity.TRACE);
			myNav.AIOverride = false;
			myNav.reset();
		}

		/// <summary>
		/// Add a command to the Autopilot, or multiple commands separated by (a) colon(s).
		/// Takes control
		/// </summary>
		/// <param name="command">command(s) to add</param>
		public void addCommand(string command)
		{
			takeControl();

			string noSpaces = command.Replace(" ", "");
			string[] inst = noSpaces.Split(':');
			foreach (string instruction in inst)
				myNav.CNS.instructions.Enqueue(instruction);
		}

		/// <summary>
		/// Adds each command in an array. Each string may contain multiple commands separated by (a) colon(s).
		/// Takes control
		/// </summary>
		/// <param name="commands">command(s) to add</param>
		public void addCommand(string[] commands)
		{
			foreach (string command in commands)
				addCommand(command);
		}

		/// <summary>
		/// Add a command to the Autopilot, or multiple commands separated by (a) colon(s).
		/// Takes control
		/// </summary>
		/// <param name="command">command(s) to add</param>
		public void addCommand(StringBuilder command)
		{
			addCommand(command.ToString());
		}

		/// <summary>
		/// Gets the world point which is the middle of the grid. Middle is defined as halfway between each extreme along each ship axis.
		/// </summary>
		/// <returns>The middle of the grid as a world vector.</returns>
		public Vector3D getGridPosition()
		{
			return;  //GridWorld.getMiddleAsWorld(myNav.myGrid);
		}

		/// <summary>
		/// Gets the world position of the remote control that is currently being used. May not be the same as the one supplied to getAutopilotLink().
		/// If no remote control is being used, returns a default Vector3D.
		/// </summary>
		/// <returns></returns>
		public Vector3D getRemoteControlPosition()
		{
			if (myNav.currentRCblock != null)
				return myNav.currentRCblock.GetPosition();
			return new Vector3D();
		}

		/// <summary>
		/// Gets the world point the Autopilot is currently trying to reach, not necissarily the destination.
		/// </summary>
		/// <returns></returns>
		public Vector3D getCurrentWaypoint()
		{
			return (Vector3D)myNav.CNS.getWayDest();
		}

		/// <summary>
		/// Gets the speed of the current grid. Accuracy is not guaranteed.
		/// </summary>
		/// <returns></returns>
		public double getSpeed()
		{
			return myNav.movementSpeed;
		}
	}
}
