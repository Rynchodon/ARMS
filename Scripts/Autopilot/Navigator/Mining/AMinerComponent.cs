using System;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Pathfinding;
using Rynchodon.Utility;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator.Mining
{
	abstract class AMinerComponent : AMiner
	{
		protected Destination m_target;
		protected string m_oreName;

		protected override MyVoxelBase TargetVoxel { get { return (MyVoxelBase)m_target.Entity; } }
		private Logable Log
		{ get { return LogableFrom.Pseudo(m_navSet.Settings_Current.NavigationBlock); } }

		protected AMinerComponent(Pathfinder pathfinder, string oreName) : base(pathfinder)
		{
			m_oreName = oreName;
		}

		protected void EnableDrills(bool enable)
		{
			if (enable)
				Log.DebugLog("Enabling drills", Logger.severity.DEBUG);
			else
				Log.DebugLog("Disabling drills", Logger.severity.DEBUG);

			CubeGridCache cache = CubeGridCache.GetFor(m_controlBlock.CubeGrid);
			if (cache == null)
			{
				Log.DebugLog("Failed to get cache", Logger.severity.INFO);
				return;
			}

			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				foreach (IMyShipDrill drill in cache.BlocksOfType(typeof(MyObjectBuilder_Drill)))
					if (!drill.Closed)
						((MyFunctionalBlock)drill).Enabled = enable;
			});
		}

		protected bool AbortMining()
		{
			if (DrillFullness() > FullAmount_Abort)
			{
				Log.DebugLog("Drills are full", Logger.severity.DEBUG);
				return true;
			}
			else if (!SufficientAcceleration(MinAccel_Abort))
			{
				Log.DebugLog("Not enough acceleration", Logger.severity.DEBUG);
				return true;
			}
			else if (m_mover.ThrustersOverWorked())
			{
				Log.DebugLog("Thrusters overworked", Logger.severity.DEBUG);
				return true;
			}
			else if (IsStuck)
			{
				Log.DebugLog("Stuck", Logger.severity.DEBUG);
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
				Log.DebugLog("capsule: " + capsule.String() + ", does not intersect voxel", Logger.severity.DEBUG);
				hitPos = capsule.P0;
			}

			//Log.DebugLog((tunnel ? "Tunnel target: " : "Backout target: ") + hitPos, Logger.severity.DEBUG);
			m_target.SetWorld(ref hitPos);
		}

	}
}
