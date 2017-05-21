using System.Text;
using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Pathfinding;
using Rynchodon.Utility;
using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator
{
	/// <summary>
	/// Screams and runs away.
	/// </summary>
	public class Coward : NavigatorMover, IEnemyResponse
	{
		private LastSeen m_enemy;

		private Logable Log
		{
			get { return new Logable(m_controlBlock.CubeGrid); }
		}

		public Coward(Pathfinder pathfinder, AllNavigationSettings navSet)
			: base(pathfinder)
		{
			Log.DebugLog("Initialized");
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
			Log.DebugLog("entered");

			if (m_enemy == null)
			{
				m_mover.StopMove();
				return;
			}

			Vector3D position = m_mover.Block.CubeBlock.GetPosition();
			Vector3D flyDirection = position - m_enemy.GetPosition();
			flyDirection.Normalize();

			Destination destination = new Destination(position + flyDirection * 1e6);
			m_pathfinder.MoveTo(destinations: destination);
		}

		public void Rotate()
		{
			Log.DebugLog("entered");
			
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
