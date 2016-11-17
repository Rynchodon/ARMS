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
	abstract class AMiner : NavigatorMover, INavigatorRotator
	{
		public const float FullAmount_Abort = 0.9f, FullAmount_Return = 0.1f;
		public const float MinAccel_Abort = 0.75f, MinAccel_Return = 1f;

		private ulong m_nextCheck_drillFull;
		private float m_current_drillFull;

		protected abstract MyVoxelBase TargetVoxel { get;}

		public AMiner(Pathfinder pathfinder) : base(pathfinder) { }

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
				Logger.DebugLog("Failed to get cache", Logger.severity.INFO);
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
			return (TargetVoxel).ContainsOrIntersects(ref surround);
		}

		public abstract void Rotate();
	}
}
