using System.Collections.Generic;
using Sandbox.ModAPI;

namespace Rynchodon.AttachedGrid
{
	/// <summary>
	/// For tracking pairs of blocks that are attached.
	/// </summary>
	public abstract class AttachableBlockPair : AttachableBlockBase
	{
		private static readonly Dictionary<long, AttachableBlockPair> registry = new Dictionary<long, AttachableBlockPair>();
		private static readonly Logger staticLogger = new Logger("AttachableBlock");

		//public static bool TryGetAttached(IMyCubeBlock lookup, out IMyCubeBlock attached)
		//{
		//	AttachableBlockPair extObj;
		//	if (!AttachableBlockPair.registry.TryGetValue(lookup.EntityId, out extObj))
		//	{
		//		staticLogger.alwaysLog("failed to get AttachableBlock from registry: " + lookup.DisplayNameText + ':' + lookup.EntityId, "TryGetAttached()", Logger.severity.WARNING);
		//		attached = null;
		//		return false;
		//	}
		//	if (extObj.Partner == null)
		//	{
		//		attached = null;
		//		return false;
		//	}
		//	attached = extObj.Partner.myBlock;
		//	return true;
		//}

		private readonly Logger myLogger;

		public AttachableBlockPair(IMyCubeBlock block, AttachedGrid.AttachmentKind kind)
			: base(block, kind)
		{
			this.myLogger = new Logger("AttachableBlockPair", block);
			registry.Add(this.myBlock.EntityId, this);
			this.myBlock.OnClose += myBlock_OnClose;

			myLogger.debugLog("Added to registry with id: " + this.myBlock.EntityId, "AttachableBlock()", Logger.severity.INFO);
		}

		private void myBlock_OnClose(VRage.ModAPI.IMyEntity obj)
		{
			myLogger.debugLog("entered myBlock_OnClose()", "myBlock_OnClose()");
			myBlock.OnClosing -= myBlock_OnClose;
			registry.Remove(myBlock.EntityId);
			myLogger.debugLog("leaving myBlock_OnClose()", "myBlock_OnClose()");
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
			if (!registry.TryGetValue(entityId, out result))
			{
				myLogger.debugLog("Failed to get partner, " + entityId + " not in registry.", "CheckPartner()", Logger.severity.DEBUG);
				return null;
			}

			//myLogger.debugLog("Got partner: " + result.myBlock.DisplayNameText + ':' + entityId, "GetPartner()", Logger.severity.TRACE);
			return result;
		}
	}
}
