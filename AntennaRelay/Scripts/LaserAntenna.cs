#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.AntennaRelay
{
	public class LaserAntenna : Receiver
	{
		private static List<LaserAntenna> value_registry = new List<LaserAntenna>();
		public static ReadOnlyList<LaserAntenna> registry { get { return new ReadOnlyList<LaserAntenna>(value_registry); } }

		private Ingame.IMyLaserAntenna myLaserAntenna;
		private Logger myLogger;

		private MyObjectBuilder_LaserAntenna builder;

		public LaserAntenna(IMyCubeBlock block)
			: base(block)
		{
			myLaserAntenna = CubeBlock as Ingame.IMyLaserAntenna;
			myLogger = new Logger("LaserAntenna", () => CubeBlock.CubeGrid.DisplayName);
			value_registry.Add(this);
		}

		protected override void Close(IMyEntity entity)
		{
			try
			{
				if (CubeBlock != null)
					value_registry.Remove(this);
			}
			catch (Exception e)
			{ myLogger.alwaysLog("exception on removing from registry: " + e, "Close()", Logger.severity.WARNING); }
			CubeBlock = null;
			myLaserAntenna = null;
			myLastSeen = null;
			myMessages = null;
		}

		public void UpdateAfterSimulation100()
		{
			try
			{
				if (!myLaserAntenna.IsWorking)
					return;

				builder = CubeBlock.getSlimObjectBuilder() as MyObjectBuilder_LaserAntenna;

				// stage 5 is the final stage. It is possible for one to be in stage 5, while the other is not
				if (builder.targetEntityId != null)
					foreach (LaserAntenna lAnt in value_registry)
						if (lAnt.CubeBlock.EntityId == builder.targetEntityId)
						{
							if (lAnt.builder != null && builder.State == 5 && lAnt.builder.State == 5)
							{
								foreach (LastSeen seen in myLastSeen.Values)
									lAnt.receive(seen);
								foreach (Message mes in myMessages)
									lAnt.receive(mes);
							}
							break;
						}

				// send to attached receivers
				Receiver.sendToAttached(CubeBlock, myLastSeen);
				Receiver.sendToAttached(CubeBlock, myMessages);
			}
			catch (Exception e)
			{ myLogger.alwaysLog("Exception: " + e, "UpdateAfterSimulation100()", Logger.severity.ERROR); }
		}
	}
}
