using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Sandbox.ModAPI;

namespace Rynchodon.Autopilot.Navigator
{
	/// <summary>
	/// <para>Stop the ship, then finished.</para>
	/// </summary>
	public class Stopper : ANavigator
	{

		private const float StoppedThreshold = 0.001f;

		private readonly Logger _logger;
		private readonly bool disableThrust;

		/// <summary>
		/// Creates a new Stopper
		/// </summary>
		/// <param name="disableThrust">iff true, disable thruster control after stopping</param>
		public Stopper(Mover mover, AllNavigationSettings navSet, bool disableThrust)
			: base (mover, navSet)
		{
			_logger = new Logger("Stopper", _block.Controller);
			this.disableThrust = disableThrust;

			_mover.FullStop();

			_logger.debugLog("created, disableThrust: " + disableThrust, "Stopper()");
		}

		public override void PerformTask()
		{
			if (_mover.Block.Physics.LinearVelocity.Sum < StoppedThreshold && _mover.Block.Physics.AngularVelocity.Sum < StoppedThreshold)
			{
				_logger.debugLog("stopped", "Stopper()");
				_navSet.OnTaskComplete();
				if (disableThrust && _mover.Block.Controller.ControlThrusters)
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
