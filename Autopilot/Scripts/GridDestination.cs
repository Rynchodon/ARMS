#define LOG_ENABLED

using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.ModAPI;
using VRageMath;

using Rynchodon.AntennaRelay;

namespace Rynchodon.Autopilot
{
	public class GridDestination
	{
		private Navigator myNav;
		private IMyEntity Entity;
		public LastSeen gridLastSeen { get { return seenBy.getLastSeen(Entity.EntityId); } }

		//public LastSeen gridLastSeen { get; private set; }
		private RemoteControl seenBy;
		public IMyCubeGrid Grid { get; private set; }
		public IMyCubeBlock Block { get; private set; }

		public GridDestination(LastSeen gridLastSeen, IMyCubeBlock destBlock, IMyCubeBlock seenBy, Navigator owner)
		{
			this.myNav = owner;
			this.Entity = gridLastSeen.Entity;
			//this.gridLastSeen = gridLastSeen;
			if (!RemoteControl.TryGet(seenBy, out this.seenBy))
				alwaysLog("failed to get ARRemoteControl", ".ctor()", Logger.severity.ERROR);
			this.Grid = gridLastSeen.Entity as IMyCubeGrid;
			if (Grid == null)
				(new Logger(null, "GridDestination")).log(Logger.severity.FATAL, ".ctor()", "Entity is not a grid");
			this.Block = destBlock;
		}

		public bool seenRecently()
		{ return (DateTime.UtcNow - gridLastSeen.LastSeenAt).TotalSeconds < 10; }

		/// <summary>
		/// if seen within 10seconds, get the actual position. otherwise, use LastSeen.predictPosition
		/// </summary>
		/// <returns></returns>
		public Vector3D GetGridPos()
		{
			return calculateInterceptionPoint(false);
			//if (myNav.CNS.isAMissile)
			//	return calculateInterceptionPoint(false);

			//if (seenRecently())
			//{
			//	log("seen recently(" + (DateTime.UtcNow - gridLastSeen.LastSeenAt).TotalSeconds + "), using actual grid centre: " + Grid.WorldAABB.Center, "GetGridPos()", Logger.severity.TRACE);
			//	return Grid.WorldAABB.Center;
			//}
			//log("it has been a while(" + (DateTime.UtcNow - gridLastSeen.LastSeenAt).TotalSeconds + "), using prediction " + gridLastSeen.predictPosition(), "GetGridPos()", Logger.severity.TRACE);
			//return gridLastSeen.predictPosition();
		}

		/// <summary>
		/// if seen within 10seconds, get the actual position. otherwise, use LastSeen.predictPosition
		/// </summary>
		/// <returns></returns>
		public Vector3D GetBlockPos()
		{
			return calculateInterceptionPoint(true);
			//if (myNav.CNS.isAMissile)
			//	return calculateInterceptionPoint(true);

			//if (seenRecently())
			//{
			//	log("seen recently(" + (DateTime.UtcNow - gridLastSeen.LastSeenAt).TotalSeconds + "), using actual block position: " + Block.GetPosition(), "GetBlockPos()", Logger.severity.TRACE);
			//	return Block.GetPosition();
			//}
			//else
			//{
			//	log("it has been a while(" + (DateTime.UtcNow - gridLastSeen.LastSeenAt).TotalSeconds + "), using prediction " + gridLastSeen.predictPosition(), "GetBlockPos()", Logger.severity.TRACE);
			//	return gridLastSeen.predictPosition();
			//}
		}

		private Vector3D calculateInterceptionPoint(bool block)
		{
			//log("entered calculateInterceptionPoint(" + block + ")", "calculateInterceptionPoint()");
			Vector3D targetPosition, targetVelocity, targetAcceleration;
			if (seenRecently())
			{
				if (block)
				{
					targetPosition = Block.GetPosition();
					//log("block is at " + targetPosition + ", grid is at " + Grid.WorldAABB.Center, "calculateInterceptionPoint()", Logger.severity.TRACE);
				}
				else
					targetPosition = Grid.WorldAABB.Center;
				//log("grid is at " + Grid.WorldAABB.Center, "calculateInterceptionPoint()", Logger.severity.TRACE);
				targetVelocity = Grid.Physics.LinearVelocity;
				targetAcceleration = Grid.Physics.GetLinearAcceleration();
			}
			else
			{
				//log("not seen recently " + (DateTime.UtcNow - gridLastSeen.LastSeenAt).TotalSeconds+" seconds since last seen", "calculateInterceptionPoint()", Logger.severity.TRACE);
				targetPosition = gridLastSeen.predictPosition();
				targetVelocity = gridLastSeen.LastKnownVelocity;
				targetAcceleration = Vector3.Zero;
			}
			if (targetVelocity == Vector3D.Zero)
			{
				//log("shorting: target velocity is zero. position = " + targetPosition, "calculateInterceptionPoint()", Logger.severity.TRACE);
				return targetPosition;
			}

			Vector3D targetToMe = myNav.getNavigationBlock().GetPosition() - targetPosition;

			double distanceTo_PathOfTarget = Vector3D.Normalize(targetVelocity).Cross(targetToMe).Length();

			double mySpeed = Math.Max(myNav.myGrid.Physics.LinearVelocity.Length(), 1);
			double myAccel = myNav.myGrid.Physics.GetLinearAcceleration().Length();

			double secondsToTarget = distanceTo_PathOfTarget / mySpeed;
			
			return targetPosition + targetVelocity * secondsToTarget + targetAcceleration * secondsToTarget * secondsToTarget / 2;
		}


		private Logger myLogger;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ alwaysLog(toLog, method, level); }
		private void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null)
			{
				if (seenBy == null || Grid == null || Block == null)
				{
					(new Logger(null, "GridDestination")).log(level, method, toLog);
					return;
				}
				myLogger = new Logger((seenBy.Entity as IMyCubeBlock).CubeGrid.DisplayName, "GridDestination");
			}
			myLogger.log(level, method, toLog, Grid.DisplayName, Block.DisplayNameText);
		}
	}
}
