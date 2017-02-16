using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace Rynchodon.Attached
{
	public class Connector : AttachableBlockUpdate
	{
		public Connector(IMyCubeBlock block)
			: base(block, AttachedGrid.AttachmentKind.Connector)
		{ }

		protected override IMyCubeBlock GetPartner()
		{
			IMyShipConnector myConn = (IMyShipConnector)myBlock;
			if (myConn.Status != Sandbox.ModAPI.Ingame.MyShipConnectorStatus.Connected)
				return null;

			return myConn.OtherConnector;
		}
	}
}
