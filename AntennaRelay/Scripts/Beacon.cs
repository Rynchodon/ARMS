#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.AntennaRelay
{
	public class Beacon
	{
		private readonly Logger myLogger;
		private readonly IMyCubeBlock CubeBlock;
		private readonly Ingame.IMyBeacon myBeacon;

		public Beacon(IMyCubeBlock block)
		{
			CubeBlock = block;
			myBeacon = block as Ingame.IMyBeacon;
			myLogger = new Logger("Beacon", CubeBlock);

			myLogger.debugLog("init as beacon: " + CubeBlock.BlockDefinition.SubtypeName, "Init()", Logger.severity.TRACE);
		}

		public void UpdateAfterSimulation100()
		{
			if (CubeBlock == null || CubeBlock.Closed || CubeBlock.CubeGrid == null) return;
			try
			{
				// send beacon self to radio antenna
				LinkedList<RadioAntenna> canSeeMe = new LinkedList<RadioAntenna>(); // friend and foe alike

				float radiusSquared = myBeacon.Radius * myBeacon.Radius;
				foreach (RadioAntenna ant in RadioAntenna.registry)
					if (CubeBlock.canSendTo(ant.CubeBlock, false, radiusSquared, true))
						canSeeMe.AddLast(ant);

				LastSeen self = new LastSeen(CubeBlock.CubeGrid);
				foreach (RadioAntenna ant in canSeeMe)
					ant.receive(self);
			}
			catch (Exception e)
			{ myLogger.alwaysLog("Exception: " + e, "UpdateAfterSimulation100()", Logger.severity.ERROR); }
		}

		public override string ToString()
		{ return CubeBlock.CubeGrid.DisplayName + "-" + myBeacon.DisplayNameText; }

	}
}
