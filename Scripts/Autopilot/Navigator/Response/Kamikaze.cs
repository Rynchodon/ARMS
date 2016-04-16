using System.Text;
using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator
{
	public class Kamikaze : NavigatorMover, IEnemyResponse
	{

		private readonly Logger m_logger;

		private LastSeen m_enemy;
		private Vector3D m_flyDirection;

		public Kamikaze(Mover mover, AllNavigationSettings navSet)
			: base(mover, navSet)
		{
			this.m_logger = new Logger(GetType().Name, () => m_controlBlock.CubeGrid.DisplayName);

			m_logger.debugLog("Initialized", "Kamikaze()");
		}

		#region IEnemyResponse Members

		public bool CanRespond()
		{
			return m_mover.CanMoveForward();
		}

		public bool CanTarget(IMyCubeGrid grid)
		{
			return true;
		}

		public void UpdateTarget(LastSeen enemy)
		{
			m_enemy = enemy;
			m_navSet.Settings_Task_NavEngage.CollisionAvoidance = false;
		}

		#endregion

		public override void Move()
		{
			m_logger.debugLog("entered", "Move()");

			if (m_enemy == null)
			{
				m_mover.StopMove();
				return;
			}

			Vector3 displacement = m_enemy.GetPosition() - m_mover.Block.CubeBlock.GetPosition();
			float distance = displacement.Length();
			Vector3 direction; Vector3.Divide(ref displacement, distance, out direction);
			Vector3 velocity = m_enemy.GetLinearVelocity() - m_mover.Block.CubeBlock.GetLinearVelocity();
			Vector3 tangVel; Vector3.Reject(ref velocity, ref direction, out tangVel);

			m_mover.CalcMove(m_mover.Block.Pseudo, m_enemy.GetPosition(), m_enemy.GetLinearVelocity() + direction * m_navSet.Settings_Task_NavEngage.SpeedTarget + tangVel * 3f);
		}

		public void Rotate()
		{
			m_logger.debugLog("entered", "Rotate()");

			if (m_enemy == null)
			{
				m_mover.StopRotate();
				return;
			}

			m_mover.CalcRotate();
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			if (m_enemy != null)
			{
				customInfo.AppendLine("Ramming an enemy at ");
				customInfo.AppendLine(m_enemy.GetPosition().ToPretty());
			}
		}

	}
}
