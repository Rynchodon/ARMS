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
			Ingame.IMyShipConnector myConn = myBlock as Ingame.IMyShipConnector;
			if (!myConn.IsConnected)
				return null;

			Ingame.IMyShipConnector other = myConn.OtherConnector;
			if (other == null)
				return null;
			return GetPartner(other.EntityId);
		}
	}
}
