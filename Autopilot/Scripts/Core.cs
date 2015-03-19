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
	[Sandbox.Common.MySessionComponentDescriptor(Sandbox.Common.MyUpdateOrder.BeforeSimulation)]
	public class Core : Sandbox.Common.MySessionComponentBase
	{
		private static Logger myLogger = new Logger(null, "Core");
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private static void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ myLogger.log(level, method, toLog); }

		private static int framesBetweenUpdates = 10;

		private static Core Instance;

		public static long updateCount { get; private set; }
		private int gridsPerFrame;

		private static Dictionary<Sandbox.ModAPI.IMyCubeGrid, Navigator> allNavigators; // for tracking which grids already have handlers and for iterating through handlers
		//private static HashSet<Navigator> blacklist = new HashSet<Navigator>();

		//private static int exceptionFramesCount = 0;

		private static bool isUpdating = false;
		public override void UpdateBeforeSimulation()
		{
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

		//private void updateCallback() { isUpdating = false; }

		private void doUpdate()
		{
			try
			{
				//log("start update", "UpdateBeforeSimulation()", Logger.severity.TRACE);
				if (!initialized)
				{
					init();
					return;
				}
				if (!controlGrids)
				{
					return;
				}

				// distribute load over frames
				int frame = (int)(updateCount % framesBetweenUpdates);
				if (frame == 0)
				{
					build();

					gridsPerFrame = (int)Math.Ceiling(1.0 * allNavigators.Count / framesBetweenUpdates);
					//log("count is " + allNavigators.Count + " per frame is " + gridsPerFrame, "doUpdate()", Logger.severity.TRACE);
				}

				//log("for frame " + frame + " process " + (gridsPerFrame * frame) + " through " + (gridsPerFrame * (1 + frame)), "doUpdate()", Logger.severity.TRACE);

				for (int index = gridsPerFrame * frame; index < gridsPerFrame * (1 + frame); index++)
				{
					if (index >= allNavigators.Count)
						break;
					//log("frame is: "+frame+", debug: index is "+index, "doUpdate()", Logger.severity.TRACE);
					Navigator current = allNavigators.ElementAt(index).Value;

					//if (blacklist.Contains(current))
					//	continue;
					try
					{
						current.update();
					}
					catch (Exception updateEx)
					{
						myLogger.log(Logger.severity.WARNING, null, "Exception on update: " + updateEx);
						try
						{
							remove(current, true);
							//current.reset();
						}
						catch (Exception resetEx)
						{
							myLogger.log(Logger.severity.FATAL, null, "Exception on reset: " + resetEx);
							//addToBlacklist(current, true);
						}
					}
				}
				//log("end update", "UpdateBeforeSimulation()", Logger.severity.TRACE);
				updateCount++;
			}
			catch (Exception coreEx)
			{
				terminate();
				myLogger.log(Logger.severity.FATAL, null, "Exception in core: " + coreEx);
			}
		}

		private bool terminated = false;
		private void terminate()
		{
			if (terminated)
				return;
			terminated = true;
			MyAPIGateway.Utilities.ShowNotification("Autopilot encountered an exception and has been terminated.", 10000, MyFontEnum.Red);
		}

		//internal static void addToBlacklist(Navigator broken, bool Exception = false)
		//{
		//	myLogger.log(Logger.severity.INFO, null, "blacklisting " + broken.myGrid.DisplayName);
		//	interruptingCow(broken.myGrid.DisplayName, Exception);
		//	blacklist.Add(broken);
		//	try
		//	{
		//		broken.fullStop("broken");
		//		broken.reportState(Navigator.ReportableState.BROKEN);
		//	}
		//	catch (Exception stopping)
		//	{
		//		myLogger.log(Logger.severity.WARNING, null, "Exception on stopping: " + stopping);
		//	}
		//}

		internal static void remove(Navigator dead, bool Exception = false)
		{
			log("removing navigator " + dead.myGrid.DisplayName, "remove()", Logger.severity.INFO);
			interruptingCow(dead.myGrid.DisplayName, Exception);
			if (!allNavigators.Remove(dead.myGrid))
				myLogger.log("failed to remove navigator " + dead.myGrid.DisplayName, "remove()", Logger.severity.WARNING);
			//blacklist.Remove(dead);
		}

		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private static void interruptingCow(string name, bool Exception)
		{
			if (Exception)
				MyAPIGateway.Utilities.ShowNotification("Autopilot removed: " + name, 3000, MyFontEnum.Red);
			else
				MyAPIGateway.Utilities.ShowNotification("Autopilot removed: " + name, 3000, MyFontEnum.Green);
		}

		private static bool initialized = false;
		private static bool controlGrids = false;

		private void init()
		{
			if (MyAPIGateway.Session == null)
				return;

			Instance = this;

			//Settings.open();

			MyAPIGateway.Utilities.MessageEntered += Rynchodon.Autopilot.Chat.Help.printCommand;

			if (!MyAPIGateway.Multiplayer.MultiplayerActive)
			{
				log("I like to play offline so I control all grids!", "init()", Logger.severity.INFO);
				controlGrids = true;
			}
			else
				if (MyAPIGateway.Multiplayer.IsServer)
				{
					log("I am a server and I control all grids!", "init()", Logger.severity.INFO);
					controlGrids = true;
				}
				else
					log("I do not get to control any grids.", "init()", Logger.severity.INFO);

			bool AutopilotAllowed;
			if (!Settings.boolSettings.TryGetValue(Settings.BoolSetName.bAllowAutopilot, out AutopilotAllowed))
			{
				log("Failed to get bAllowAutopilot", "init()", Logger.severity.WARNING);
			}
			else if (!AutopilotAllowed)
			{
				log("Autopilot disabled", "init()", Logger.severity.INFO);
				controlGrids = false;
			}

			if (controlGrids)
			{
				allNavigators = new Dictionary<Sandbox.ModAPI.IMyCubeGrid, Navigator>();
				build();
			}

			myLogger.ShowNotificationDebug("Autopilot Dev loaded", 10000);

			//try
			//{
			//	if (MyAPIGateway.Utilities.FileExistsInLocalStorage("Autopilot.log", typeof(Core)))
			//		MyAPIGateway.Utilities.DeleteFileInLocalStorage("Autopilot.log", typeof(Core));
			//}
			//catch (Exception e)
			//{
			//	log("failed to delete old log file", "init()", Logger.severity.INFO);
			//	log("Exception: " + e, "init()", Logger.severity.INFO);
			//}

			initialized = true;
		}

		private void build()
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
					log("new grid added "+grid.DisplayName, "build", Logger.severity.INFO);
					Navigator cGridHandler = new Navigator(grid);
					allNavigators.Add(grid, cGridHandler);
				}
			}
		}

		/// <summary>
		/// as Dictionary.TryGetValue()
		/// </summary>
		/// <param name="gridToLookup"></param>
		/// <param name="navForGrid"></param>
		/// <returns></returns>
		internal static bool getNavForGrid(Sandbox.ModAPI.IMyCubeGrid gridToLookup, out Navigator navForGrid)
		{
			if (Instance == null || allNavigators == null)
			{
				navForGrid = null;
				return false;
			}
			return allNavigators.TryGetValue(gridToLookup, out navForGrid);
		}

		// cannot log here, Logger is closed/closing
		protected override void UnloadData()
		{
			//try { Settings.writeAll(); }
			//catch { }
			allNavigators = null;
			try { MyAPIGateway.Utilities.MessageEntered -= Rynchodon.Autopilot.Chat.Help.printCommand; }
			catch { }
		}
	}
}
