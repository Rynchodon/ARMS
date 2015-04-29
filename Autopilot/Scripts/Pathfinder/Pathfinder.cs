#define LOG_ENABLED // remove on build

using Sandbox.ModAPI;
using System;
using VRage;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinder
{
	/// <summary>
	/// Finds alternate paths if the current path is blocked.
	/// </summary>
	public class Pathfinder
	{
		public readonly IMyCubeGrid CubeGrid;
		public Vector3D Destination { get; private set; }
		public IMyCubeBlock NavigationBlock { get; private set; }
		public bool IgnoreAsteroids { get; private set; }

		private PathChecker myPathChecker;
		private static ThreadManager PathFinderThread = new ThreadManager();

		/// <summary>next time CheckPath() is allowed to run</summary>
		private DateTime nextRun = DateTime.MinValue;

		private Logger myLogger;

		public Pathfinder(IMyCubeGrid grid)
		{
			grid.throwIfNull_argument("grid");
			CubeGrid = grid;
			myPathChecker = new PathChecker(grid);
			myLogger = new Logger("Pathfinder", () => grid.DisplayName);
		}

		private PathfinderOutput myOutput;
		private FastResourceLock lock_myOutput = new FastResourceLock();

		private void SetOutput(PathfinderOutput newOutput)
		{
			if (myPathChecker != null && myPathChecker.Interrupt)
				return;
			using (lock_myOutput.AcquireExclusiveUsing())
				myOutput = newOutput;
		}

		public PathfinderOutput GetOutput()
		{
			using (lock_myOutput.AcquireSharedUsing())
				return myOutput;
		}

		public void Run(Vector3D destination, IMyCubeBlock NavigationBlock, bool ignoreAsteroids)
		{
			// if something has changed, interrupt previous
			if (this.Destination != destination || this.NavigationBlock != NavigationBlock || this.IgnoreAsteroids != ignoreAsteroids || myOutput == null)
			{
				SetOutput(new PathfinderOutput(PathfinderOutput.Result.Incomplete));
				if (myPathChecker != null)
					myPathChecker.Interrupt = true;
				this.Destination = destination;
				this.NavigationBlock = NavigationBlock;
				this.IgnoreAsteroids = ignoreAsteroids;
			}

			// not a race, this function is only called from one thread
			if (DateTime.UtcNow < nextRun)
				return;
			nextRun = DateTime.MaxValue;

			PathFinderThread.EnqueueAction(Wrapper_CheckPath);
		}

		private void Wrapper_CheckPath()
		{
			try
			{ CheckPath(); }
			catch (InterruptException)
			{ myLogger.debugLog("Caught Interrupt", "Wrapper_CheckPath", Logger.severity.DEBUG); }
			catch (Exception other)
			{ myLogger.debugLog("Exception: " + other, "Wrapper_CheckPath", Logger.severity.ERROR); }
			finally
			{ nextRun = DateTime.UtcNow + new TimeSpan(0, 0, 1); }
		}

		private void CheckPath()
		{
			// path check incomplete, do not replace output - it may be valid

			myLogger.debugLog("testing forward path", "CheckPath()");
			IMyEntity ObstructingEntity = myPathChecker.TestPath(RelativeVector3F.createFromWorldAbsolute(Destination, CubeGrid), NavigationBlock, IgnoreAsteroids);

			if (ObstructingEntity == null)
			{
				myLogger.debugLog("Path forward is clear", "CheckPath()", Logger.severity.DEBUG);
				SetOutput(new PathfinderOutput(PathfinderOutput.Result.Path_Clear));
				return;
			}
			CheckInterrupt();
			SetOutput(new PathfinderOutput(PathfinderOutput.Result.Searching_Alt, ObstructingEntity));
			myLogger.debugLog("Path forward is obstructed by " + ObstructingEntity.getBestName(), "CheckPath()", Logger.severity.TRACE);

			Vector3 obstructionCentre = ObstructingEntity.GetCentre();
			Vector3 lineToObstruction = obstructionCentre - CubeGrid.GetCentre();
			Vector3 newPath_v1, newPath_v2;
			lineToObstruction.CalculatePerpendicularVector(out newPath_v1);
			newPath_v2 = lineToObstruction.Cross(newPath_v1);
			newPath_v1 = Vector3.Normalize(newPath_v1);
			newPath_v2 = Vector3.Normalize(newPath_v2);
			Vector3[] NewPathVectors = { newPath_v1, newPath_v2, Vector3.Negate(newPath_v1), Vector3.Negate(newPath_v2) };

			int newPathMinimum = (int)(ObstructingEntity.WorldVolume.Radius + CubeGrid.WorldVolume.Radius + 10);
			for (int newPathAdd = 0; newPathAdd < 10000; newPathAdd *= 2)
			{
				int newPathDistance = newPathMinimum + newPathAdd;
				for (int whichNewPath = 0; whichNewPath < 4; whichNewPath++)
				{
					Vector3 newPathDestination = obstructionCentre + newPathDistance * NewPathVectors[whichNewPath];
					myLogger.debugLog("testing alternate path: " + newPathDestination, "CheckPath()");
					if (myPathChecker.TestPath(RelativeVector3F.createFromWorldAbsolute(newPathDestination, CubeGrid), NavigationBlock, IgnoreAsteroids) == null) // found a new path
					{
						SetOutput(new PathfinderOutput(PathfinderOutput.Result.Alternate_Path, ObstructingEntity, newPathDestination));
						myLogger.debugLog("Found a new path: " + newPathDestination, "CheckPath()", Logger.severity.DEBUG);
						return;
					}
					CheckInterrupt();
				}
			}
			SetOutput(new PathfinderOutput(PathfinderOutput.Result.No_Way_Forward, ObstructingEntity));
			myLogger.debugLog("No Way Forward: " + ObstructingEntity.getBestName(), "CheckPath()", Logger.severity.INFO);
		}

		#region Interrupt

		/// <summary>
		/// throws an InterruptException if Interrupt
		/// </summary>
		private void CheckInterrupt()
		{
			if (myPathChecker.Interrupt)
				throw new InterruptException();
		}

		#endregion
	}
}
