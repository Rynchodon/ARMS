using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Sandbox.ModAPI;

namespace Rynchodon.Autopilot.Navigator
{
	/// <summary>
	/// <para>Stop the ship, then finished.</para>
	/// </summary>
	public class Stopper : NavigatorMover
	{

		private const float StoppedThreshold = 0.001f;

		private readonly Logger _logger;
		private readonly bool exitAfter;

		/// <summary>
		/// Creates a new Stopper
		/// </summary>
		/// <param name="exitAfter">iff true, disable thruster control after stopping</param>
		public Stopper(Mover mover, AllNavigationSettings navSet, bool exitAfter)
			: base(mover, navSet)
		{
			_logger = new Logger("Stopper", m_controlBlock.Controller);
			this.exitAfter = exitAfter;

			_mover.FullStop();

			_logger.debugLog("created, disableThrust: " + exitAfter, "Stopper()");

			if (exitAfter)
				_navSet.Settings_Commands.NavigatorMover = this;
			else
				_navSet.Settings_Task_Secondary.NavigatorMover = this;
		}

		public override void Move()
		{
			if (_mover.Block.Physics.LinearVelocity.Sum < StoppedThreshold )
			{
				_logger.debugLog("stopped", "Stopper()");
				_navSet.OnTaskSecondaryComplete();
				if (exitAfter && _mover.Block.Controller.ControlThrusters)
				{
					_logger.debugLog("disabling thrusters", "Stopper()");
					_mover.Block.Terminal.GetActionWithName("ControlThrusters").Apply(_mover.Block.Terminal);
				}
			}
			else
				_logger.debugLog("not stopped", "Stopper()");
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{ customInfo.AppendLine("Stopping"); }

	}

}
