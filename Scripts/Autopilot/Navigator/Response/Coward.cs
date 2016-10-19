using System.Text;
using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Pathfinding;
using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator
{
	/// <summary>
	/// Screams and runs away.
	/// </summary>
	public class Coward : NavigatorMover, IEnemyResponse
	{

		private readonly Logger m_logger;

		private LastSeen m_enemy;

		public Coward(Pathfinder pathfinder, AllNavigationSettings navSet)
			: base(pathfinder)
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

			Vector3D position = m_mover.Block.CubeBlock.GetPosition();
			Vector3D flyDirection = position - m_enemy.GetPosition();
			flyDirection.Normalize();

			Destination destination = new Destination(position + flyDirection * 1e6);
			m_pathfinder.MoveTo(m_mover.Block.Pseudo, ref destination);
		}

		public void Rotate()
		{
			m_logger.debugLog("entered");
			
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
				customInfo.Append("Running like a coward from an enemy at ");
				customInfo.AppendLine(m_enemy.GetPosition().ToPretty());
			}
		}

	}
}
