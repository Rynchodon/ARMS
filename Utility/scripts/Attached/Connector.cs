using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Attached
{
	public class Connector : AttachableBlockPair
	{
		private readonly Logger myLogger;

		public Connector(IMyCubeBlock block)
			: base(block, AttachedGrid.AttachmentKind.Connector)
		{
			myLogger = new Logger("Connector", block);
		}

		protected override AttachableBlockPair GetPartner()
		{
			Ingame.IMyShipConnector other = (myBlock as Ingame.IMyShipConnector).OtherConnector;
			if (other == null)
				return null;
			return GetPartner(other.EntityId);
		}
	}
}
