#define LOG_ENABLED //remove on build

using System;
using Rynchodon.AntennaRelay;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.NavigationSettings
{
	public class GridDestination
	{
		private Navigator myNav;
		private IMyEntity Entity;
		public LastSeen gridLastSeen { get { return seenBy.getLastSeen(Entity.EntityId); } }

		private ShipController seenBy;
		public IMyCubeGrid Grid { get; private set; }
		public IMyCubeBlock Block { get; private set; }
		public Vector3D? Offset { get; set; }

		private Logger myLogger;

		public GridDestination(LastSeen gridLastSeen, IMyCubeBlock destBlock, IMyCubeBlock seenBy, Navigator owner)
		{
			myLogger = new Logger("GridDestination");

			this.myNav = owner;
			this.Entity = gridLastSeen.Entity;
			if (!ShipController.TryGet(seenBy, out this.seenBy))
				myLogger.alwaysLog("failed to get ARShipController", ".ctor()", Logger.severity.ERROR);
			this.Grid = gridLastSeen.Entity as IMyCubeGrid;
			if (Grid == null)
				myLogger.alwaysLog("Entity is not a grid", "GridDestination()", Logger.severity.FATAL);
			this.Block = destBlock;

			if (Block != null)
				myLogger = new Logger("GridDestination", () => this.Grid.DisplayName, () => this.Block.DisplayNameText);
			else
				myLogger = new Logger("GridDestination", () => this.Grid.DisplayName);
		}

		public bool seenRecently()
		{ return (DateTime.UtcNow - gridLastSeen.LastSeenAt).TotalSeconds < 10; }

		/// <summary>
		/// if seen within 10seconds, get the actual position. otherwise, use LastSeen.predictPosition
		/// </summary>
		public Vector3D GetGridPos()
		{
			if (Grid == null)
				throw new NullReferenceException("Grid");
			if (Grid.Closed)
				throw new NullReferenceException("Grid is closed");

			return calculateInterceptionPoint(false);
		}

		/// <summary>
		/// if seen within 10seconds, get the actual position. otherwise, use LastSeen.predictPosition
		/// </summary>
		public Vector3D GetBlockPos()
		{
			if (Grid == null)
				throw new NullReferenceException("Grid");
			if (Grid.Closed)
				throw new NullReferenceException("Grid is closed");
			if (Block == null)
				throw new NullReferenceException("Block");
			if (Block.Closed)
				throw new NullReferenceException("Block is closed");

			return calculateInterceptionPoint(true);
		}

		/// <summary>
		/// Checks if this GridDestination has a valid grid.
		/// </summary>
		public bool ValidGrid()
		{ return Grid != null && !Grid.Closed; }

		/// <summary>
		/// Checks if this GridDestination has a valid block.
		/// </summary>
		public bool ValidBlock()
		{ return Block != null && !Block.Closed; }

		private Vector3D calculateInterceptionPoint(bool fromBlock)
		{
			//myLogger.debugLog("entered calculateInterceptionPoint(" + block + ")", "calculateInterceptionPoint()");
			Vector3D targetPosition, targetVelocity, targetAcceleration;
			if (seenRecently())
			{
				if (fromBlock)
				{
					//if (Offset.HasValue)
					//	targetPosition = RelativePosition3F.FromBlock(Block, Offset.Value).ToWorld();
					//else
					targetPosition = Block.GetPosition();
					if (Offset.HasValue)
						targetPosition += Offset.Value;
					myLogger.debugLog("block is at " + targetPosition + ", grid is at " + Grid.WorldAABB.Center + ", Offset = " + Offset, "calculateInterceptionPoint()", Logger.severity.TRACE);
				}
				else
				{
					//if (Offset.HasValue)
					//	targetPosition = RelativePosition3F.FromLocal(Grid, Offset.Value).ToWorld();
					//else
					targetPosition = Grid.WorldAABB.Center;
					if (Offset.HasValue)
						targetPosition += Offset.Value;
					myLogger.debugLog("grid is at " + targetPosition + ", Offset = " + Offset, "calculateInterceptionPoint()", Logger.severity.TRACE);
				}

				targetVelocity = Grid.Physics.LinearVelocity;
				targetAcceleration = Grid.Physics.GetLinearAcceleration();
			}
			else
			{
				myLogger.debugLog("not seen recently " + (DateTime.UtcNow - gridLastSeen.LastSeenAt).TotalSeconds + " seconds since last seen", "calculateInterceptionPoint()", Logger.severity.TRACE);
				targetPosition = gridLastSeen.predictPosition();
				targetVelocity = gridLastSeen.LastKnownVelocity;
				targetAcceleration = Vector3.Zero;
			}
			if (targetVelocity == Vector3D.Zero)
			{
				//myLogger.debugLog("shorting: target velocity is zero. position = " + targetPosition, "calculateInterceptionPoint()", Logger.severity.TRACE);
				return targetPosition;
			}

			Vector3D targetToMe = myNav.getNavigationBlock().GetPosition() - targetPosition;

			double distanceTo_PathOfTarget = Vector3D.Normalize(targetVelocity).Cross(targetToMe).Length();

			double mySpeed = Math.Max(myNav.myGrid.Physics.LinearVelocity.Length(), 1);
			double myAccel = myNav.myGrid.Physics.GetLinearAcceleration().Length();

			double secondsToTarget = distanceTo_PathOfTarget / mySpeed;

			return targetPosition + targetVelocity * secondsToTarget + targetAcceleration * secondsToTarget * secondsToTarget / 2;
		}
	}
}
