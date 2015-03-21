#define LOG_ENABLED // remove on build

using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.Common;
using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;

using VRage;
using VRageMath;

namespace Rynchodon.AntennaRelay
{
	/// <summary>
	/// TextPanel will fetch instructions from Antenna and write them either for players or for programmable blocks.
	/// </summary>
	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_LaserAntenna))]
	public class TextPanel : UpdateEnforcer
	{
		private const string publicTitle_forPlayer = "Grid found by Autopilot";
		private const string publicTitle_forProgram = "Autopilot to Program";

		private IMyCubeBlock myCubeBlock;
		private Logger myLogger = new Logger(null, "TextPanel");

		private Receiver myAntenna;

		protected override void DelayedInit()
		{
			myCubeBlock = Entity as IMyCubeBlock;
			myLogger = new Logger(myCubeBlock.CubeGrid.DisplayName, "TextPanel", myCubeBlock.DisplayNameText);
			EnforcedUpdate = MyEntityUpdateEnum.EACH_100TH_FRAME;
		}

		public override void Close()
		{
			base.Close();
			myCubeBlock = null;
		}

		public override void UpdateAfterSimulation100()
		{
			if (!searchForAntenna())
				return;


		}

		private bool searchForAntenna()
		{
			if (myAntenna != null && !myAntenna.Closed) // already have one
				return true;

			foreach (Receiver antenna in RadioAntenna.registry)
				if (antenna.CubeBlock.canSendTo(myCubeBlock, true))
				{
					myLogger.debugLog("found antenna: " + antenna.CubeBlock.DisplayNameText, "searchForAntenna()", Logger.severity.INFO);
					myAntenna = antenna;
					return true;
				}
			foreach (Receiver antenna in LaserAntenna.registry)
				if (antenna.CubeBlock.canSendTo(myCubeBlock, true))
				{
					myLogger.debugLog("found antenna: " + antenna.CubeBlock.DisplayNameText, "searchForAntenna()", Logger.severity.INFO);
					myAntenna = antenna;
					return true;
				}
			return false;
		}


	}
}
