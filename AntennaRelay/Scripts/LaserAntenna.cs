using System;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.AntennaRelay
{
	public class LaserAntenna : ReceiverBlock
	{

		private Ingame.IMyLaserAntenna myLaserAntenna;
		private Logger myLogger;

		private MyObjectBuilder_LaserAntenna builder;

		public LaserAntenna(IMyCubeBlock block)
			: base(block)
		{
			myLaserAntenna = CubeBlock as Ingame.IMyLaserAntenna;
			myLogger = new Logger("LaserAntenna", () => CubeBlock.CubeGrid.DisplayName);
			Registrar.Add(block, this);
		}

		public void UpdateAfterSimulation100()
		{
			try
			{
				if (!myLaserAntenna.IsWorking)
					return;

				builder = CubeBlock.getSlimObjectBuilder() as MyObjectBuilder_LaserAntenna;

				// state 5 is the final state. It is possible for one to be in state 5, while the other is not
				if (builder.targetEntityId != null)
				{
					LaserAntenna lAnt;
					if (Registrar.TryGetValue(builder.targetEntityId.Value, out lAnt))
						if (lAnt.builder != null && builder.State == 5 && lAnt.builder.State == 5)
							Relay(lAnt);
				}

				RelayAttached();
			}
			catch (Exception e)
			{ myLogger.alwaysLog("Exception: " + e, "UpdateAfterSimulation100()", Logger.severity.ERROR); }
		}
	}
}
