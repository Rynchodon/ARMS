using System;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Pathfinding;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.Entity;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator.Mining
{
	abstract class AMinerComponent : NavigatorMover, INavigatorRotator
	{
		public const float FullAmount_Abort = 0.9f, FullAmount_Return = 0.1f;
		public const float MinAccel_Abort = 0.75f, MinAccel_Return = 1f;

		protected Destination m_target;

		private readonly Logger m_logger;
		private ulong m_nextCheck_drillFull;
		private float m_current_drillFull;

		protected AMinerComponent(Pathfinder pathfinder) : base(pathfinder)
		{
			m_logger = new Logger(m_navSet.Settings_Current.NavigationBlock);
		}

		protected bool IsStuck { get { return m_pathfinder.CurrentState == Pathfinder.State.FailedToFindPath || m_mover.MoveStuck; } }

		/// <summary>
		/// <para>In survival, returns fraction of drills filled</para>
		/// <para>In creative, returns content per drill * 0.01</para>
		/// </summary>
		protected float DrillFullness()
		{
			if (Globals.UpdateCount < m_nextCheck_drillFull)
				return m_current_drillFull;
			m_nextCheck_drillFull = Globals.UpdateCount + 100ul;

			MyFixedPoint content = 0, capacity = 0;
			int drillCount = 0;

			var cache = CubeGridCache.GetFor(m_controlBlock.CubeGrid);
			if (cache == null)
			{
				m_logger.debugLog("Failed to get cache", Logger.severity.INFO);
				return float.MaxValue;
			}

			foreach (IMyShipDrill drill in cache.BlocksOfType(typeof(MyObjectBuilder_Drill)))
			{
				MyInventoryBase drillInventory = ((MyEntity)drill).GetInventoryBase(0);

				content += drillInventory.CurrentVolume;
				capacity += drillInventory.MaxVolume;
				drillCount++;
			}

			if (drillCount == 0)
				m_current_drillFull = float.MaxValue;
			else if (MyAPIGateway.Session.CreativeMode)
				m_current_drillFull = (float)content * 0.01f / drillCount;
			else
				m_current_drillFull = (float)content / (float)capacity;

			return m_current_drillFull;
		}

		/// <summary>
		/// Checks for enough acceleration to move the ship forward and backward with the specified acceleration.
		/// </summary>
		protected bool SufficientAcceleration(float acceleration)
		{
			PseudoBlock navBlock = m_navSet.Settings_Current.NavigationBlock;
			return m_mover.Thrust.CanMoveDirection(Base6Directions.GetClosestDirection(navBlock.LocalMatrix.Forward), acceleration) &&
				m_mover.Thrust.CanMoveDirection(Base6Directions.GetClosestDirection(navBlock.LocalMatrix.Backward), acceleration);
		}

		protected bool IsNearVoxel(double lengthMulti = 1d)
		{
			BoundingSphereD surround = new BoundingSphereD(m_grid.GetCentre(), m_grid.LocalVolume.Radius);
			return ((MyVoxelBase)m_target.Entity).Intersects(ref surround);
		}

		protected void EnableDrills(bool enable, bool force = false)
		{
			if (enable)
				m_logger.debugLog("Enabling drills", Logger.severity.DEBUG);
			else
				m_logger.debugLog("Disabling drills", Logger.severity.DEBUG);

			CubeGridCache cache = CubeGridCache.GetFor(m_controlBlock.CubeGrid);
			if (cache == null)
			{
				m_logger.debugLog("Failed to get cache", Logger.severity.INFO);
				return;
			}

			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				foreach (IMyShipDrill drill in cache.BlocksOfType(typeof(MyObjectBuilder_Drill)))
					if (!drill.Closed)
						drill.RequestEnable(enable);
			});
		}

		protected bool BackoutTarget()
		{
			if (m_navSet.DistanceLessThan(1f))
			{
				m_logger.debugLog("Reached position: " + m_target, Logger.severity.WARNING);
				m_target.SetWorld(m_target.WorldPosition() + m_navBlock.WorldMatrix.Backward * 100d);
				return true;
			}
			else if (IsStuck)
			{
				m_logger.debugLog("Stuck", Logger.severity.DEBUG);
				return false;
			}
			else
			{
				m_pathfinder.MoveTo(destinations: m_target);
				return true;
			}
		}

		protected void SetOutsideTarget(bool tunnel)
		{
			PseudoBlock navBlock = m_navBlock;
			Vector3D direction = tunnel ? navBlock.WorldMatrix.Forward : navBlock.WorldMatrix.Backward;

			MyVoxelBase voxel = (MyVoxelBase)m_target.Entity;
			CapsuleD capsule;
			Vector3D.Multiply(ref direction, voxel.PositionComp.LocalVolume.Radius * 2d, out capsule.P0);
			capsule.P1 = navBlock.WorldPosition;
			capsule.Radius = m_grid.LocalVolume.Radius * 4f;

			Vector3D hitPos;
			if (!CapsuleDExtensions.Intersects(ref capsule, voxel, out hitPos))
				throw new Exception("Failed to intersect voxel");

			m_logger.debugLog((tunnel ? "Tunnel target: " : "Backout target: ") + hitPos);
			m_target.SetWorld(ref hitPos);
		}

		public abstract void Rotate();

	}
}
