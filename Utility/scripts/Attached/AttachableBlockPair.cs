using Sandbox.ModAPI;

namespace Rynchodon.Attached
{
	/// <summary>
	/// For tracking pairs of blocks that are attached.
	/// </summary>
	public abstract class AttachableBlockPair : AttachableBlockBase
	{
		private static readonly Logger staticLogger = new Logger("AttachableBlock");

		private readonly Logger myLogger;

		public AttachableBlockPair(IMyCubeBlock block, AttachedGrid.AttachmentKind kind)
			: base(block, kind)
		{
			this.myLogger = new Logger("AttachableBlockPair", block);
			Registrar.Add(this.myBlock, this);
		}

		public void Update()
		{
			AttachableBlockPair partner = GetPartner();
			if (partner == null)
				Detach();
			else
				Attach(partner.myBlock);
		}

		public override string ToString()
		{ return "AttachableBlock:" + myBlock.DisplayNameText; }

		protected abstract AttachableBlockPair GetPartner();

		protected AttachableBlockPair GetPartner(long entityId)
		{
			if (entityId == 0)
			{
				//myLogger.debugLog("No partner, id == 0", "GetPartner()", Logger.severity.TRACE);
				return null;
			}

			AttachableBlockPair result;
			if (!Registrar.TryGetValue(entityId, out result))
			{
				myLogger.debugLog("Failed to get partner, " + entityId + " not in registry.", "CheckPartner()", Logger.severity.DEBUG);
				return null;
			}

			//myLogger.debugLog("Got partner: " + result.myBlock.DisplayNameText + ':' + entityId, "GetPartner()", Logger.severity.TRACE);
			return result;
		}
	}
}
