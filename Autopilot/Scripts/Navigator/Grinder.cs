using System.Collections.Generic;
using System.Text;
using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
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

		//private Vector3D m_targetCentre, m_targetPosition;
		private Vector3D m_targetPosition;
		private IMyEntity m_target;

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
		}

		~Grinder()
		{
			try { EnableGrinders(false); }
			catch { }
		}

		#region IEnemyResponse Members

		public bool CanRespond()
		{
			return m_mover.CanMoveForward(m_mover.Block.Pseudo) && m_navGrind.FunctionalBlocks != 0;
		}

		public bool CanTarget(IMyCubeGrid grid)
		{
			return true;
		}

		public void UpdateTarget(LastSeen enemy)
		{
			if (enemy == null )
			{
				m_target = null;
				EnableGrinders(false);
				return;
			}
			EnableGrinders(true);
			m_navSet.Settings_Task_NavEngage.DestinationEntity = enemy.Entity;
			m_target = enemy.Entity;
			IMyCubeGrid grid = m_target as IMyCubeGrid;

			Vector3I grindOnTarget = grid.WorldToGridInteger(m_navGrind.WorldPosition);
			Vector3I gridMin = Vector3I.Round(grid.LocalAABB.Min / grid.GridSize);
			Vector3I gridMax = Vector3I.Round(grid.LocalAABB.Max / grid.GridSize);
			Vector3I clampedGrindOnTarget;
			Vector3I.Clamp(ref grindOnTarget, ref gridMin, ref gridMax, out clampedGrindOnTarget);

			Vector3I closestBlock;
			if (!GridCellCache.GetCellCache(grid).GetClosestOccupiedCell(clampedGrindOnTarget, out closestBlock))
			{
				m_logger.alwaysLog("No closest block found", "UpdateTarget()", Logger.severity.FATAL);
				return;
			}

			m_targetPosition = grid.GridIntegerToWorld(closestBlock);

			Vector3 blockToGrinder = m_navGrind.WorldPosition - m_targetPosition;
			blockToGrinder.Normalize();
			m_targetPosition += blockToGrinder * 1.5f;

			//m_targetCentre = enemy.Entity.Physics.CenterOfMassWorld;
			//IMyCubeGrid targetGrid = enemy.Entity as IMyCubeGrid;
			//Vector3I? hit = targetGrid.RayCastBlocks(m_navGrind.WorldPosition, m_targetCentre);
			//Vector3 toGrinderFromTarget = m_navGrind.WorldPosition - m_targetCentre;
			//toGrinderFromTarget.Normalize();
			//m_logger.debugLog("ray cast from " + m_navGrind.WorldPosition.ToGpsTag("grinder position") + " to " + m_targetCentre.ToGpsTag("target position"), "UpdateTarget()");
			//if (hit.HasValue)
			//{
			//	m_targetPosition = targetGrid.GridIntegerToWorld(hit.Value);
			//	m_logger.debugLog("Ray cast hit " + targetGrid.GetCubeBlock(hit.Value).getBestName() + " at " + m_targetPosition.ToGpsTag("hit position"), "UpdateTarget()");
			//}
			//else
			//{
			//	m_targetPosition = m_targetCentre;
			//	m_logger.debugLog("Ray cast did not hit any blocks, using CoM: " + m_targetPosition.ToGpsTag("centre of mass"), "UpdateTarget()");
			//}
			//m_targetPosition += toGrinderFromTarget * 1.5f;
			//m_logger.debugLog("final target: " + m_targetPosition, "UpdateTarget()");
		}

		#endregion

		public override void Move()
		{
			if (m_target == null)
			{
				m_mover.StopMove();
				return;
			}

			m_logger.debugLog("moving to " + m_targetPosition, "Move()");
			m_mover.CalcMove(m_navGrind, m_targetPosition, m_target.GetLinearVelocity(), true);
		}

		public void Rotate()
		{
			if (m_target == null)
			{
				m_mover.StopRotate();
				return;
			}

			//float distance = m_navSet.Settings_Current.Distance;
			//if (distance < 3f)
			//	m_mover.StopRotate();
			//else
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

	}
}
