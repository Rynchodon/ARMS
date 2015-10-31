using System;
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
				LastSeen self = new LastSeen(CubeBlock.CubeGrid, LastSeen.UpdateTime.Broadcasting);

				float radiusSquared = myBeacon.Radius * myBeacon.Radius;
				Registrar.ForEach((RadioAntenna ant) => {
					if (CubeBlock.canSendTo(ant.CubeBlock, false, radiusSquared, true))
						ant.Receive(self);
					return false;
				});
			}
			catch (Exception e)
			{ myLogger.alwaysLog("Exception: " + e, "UpdateAfterSimulation100()", Logger.severity.ERROR); }
		}

		public override string ToString()
		{ return CubeBlock.CubeGrid.DisplayName + "-" + myBeacon.DisplayNameText; }

	}
}
