// skip file on build

using System.Text;
using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage;
using VRage.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Autopilot.Navigator
{

	/*
	 * TODO:
	 * verify that it works correctly for large ship
	 * get in front of moving target
	 */

	public class Grinder : NavigatorMover, IEnemyResponse
	{

		private readonly Logger m_logger;
		private readonly MultiBlock<MyObjectBuilder_ShipGrinder> m_navGrind;
		private readonly float m_grinderOffset;

		private Vector3D m_targetPosition;
		private IMyEntity m_target;
		private ulong m_next_grinderFullCheck;
		private bool m_grinderFull;

		public Grinder(Mover mover, AllNavigationSettings navSet)
			: base(mover, navSet)
		{
			this.m_logger = new Logger(GetType().Name, () => m_controlBlock.CubeGrid.DisplayName);

			PseudoBlock navBlock = m_navSet.Settings_Current.NavigationBlock;
			m_navGrind = navBlock.Block is Ingame.IMyShipGrinder
				? new MultiBlock<MyObjectBuilder_ShipGrinder>(navBlock.Block)
				: new MultiBlock<MyObjectBuilder_ShipGrinder>(m_mover.Block.CubeGrid);

			if (m_navGrind.FunctionalBlocks == 0)
			{
				m_logger.debugLog("no working drills", "Grinder()", Logger.severity.INFO);
				return;
			}

			m_grinderOffset = m_navGrind.Block.GetLengthInDirection(m_navGrind.Block.GetFaceDirection()[0]) * 0.5f;
			m_logger.debugLog("m_grinderOffset: " + m_grinderOffset, "Grinder()");
		}

		~Grinder()
		{
			try { EnableGrinders(false); }
			catch { }
		}

		#region IEnemyResponse Members

		public bool CanRespond()
		{
			return m_mover.CanMoveForward(m_mover.Block.Pseudo) && m_navGrind.FunctionalBlocks != 0 && !GrinderFull();
		}

		public bool CanTarget(IMyCubeGrid grid)
		{
			return true;
		}

		public void UpdateTarget(LastSeen enemy)
		{
			if (enemy == null)
			{
				if (m_target != null)
				{
					EnableGrinders(false);
					m_target = null;
				}
			}
			else if (enemy.Entity != m_target)
			{
				EnableGrinders(true);
				m_navSet.Settings_Task_NavEngage.DestinationEntity = enemy.Entity;
				m_target = enemy.Entity;
			}
		}

		#endregion

		public override void Move()
		{
			if (m_target == null)
			{
				m_mover.StopMove();
				return;
			}

			IMyCubeGrid grid = m_target as IMyCubeGrid;

			m_targetPosition = GridCellCache.GetCellCache(grid).GetClosestOccupiedCell(m_navGrind.WorldPosition);
				
			Vector3 blockToGrinder = m_navGrind.WorldPosition - m_targetPosition;
			blockToGrinder.Normalize();

			Vector3D destination = m_targetPosition + blockToGrinder * (m_grinderOffset + grid.GridSize * 0.75f);
			m_logger.debugLog("total offset: " + (m_grinderOffset + grid.GridSize), "Move()");

			m_logger.debugLog("grind pos: " + m_navGrind.WorldPosition + ", moving to " + destination, "Move()");
			m_mover.CalcMove(m_navGrind, destination, m_target.GetLinearVelocity(), true);
		}

		public void Rotate()
		{
			if (m_target == null)
			{
				m_mover.StopRotate();
				return;
			}

			m_mover.CalcRotate(m_navGrind, RelativeDirection3F.FromWorld(m_navGrind.Grid, m_targetPosition - m_navGrind.WorldPosition));
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			customInfo.AppendLine("Reducing an enemy to its constituent parts");
		}

		private void EnableGrinders(bool enable)
		{
			if (enable)
				m_logger.debugLog("enabling grinders", "EnableGrinders()", Logger.severity.DEBUG);
			else
				m_logger.debugLog("disabling grinders", "EnableGrinders()", Logger.severity.DEBUG);

			var allGrinders = CubeGridCache.GetFor(m_controlBlock.CubeGrid).GetBlocksOfType(typeof(MyObjectBuilder_ShipGrinder));
			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				foreach (Ingame.IMyShipGrinder grinder in allGrinders)
					if (!grinder.Closed)
						grinder.RequestEnable(enable);
			}, m_logger);
		}

		private bool GrinderFull()
		{
			if (Globals.UpdateCount < m_next_grinderFullCheck)
				return m_grinderFull;
			m_next_grinderFullCheck = Globals.UpdateCount + 100ul;

			MyFixedPoint content = 0, capacity = 0;
			int grinderCount = 0;
			var allGrinders = CubeGridCache.GetFor(m_controlBlock.CubeGrid).GetBlocksOfType(typeof(MyObjectBuilder_ShipGrinder));
			if (allGrinders == null)
				return true;

			foreach (Ingame.IMyShipGrinder grinder in allGrinders)
			{
				IMyInventory grinderInventory = (IMyInventory)Ingame.TerminalBlockExtentions.GetInventory(grinder, 0);
				content += grinderInventory.CurrentVolume;
				capacity += grinderInventory.MaxVolume;
				grinderCount++;
			}

			m_grinderFull = (float)content / (float)capacity >= 0.9f;
			return m_grinderFull;
		}

	}
}
