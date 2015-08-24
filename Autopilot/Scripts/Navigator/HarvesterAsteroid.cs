using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.ModAPI;

namespace Rynchodon.Autopilot.Navigator
{
	public class HarvesterAsteroid : ANavigator
	{

		private static bool CreativeMode = MyAPIGateway.Session.CreativeMode;

		private const float FullAmount_Abort = 0.9f, FullAmount_Return = 0.1f;
		private const float rotLenSq_finishedRotate = 0.00762f; // 5°
		private const float rotLenSq_offCourse = 0.685f; // 15°

		private enum HarvesterState : byte { H_Off, H_Ready, Harvest, H_Stuck, H_Back, H_Tunnel, H_Finished }

		private readonly ShipController_Block myBlock;
		private readonly Logger myLogger;

		private HarvesterState CurrentHarvesterState = HarvesterState.H_Off;

		public HarvesterAsteroid(ShipController_Block block)
		{
			myBlock = block;
			myLogger = new Logger("HarvesterAsteroid", block);
		}

		public override string ReportableState
		{ get { return CurrentHarvesterState.ToString(); } }

		public override void PerformTask()
		{
			throw new NotImplementedException();
		}

	}
}
