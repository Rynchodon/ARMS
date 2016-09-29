using System.Text;
using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Rynchodon.Utility.Vectors;
using Rynchodon.Weapons;
using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator
{
	public class Kamikaze : NavigatorMover, IEnemyResponse
	{

		private readonly Logger m_logger;

		private LastSeen m_enemy;
		private bool m_approaching;

		public Kamikaze(Mover mover, AllNavigationSettings navSet)
			: base(mover)
		{
			this.m_logger = new Logger(() => m_controlBlock.CubeGrid.DisplayName);

			m_logger.debugLog("Initialized");
		}

		#region IEnemyResponse Members

		public bool CanRespond()
		{
			return m_mover.Thrust.CanMoveForward();
		}

		public bool CanTarget(IMyCubeGrid grid)
		{
			return true;
		}

		public void UpdateTarget(LastSeen enemy)
		{
			m_enemy = enemy;
			m_navSet.Settings_Task_NavEngage.DestinationEntity = enemy.Entity;
		}

		#endregion

		public override void Move()
		{
			m_logger.debugLog("entered");

			if (m_enemy == null)
			{
				m_mover.StopMove();
				return;
			}

			m_mover.Thrust.Update();
			m_approaching = m_mover.SignificantGravity() && !m_navSet.DistanceLessThan(3000f);
			if (m_approaching)
			{
				m_mover.CalcMove(m_mover.Block.Pseudo, m_enemy.GetPosition(), m_enemy.GetLinearVelocity());
				return;
			}

			Vector3D enemyPosition = m_enemy.GetPosition();
			float myAccel = m_mover.Thrust.GetForceInDirection(Base6Directions.GetClosestDirection(m_mover.Thrust.Standard.LocalMatrix.Forward)) / m_controlBlock.Physics.Mass;
			Vector3 aimDirection;
			Vector3D contactPoint;
			TargetingBase.FindInterceptVector(m_controlBlock.Pseudo.WorldPosition, m_controlBlock.Physics.LinearVelocity, enemyPosition, m_enemy.GetLinearVelocity(), myAccel, true, out aimDirection, out contactPoint);

			m_mover.SetMove(m_controlBlock.Pseudo, contactPoint, ((DirectionWorld)(aimDirection * myAccel)).ToBlock(m_mover.Block.CubeBlock));
		}

		public void Rotate()
		{
			m_logger.debugLog("entered");

			if (m_enemy == null)
			{
				m_mover.StopRotate();
				return;
			}

			if (m_approaching)
			{
				m_logger.debugLog("approaching");
				m_mover.CalcRotate();
			}
			else
			{
				m_logger.debugLog("ramming");
				m_mover.CalcRotate_Accel();
			}
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
