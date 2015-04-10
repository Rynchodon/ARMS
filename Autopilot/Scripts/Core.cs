#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using System.Linq;
//using System.Text;

using Sandbox.Common;
//using Sandbox.Common.Components;
//using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
//using Sandbox.Engine;
//using Sandbox.Game;
using Sandbox.ModAPI;
//using Sandbox.ModAPI.Ingame;
//using Sandbox.ModAPI.Interfaces;

//using VrageLib = System.Runtime.CompilerServices;

namespace Rynchodon.Autopilot
{
	/// <summary>
	/// This ties Autopilot's navigation thread to the game's frame updates.
	/// This is the entry point for most of the Autopilot module's logic.
	/// </summary>
	[Sandbox.Common.MySessionComponentDescriptor(Sandbox.Common.MyUpdateOrder.BeforeSimulation)]
	public class Core : Sandbox.Common.MySessionComponentBase
	{
		private bool initialized = false;
		private bool terminated = false;
		private bool controlGrids = false;
		
		// for tracking which grids already have handlers and for iterating through handlers
		private Dictionary<Sandbox.ModAPI.IMyCubeGrid, Navigator> allNavigators;

		public long updateCount { get; private set; }
		private bool isUpdating = false;
		private const int FRAMES_BETWEEN_UPDATES = 10;
		private int gridsPerFrame;
		private int delayStart = 300; // updates before starting

		private readonly Logger myLogger = new Logger(null, "Core");

		private static Core Instance;

		#region Class Lifecycle
		private void init()
		{
			if (MyAPIGateway.Session == null)
				return;

			MyAPIGateway.Utilities.MessageEntered += Rynchodon.Autopilot.Chat.Help.printCommand;

			if (!MyAPIGateway.Multiplayer.MultiplayerActive)
			{
				myLogger.debugLog("I like to play offline so I control all grids!", "init()", Logger.severity.INFO);
				controlGrids = true;
			}
			else
				if (MyAPIGateway.Multiplayer.IsServer)
				{
					myLogger.debugLog("I am a server and I control all grids!", "init()", Logger.severity.INFO);
					controlGrids = true;
				}
				else
					myLogger.debugLog("I do not get to control any grids.", "init()", Logger.severity.INFO);

			bool AutopilotAllowed;
			if (!Settings.boolSettings.TryGetValue(Settings.BoolSetName.bAllowAutopilot, out AutopilotAllowed))
			{
				myLogger.debugLog("Failed to get bAllowAutopilot", "init()", Logger.severity.WARNING);
			}
			else if (!AutopilotAllowed)
			{
				myLogger.debugLog("Autopilot disabled", "init()", Logger.severity.INFO);
				controlGrids = false;
			}

			if (controlGrids) {
				allNavigators = new Dictionary<Sandbox.ModAPI.IMyCubeGrid, Navigator>();
				findNavigators();
			}

			myLogger.debugNotify("Autopilot Dev loaded", 10000);
			Instance = this;
			initialized = true;
		}

		private void terminate()
		{
			if (!terminated)
				terminated = true;
			myLogger.debugNotify("Autopilot encountered an exception and has been terminated.", 10000, Logger.severity.FATAL);
		}

		#endregion
		#region SessionComponent Hooks

		public override void UpdateBeforeSimulation()
		{
			if (delayStart > 0)
			{
				delayStart--;
				return;
			}

			//MainLock.Lock.ReleaseExclusive();
			//try
			//{
				if (isUpdating || terminated)
					return;
				isUpdating = true;
				//MyAPIGateway.Parallel.Start(doUpdate, updateCallback);
				doUpdate();
				isUpdating = false;
			//}
			//finally { MainLock.Lock.AcquireExclusive(); }
		}

		// cannot log here, Logger is closed/closing
		protected override void UnloadData()
		{
			allNavigators = null;
			try { MyAPIGateway.Utilities.MessageEntered -= Rynchodon.Autopilot.Chat.Help.printCommand; }
			catch { }
		}

		#endregion
		#region Updates

		private void doUpdate()
		{
			try
			{
				//Logger.debugLog("start update", "UpdateBeforeSimulation()", Logger.severity.TRACE);
				if (!initialized)
				{
					init();
					return;
				}
				if (!controlGrids)
					return;

				// distribute load over frames
				int frame = (int)(updateCount % FRAMES_BETWEEN_UPDATES);
				if (frame == 0)
				{
					findNavigators();

					gridsPerFrame = (int)Math.Ceiling(1.0 * allNavigators.Count / FRAMES_BETWEEN_UPDATES);
					//myLogger.debugLog("count is " + allNavigators.Count + " per frame is " + gridsPerFrame,
					//					"doUpdate()", Logger.severity.TRACE);
				}

				//myLogger.debugLog("for frame " + frame + " process " + (gridsPerFrame * frame) + " through " +
				// 					(gridsPerFrame * (1 + frame)), "doUpdate()", Logger.severity.TRACE);

				for (int index = gridsPerFrame * frame; index < gridsPerFrame * (1 + frame); index++)
				{
					if (index >= allNavigators.Count)
						break;
					//myLogger.debugLog("frame is: "+frame+", debug: index is "+index, "doUpdate()",
					// 					Logger.severity.TRACE);
					Navigator current = allNavigators.ElementAt(index).Value;

					try { current.update(); }
					catch (Exception updateEx)
					{
						myLogger.log("Exception on update: " + updateEx, null, Logger.severity.ERROR);

						try { remove(current, true); }
						catch (Exception resetEx)
						{ myLogger.log("Exception on reset: " + resetEx, null, Logger.severity.FATAL); }
					}
				}
				//log("end update", "UpdateBeforeSimulation()", Logger.severity.TRACE);
				updateCount++;
			}
			catch (Exception coreEx)
			{
				terminate();
				myLogger.log("Exception in core: " + coreEx, "remove()", Logger.severity.FATAL);
			}
		}

		#endregion
		#region Navigator Management

		// @wip @feature Attach to entity add/update events instead for less expensive searching of autopilot ships
		private void findNavigators()
		{
			HashSet<IMyEntity> entities = new HashSet<IMyEntity>();
			MyAPIGateway.Entities.GetEntities(entities, e => e is Sandbox.ModAPI.IMyCubeGrid);
			foreach (IMyEntity entity in entities)
			{
				Sandbox.ModAPI.IMyCubeGrid grid = entity as Sandbox.ModAPI.IMyCubeGrid;
				if (grid == null || grid.Closed || grid.MarkedForClose)
					continue;
				if (!allNavigators.ContainsKey(grid))
				{
					myLogger.debugLog("new grid added " + grid.DisplayName, "build", Logger.severity.INFO);
					Navigator cGridHandler = new Navigator(grid);
					allNavigators.Add(grid, cGridHandler);
				}
			}
		}

		/// <summary>
		/// Remove a navigator object from the list and pass any errors that
		/// caused its removal to the log output
		/// </summary>
		/// <param name="dead"></param>
		/// <param name="exception"></param>
		internal static void remove(Navigator dead, bool exception = false)
		{
			Instance.myLogger.debugLog("removing navigator " + dead.myGrid.DisplayName, "remove()", Logger.severity.INFO);
			Logger.severity level = exception ? Logger.severity.ERROR : Logger.severity.INFO;
			Instance.myLogger.debugNotify("Autopilot removed: " + dead.myGrid.DisplayName, 3000, level);

			if (!Instance.allNavigators.Remove(dead.myGrid))
				Instance.myLogger.log("failed to remove navigator " + dead.myGrid.DisplayName, "remove()", Logger.severity.WARNING);

			dead.Close();
		}

		#endregion
	}
}
