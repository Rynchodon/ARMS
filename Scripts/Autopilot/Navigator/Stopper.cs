using System.Text;
using Rynchodon.Autopilot.Pathfinding;

namespace Rynchodon.Autopilot.Navigator
{
	/// <summary>
	/// <para>Stops the ship</para>
	/// </summary>
	public class Stopper : NavigatorMover, INavigatorRotator
	{

		private readonly Logger _logger;
		private readonly bool m_exitAfter;

		private float m_lastLinearSpeedSquared = float.MaxValue, m_lastAngularSpeedSquared = float.MaxValue;

		/// <summary>
		/// Creates a new Stopper
		/// </summary>
		/// <param name="pathfinder">The Mover to use</param>
		/// <param name="navSet">The settings to use</param>
		public Stopper(Pathfinder pathfinder, bool exitAfter = false)
			: base(pathfinder)
		{
			_logger = new Logger(m_controlBlock.Controller);
			m_exitAfter = exitAfter;

			m_mover.MoveAndRotateStop();

			m_navSet.Settings_Task_NavRot.NavigatorMover = this;
		}

		/// <summary>
		/// Determines whether or not the ship is slowing in linear speed.
		/// </summary>
		private bool LinearSlowdown()
		{
			if (m_lastLinearSpeedSquared == 0f)
				return false;

			float linearSpeedSquared = m_mover.Block.Physics.LinearVelocity.LengthSquared();
			if (!m_exitAfter && linearSpeedSquared < 0.01f || m_lastLinearSpeedSquared <= linearSpeedSquared)
			{
				m_lastLinearSpeedSquared = 0f;
				return false;
			}

			m_lastLinearSpeedSquared = linearSpeedSquared;
			return true;
		}

		/// <summary>
		/// Determines whether or not the ship is slowing in angular speed.
		/// </summary>
		private bool AngularSlowdown()
		{
			if (m_lastAngularSpeedSquared == 0f)
				return false;

			float angularSpeedSquared = m_mover.Block.Physics.AngularVelocity.LengthSquared();
			if (!m_exitAfter && angularSpeedSquared < 0.01f || m_lastAngularSpeedSquared <= angularSpeedSquared)
			{
				m_lastAngularSpeedSquared = 0f;
				return false;
			}

			m_lastAngularSpeedSquared = angularSpeedSquared;
			return true;
		}

		/// <summary>
		/// Waits for the grid to stop.
		/// </summary>
		public override void Move()
		{
			if (LinearSlowdown() || AngularSlowdown())
			{
				_logger.debugLog("linear: " + m_mover.Block.Physics.LinearVelocity + ", angular: " + m_mover.Block.Physics.AngularVelocity);
				return;
			}

			INavigatorRotator rotator = m_navSet.Settings_Current.NavigatorRotator;
			if (rotator != null && !m_navSet.DirectionMatched())
			{
				_logger.debugLog("waiting for rotator to match");
				return;
			}

			m_mover.MoveAndRotateStop();
			_logger.debugLog("stopped");
			m_navSet.OnTaskComplete_NavRot();
			if (m_exitAfter)
			{
				_logger.debugLog("setting disable", Logger.severity.DEBUG);
				m_mover.SetControl(false);
			}
		}

		/// <summary>
		/// Appends "Exit after stopping" or "Stopping"
		/// </summary>
		/// <param name="customInfo">The autopilot block's custom info</param>
		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			if (m_exitAfter)
				customInfo.AppendLine("Exit after stopping");
			else
				customInfo.AppendLine("Stopping");
		}

		public void Rotate()
		{
			m_mover.CalcRotate_Stop();
		}

	}

}
