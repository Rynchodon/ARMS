using Sandbox.Game.Entities.Blocks;
using VRage.Game.ModAPI;

namespace Rynchodon.AntennaRelay
{
	/// <summary>
	/// Sensor detects stuff, obviously.
	/// </summary>
	public sealed class Sensor
	{

		public readonly MySensorBlock Block;
		private readonly IRelayPart _relayPart;

		public Sensor(IMyCubeBlock sensor)
		{
			this.Block = (MySensorBlock)sensor;
			_relayPart = RelayClient.GetOrCreateRelayPart(sensor);
		}

		public void Update100()
		{
			if (Block.LastDetectedEntity == null || !NewRadar.IsValidRadarTarget(Block.LastDetectedEntity))
				return;

			RelayStorage store = _relayPart.GetStorage();
			if (store == null)
				return;

			store.Receive(new LastSeen(Block.LastDetectedEntity, LastSeen.DetectedBy.ByRadar, new LastSeen.RadarInfo(NewRadar.Volume(Block.LastDetectedEntity))));
		}

	}
}
