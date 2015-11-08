using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.AntennaRelay
{
	/// <summary>
	/// Keeps track of transmissions for a remote control. A remote control cannot relay, so it should only receive messages for iteself.
	/// </summary>
	public class ShipController : ReceiverBlock
	{
		private Ingame.IMyShipController myController;
		private Logger myLogger;

		public ShipController(IMyCubeBlock block)
			: base(block)
		{
			myLogger = new Logger("ShipController", () => CubeBlock.CubeGrid.DisplayName);
			myController = CubeBlock as Ingame.IMyShipController;
			Registrar.Add(CubeBlock, this);
		}
	}
}
