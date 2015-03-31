// skip file on build

#define DEBUG // remove on build

using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.ModAPI;
using VRage;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinder
{
	public class CollisionBox
	{
		private Logger myLogger;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ alwaysLog(toLog, method, level); }
		private void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null) myLogger = new Logger(myGrid.DisplayName, "CollisionBox");
			myLogger.log(level, method, toLog);
		}

		public class Feedback
		{
			readonly bool PathClear;
			readonly bool HasWaypoint;
			readonly Vector3D Waypoint;

			Feedback(bool pathClear = false, Vector3D waypoint = new Vector3D())
			{
				this.PathClear = pathClear;
				this.HasWaypoint = Vector3D.IsValid(waypoint);
				this.Waypoint = waypoint;
			}
		}

		private Locked<Feedback> locked_currentFeedback = new Locked<Feedback>();
		public Feedback currentFeedback
		{
			get { return locked_currentFeedback.Value; }
			private set { locked_currentFeedback.Value = value; }
		}

		// fields filled before separate thread
		private IMyCubeGrid myGrid;

		public void startCollision()
		{
			if (!lock_runCollision.TryAcquireExclusive()) // already running
				return;
			try
			{
				// fill variables

				MyAPIGateway.Parallel.Start(runCollision); // start collision thread
			}
			finally
			{
				lock_runCollision.ReleaseExclusive();
			}
		}

		//fields filled by separate thread
		private List<IMyEntity> avoidanceEntities;

		private FastResourceLock lock_runCollision = new FastResourceLock();
		private void runCollision()
		{
			lock_runCollision.AcquireExclusive();
			try
			{
				DateTime start = DateTime.UtcNow;

				// do collision avoidance stuff
				HashSet<IMyEntity> entitiesHash = new HashSet<IMyEntity>();
				MyAPIGateway.Entities.GetEntities(entitiesHash, collect_entities);
				avoidanceEntities = new List<IMyEntity>(entitiesHash);

				log("time to run = " + (DateTime.UtcNow - start).TotalMilliseconds, "runCollision()", Logger.severity.DEBUG);
			}
			catch (Exception e)
			{
				alwaysLog("Exception: " + e, "runCollision()", Logger.severity.ERROR);
			}
			finally
			{
				lock_runCollision.ReleaseExclusive();
			}
		}

		private bool collect_entities(IMyEntity entity)
		{
			if (entity is IMyCharacter ||
				entity == myGrid ||
				(entity.Physics != null && entity.Physics.Mass < 1000))
				return false;

			IMyCubeGrid entityAsGrid = entity as IMyCubeGrid;
			if (entityAsGrid != null && AttachedGrids.isGridAttached(myGrid, entityAsGrid))
				return false;

			return true;
		}
	}
}
