using System;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Pathfinding;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator.Mining
{
	abstract class AMinerComponent : AMiner
	{
		protected Destination m_target;
		protected string m_oreName;

		private readonly Logger m_logger;

		protected override MyVoxelBase TargetVoxel { get { return (MyVoxelBase)m_target.Entity; } }

		protected AMinerComponent(Pathfinder pathfinder, string oreName) : base(pathfinder)
		{
			m_logger = new Logger(m_navSet.Settings_Current.NavigationBlock);
			m_oreName = oreName;
		}

		protected void EnableDrills(bool enable)
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

		protected bool AbortMining()
		{
			if (DrillFullness() > FullAmount_Abort)
			{
				m_logger.debugLog("Drills are full", Logger.severity.DEBUG);
				return true;
			}
			else if (!SufficientAcceleration(MinAccel_Abort))
			{
				m_logger.debugLog("Not enough acceleration", Logger.severity.DEBUG);
				return true;
			}
			else if (m_mover.ThrustersOverWorked())
			{
				m_logger.debugLog("Thrusters overworked", Logger.severity.DEBUG);
				return true;
			}
			else if (IsStuck)
			{
				m_logger.debugLog("Stuck", Logger.severity.DEBUG);
				return true;
			}
			return false;
		}

		protected void SetOutsideTarget(Vector3D direction)
		{
			PseudoBlock navBlock = m_navBlock;

			MyVoxelBase voxel = (MyVoxelBase)m_target.Entity;
			CapsuleD capsule;
			Vector3D offset; Vector3D.Multiply(ref direction, voxel.PositionComp.LocalVolume.Radius * 2d, out offset);
			capsule.P1 = navBlock.WorldPosition;
			Vector3D.Add(ref capsule.P1, ref offset, out capsule.P0);
			capsule.Radius = m_grid.LocalVolume.Radius * 4f;

			Vector3D hitPos;
			if (!CapsuleDExtensions.Intersects(ref capsule, voxel, out hitPos))
			{
				m_logger.debugLog("capsule: " + capsule.String() + ", does not intersect voxel", Logger.severity.DEBUG);
				hitPos = capsule.P0;
			}

			//m_logger.debugLog((tunnel ? "Tunnel target: " : "Backout target: ") + hitPos, Logger.severity.DEBUG);
			m_target.SetWorld(ref hitPos);
		}

	}
}
