using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Sandbox.ModAPI;

namespace Rynchodon.Autopilot.Navigator
{
	/// <summary>
	/// <para>Stops the ship</para>
	/// </summary>
	public class Stopper : NavigatorMover
	{

		private const float StoppedThreshold = 0.001f;

		private readonly Logger _logger;
		private readonly bool exitAfter;

		/// <summary>
		/// Creates a new Stopper
		/// </summary>
		/// <param name="mover">The Mover to use</param>
		/// <param name="navSet">The settings to use</param>
		/// <param name="exitAfter">iff true, disable thruster control after stopping</param>
		public Stopper(Mover mover, AllNavigationSettings navSet, bool exitAfter)
			: base(mover, navSet)
		{
			_logger = new Logger("Stopper", m_controlBlock.Controller);
			this.exitAfter = exitAfter;

			m_mover.StopMove();
			m_mover.StopRotate();

			_logger.debugLog("created, disableThrust: " + exitAfter, "Stopper()");

			if (exitAfter)
				m_navSet.Settings_Commands.NavigatorMover = this;
			else
				m_navSet.Settings_Task_Primary.NavigatorMover = this;
		}

		#region NavigatorMover Members

		/// <summary>
		/// Waits for the grid to stop.
		/// </summary>
		public override void Move()
		{
			if (m_mover.Block.Physics.LinearVelocity.LengthSquared() < StoppedThreshold && m_mover.Block.Physics.AngularVelocity.LengthSquared() < StoppedThreshold)
			{
				INavigatorRotator rotator = m_navSet.Settings_Current.NavigatorRotator;
				if (rotator != null && !m_navSet.DirectionMatched())
				{
					_logger.debugLog("waiting for rotator to match", "Move()");
					return;
				}

				_logger.debugLog("stopped", "Stopper()");
				m_navSet.OnTaskPrimaryComplete();
				if (exitAfter && m_mover.Block.Controller.ControlThrusters)
				{
					_logger.debugLog("disabling thrusters", "Stopper()");
					m_mover.Block.SetControl(false);
				}
			}
			//else
			//	_logger.debugLog("not stopped", "Stopper()");
		}

		/// <summary>
		/// Appends "Exit after stopping" or "Stopping"
		/// </summary>
		/// <param name="customInfo">The autopilot block's custom info</param>
		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			if (exitAfter)
				customInfo.AppendLine("Exit after stopping");
			else
				customInfo.AppendLine("Stopping");
		}

		#endregion

	}

}
