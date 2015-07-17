using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.AttachedGrid
{
	public class Connector : AttachableBlock
	{
		private readonly Logger myLogger;

		public Connector(IMyCubeBlock block)
			: base(block)
		{
			myLogger = new Logger("Connector", block);
			AttachmentKind = AttachedGrid.AttachmentKind.Connector;
		}

		protected override AttachableBlock GetPartner(AttachableBlock current)
		{
			Ingame.IMyShipConnector other = (myBlock as Ingame.IMyShipConnector).OtherConnector;
			return registry[other.EntityId];
		}
	}
}
