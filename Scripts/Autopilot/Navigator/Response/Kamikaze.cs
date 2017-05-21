using System.Text;
using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Pathfinding;
using Rynchodon.Utility;
using Rynchodon.Weapons;
using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator
{
	public class Kamikaze : NavigatorMover, IEnemyResponse
	{
		private LastSeen m_enemy;
		private bool m_approaching;

		private Logable Log { get { return new Logable(m_controlBlock.CubeGrid); } }

		public Kamikaze(Pathfinder pathfinder, AllNavigationSettings navSet) : base(pathfinder) { }

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
			m_navSet.Settings_Task_NavEngage.DestinationEntity = enemy?.Entity;
		}

		#endregion

		public override void Move()
		{
			//Log.DebugLog("entered");

			if (m_enemy == null)
			{
				m_mover.StopMove();
				return;
			}

			m_mover.Thrust.Update();
			m_approaching = m_mover.SignificantGravity() && !m_navSet.DistanceLessThan(3000f);
			if (m_approaching)
			{
				m_pathfinder.MoveTo(m_enemy);
				return;
			}

			float myAccel = m_mover.Thrust.GetForceInDirection(Base6Directions.GetClosestDirection(m_mover.Thrust.Standard.LocalMatrix.Forward)) * Movement.Mover.AvailableForceRatio / m_controlBlock.Physics.Mass;

			Vector3D enemyPosition = m_enemy.GetPosition();
			Vector3 aimDirection;
			Vector3D contactPoint;
			TargetingBase.FindInterceptVector(m_controlBlock.Pseudo.WorldPosition, m_controlBlock.Physics.LinearVelocity, enemyPosition, m_enemy.GetLinearVelocity(), myAccel, true, out aimDirection, out contactPoint);
			Vector3 aimVelo; Vector3.Multiply(ref aimDirection, myAccel, out aimVelo);

			Vector3 linearVelocity = m_controlBlock.Physics.LinearVelocity;
			Vector3 addToVelocity; Vector3.Add(ref linearVelocity, ref aimVelo, out addToVelocity);

			m_pathfinder.MoveTo(m_enemy, addToVelocity: addToVelocity);
		}

		public void Rotate()
		{
			//Log.DebugLog("entered");

			if (m_enemy == null)
			{
				m_mover.StopRotate();
				return;
			}

			if (m_approaching)
			{
				//Log.DebugLog("approaching");
				m_mover.CalcRotate();
			}
			else
			{
				//Log.DebugLog("ramming");
				if (m_navSet.DistanceLessThan(100f))
				{
					// just before impact, face the target
					Vector3 targetDirection = m_enemy.GetPosition() - m_controlBlock.CubeBlock.GetPosition();
					targetDirection.Normalize();
					m_mover.CalcRotate(m_mover.Thrust.Standard, RelativeDirection3F.FromWorld(m_mover.Block.CubeGrid, targetDirection));
				}
				else
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
