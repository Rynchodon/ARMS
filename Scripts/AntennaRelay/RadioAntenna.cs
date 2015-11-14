using System;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.AntennaRelay
{
	public class RadioAntenna : ReceiverBlock
	{

		private Ingame.IMyRadioAntenna myRadioAntenna;
		private Logger myLogger;

		public RadioAntenna(IMyCubeBlock block)
			: base(block)
		{
			myLogger = new Logger("RadioAntenna", () => CubeBlock.CubeGrid.DisplayName);
			myRadioAntenna = CubeBlock as Ingame.IMyRadioAntenna;
			Registrar.Add(myRadioAntenna, this);
		}

		public void UpdateAfterSimulation100()
		{
			try
			{
				if (!myRadioAntenna.IsWorking)
					return;

				float radiusSquared;
				MyObjectBuilder_RadioAntenna antBuilder = CubeBlock.GetObjectBuilderCubeBlock() as MyObjectBuilder_RadioAntenna;
				if (!antBuilder.EnableBroadcasting)
					radiusSquared = 0;
				else
					radiusSquared = myRadioAntenna.Radius * myRadioAntenna.Radius;

				LastSeen self = new LastSeen(CubeBlock.CubeGrid, LastSeen.UpdateTime.Broadcasting);

				Registrar.ForEach((RadioAntenna ant) => {
					// send antenna self to radio antennae
					if (CubeBlock.canSendTo(ant.CubeBlock, false, radiusSquared, true))
						ant.Receive(self);

					// relay information to friendlies
					if (CubeBlock.canSendTo(ant.CubeBlock, true, radiusSquared, true))
						Relay(ant);
				});

				ForEachLastSeen(seen => {
					// relay information to friendly players
					foreach (Player player in Player.AllPlayers)
						if (CubeBlock.canSendTo(player.myPlayer, true, radiusSquared, true))
							player.receive(seen);

					// relay information to missile antennae
					Registrar.ForEach((MissileAntenna ant) => {
						if (CubeBlock.canSendTo(ant.Entity, true, radiusSquared, true))
							ant.Receive(seen);
					});

					return false;
				});

				RelayAttached();
			}
			catch (Exception e)
			{ myLogger.alwaysLog("Exception: " + e, "UpdateAfterSimulation100()", Logger.severity.ERROR); }
		}
	}
}
