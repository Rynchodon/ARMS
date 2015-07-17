using System.Collections.Generic;
using Sandbox.ModAPI;

namespace Rynchodon.AttachedGrid
{
	/// <summary>
	/// For tracking pairs of blocks that are attached.
	/// </summary>
	public abstract class AttachableBlock
	{
		internal static Dictionary<long, AttachableBlock> registry = new Dictionary<long, AttachableBlock>();

		private static Logger staticLogger = new Logger("AttachableBlock");

		internal readonly IMyCubeBlock myBlock;

		private readonly Logger myLogger;

		public static bool TryGetAttached(IMyCubeBlock lookup, out IMyCubeBlock attached)
		{
			AttachableBlock extObj;
			if (!AttachableBlock.registry.TryGetValue(lookup.EntityId, out extObj))
			{
				staticLogger.alwaysLog("failed to get AttachableBlock from registry: " + lookup.DisplayNameText + ':' + lookup.EntityId, "TryGetAttached()", Logger.severity.WARNING);
				attached = null;
				return false;
			}
			if (extObj.Partner == null)
			{
				attached = null;
				return false;
			}
			attached = extObj.Partner.myBlock;
			return true;
		}

		public AttachableBlock(IMyCubeBlock block)
		{
			this.myLogger = new Logger("AttachableBlock", block);
			this.myBlock = block;
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

		public void Update100()
		{ CheckPartner(); }

		public override string ToString()
		{ return "AttachableBlock:" + myBlock.DisplayNameText; }

		//internal bool Closed { get { return myBlock.Closed; } }

		internal AttachableBlock Partner { get; private set; }

		internal AttachedGrid.AttachmentKind AttachmentKind { get; protected set; }

		protected abstract AttachableBlock GetPartner(AttachableBlock current);

		protected AttachableBlock GetPartner(long entityId)
		{
			if (entityId == 0)
			{
				myLogger.debugLog("No partner, id == 0", "GetPartner()", Logger.severity.TRACE);
				return null;
			}

			AttachableBlock result;
			if (!registry.TryGetValue(entityId, out result))
			{
				myLogger.alwaysLog("Failed to get partner, " + entityId + " not in registry.", "CheckPartner()", Logger.severity.WARNING);
				return null;
			}

			//myLogger.debugLog("Got partner: " + result.myBlock.DisplayNameText + ':' + entityId, "GetPartner()", Logger.severity.TRACE);
			return result;
		}

		private void CheckPartner()
		{
			//myLogger.debugLog("entered CheckPartner()", "CheckPartner()");

			if (Partner != null && Partner.myBlock.MarkedForClose)
			{
				myLogger.debugLog("Partner has closed", "CheckPartner()", Logger.severity.INFO);
				Partner = null;
			}

			AttachableBlock newPartner = GetPartner(Partner);
			if (Partner == newPartner)
				return;

			// Because CheckPartner() is called inconsistently, there could be both a value_partner and a newPartner

			if (Partner != null)
			{
				myLogger.debugLog("unpairing " + Partner, "CheckPartner()", Logger.severity.INFO);
				AttachedGrid.AddRemoveConnection(AttachmentKind, myBlock.CubeGrid, Partner.myBlock.CubeGrid, false);
				Partner.Partner = null;
			}

			if (newPartner != null)
			{
				myLogger.debugLog("pairing " + newPartner, "CheckPartner()", Logger.severity.INFO);
				newPartner.Partner = this;
				AttachedGrid.AddRemoveConnection(AttachmentKind, myBlock.CubeGrid, Partner.myBlock.CubeGrid, true);
			}

			Partner = newPartner;
		}
	}
}
