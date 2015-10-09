#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.AntennaRelay
{
	public class RadioAntenna : Receiver
	{
		private static List<RadioAntenna> value_registry = new List<RadioAntenna>();
		public static ReadOnlyList<RadioAntenna> registry { get { return new ReadOnlyList<RadioAntenna>(value_registry); } }

		private Ingame.IMyRadioAntenna myRadioAntenna;
		private Logger myLogger;

		public RadioAntenna(IMyCubeBlock block)
			: base(block)
		{
			myLogger = new Logger("RadioAntenna", () => CubeBlock.CubeGrid.DisplayName);
			myRadioAntenna = CubeBlock as Ingame.IMyRadioAntenna;
			value_registry.Add(this);
		}

		protected override void Close(IMyEntity entity)
		{
			try
			{ value_registry.Remove(this); }
			catch (Exception e)
			{ myLogger.alwaysLog("exception on removing from registry: " + e, "Close()", Logger.severity.WARNING); }
			myRadioAntenna = null;
		}

		public void UpdateAfterSimulation100()
		{
			try
			{
				if (!myRadioAntenna.IsWorking)
					return;

				//Showoff.doShowoff(CubeBlock, myLastSeen.Values.GetEnumerator(), myLastSeen.Count);

				float radiusSquared;
				MyObjectBuilder_RadioAntenna antBuilder = CubeBlock.GetObjectBuilderCubeBlock() as MyObjectBuilder_RadioAntenna;
				if (!antBuilder.EnableBroadcasting)
					radiusSquared = 0;
				else
					radiusSquared = myRadioAntenna.Radius * myRadioAntenna.Radius;

				LastSeen self = new LastSeen(CubeBlock.CubeGrid, LastSeen.UpdateTime.Broadcasting);

				// send antenna self to radio antennae
				foreach (RadioAntenna ant in RadioAntenna.registry)
					if (CubeBlock.canSendTo(ant.CubeBlock, false, radiusSquared, true))
						ant.Receive(self);

				// relay information to friendlies
				foreach (RadioAntenna ant in value_registry)
					if (CubeBlock.canSendTo(ant.CubeBlock, true, radiusSquared, true))
						Relay(ant);

				// relay information to friendly players
				ForEachLastSeen(seen => {
					foreach (Player player in Player.AllPlayers)
						if (CubeBlock.canSendTo(player.myPlayer, true, radiusSquared, true))
								player.receive(seen);
					return false;
				});

				RelayAttached();
			}
			catch (Exception e)
			{ myLogger.alwaysLog("Exception: " + e, "UpdateAfterSimulation100()", Logger.severity.ERROR); }
		}
	}
}
