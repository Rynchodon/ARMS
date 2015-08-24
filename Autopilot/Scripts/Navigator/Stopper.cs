using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.ModAPI.Interfaces;

namespace Rynchodon.Autopilot.Navigator
{
	/// <summary>
	/// <para>Stop the ship, then finished.</para>
	/// </summary>
	public class Stopper : ANavigator
	{

		private const float StoppedThreshold = 0.001f;

		private readonly ShipController_Block myBlock;
		private readonly bool disableThrust;

		/// <summary>
		/// Creates a new Stopper
		/// </summary>
		/// <param name="block">block to stop</param>
		/// <param name="disableThrust">iff true, disable thruster control after stopping</param>
		public Stopper(ShipController_Block block, bool disableThrust)
		{
			this.myBlock = block;
			this.disableThrust = disableThrust;

			block.MoveAndRotateStopped();
			CurrentState = NavigatorState.Running;
		}

		public override string ReportableState
		{ get { return "Stopping"; } }

		public override void PerformTask()
		{
			if (myBlock.Physics.LinearVelocity.Sum < StoppedThreshold)
			{
				CurrentState = NavigatorState.Finished;
				if (disableThrust && myBlock.EnabledThrusts)
					myBlock.SwitchThrusts();
			}
		}

	}
}
