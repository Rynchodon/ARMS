using System;
using Sandbox.ModAPI;
using VRage.ModAPI;

namespace Rynchodon.Attached
{
	/// <summary>
	/// All attach and detach should go through here. Handles block and/or grid closings.
	/// </summary>
	public abstract class AttachableBlockBase
	{

		private readonly Logger myLogger;
		public readonly AttachedGrid.AttachmentKind AttachmentKind;
		public readonly IMyCubeBlock myBlock;

		private IMyCubeGrid myGrid;
		private IMyCubeGrid curAttTo;
		private IMyCubeBlock curAttToBlock;

		/// <summary>True iff an attachment has been formed.</summary>
		protected bool IsAttached
		{ get { return curAttTo != null; } }

		protected AttachableBlockBase(IMyCubeBlock block, AttachedGrid.AttachmentKind kind)
		{
			myLogger = new Logger("AttachableBlockBase", block);
			AttachmentKind = kind;
			myBlock = block;

			myLogger.debugLog("Created for: " + block.DisplayNameText, "AttachableBlockBase()");

			block.OnClose += Detach;
			Registrar.Add(this.myBlock, this);
		}

		protected AttachableBlockBase GetPartner(long entityId)
		{
			if (entityId == 0)
			{
				//myLogger.debugLog("No partner, id == 0", "GetPartner()", Logger.severity.TRACE);
				return null;
			}

			AttachableBlockBase result;
			if (!Registrar.TryGetValue(entityId, out result))
			{
				myLogger.debugLog("Failed to get partner, " + entityId + " not in registry.", "CheckPartner()", Logger.severity.DEBUG);
				return null;
			}

			//myLogger.debugLog("Got partner: " + result.myBlock.DisplayNameText + ':' + entityId, "GetPartner()", Logger.severity.TRACE);
			return result;
		}

		protected void Attach(IMyCubeBlock block)
		{
			//myLogger.debugLog("Attach(block: " + block.DisplayNameText + ")", "Attach()");
			if (curAttToBlock == block)
				return;

			Attach(block.CubeGrid, true);
			block.OnClose += Detach;
			curAttToBlock = block;
		}

		protected void Attach(IMyCubeGrid grid)
		{
			//myLogger.debugLog("Attach(grid: " + grid.DisplayName + ")", "Attach()");
			Attach(grid, false);
		}

		private void Attach(IMyCubeGrid grid, bool force)
		{
			if (!force && grid == curAttTo)
				return;

			Detach();

			myGrid = myBlock.CubeGrid;
			myGrid.OnClose += Detach;
			grid.OnClose += Detach;

			myLogger.debugLog("attaching " + myGrid.DisplayName + " to " + grid.DisplayName, "Attach()", Logger.severity.DEBUG);
			AttachedGrid.AddRemoveConnection(AttachmentKind, myGrid, grid, true);
			curAttTo = grid;
		}

		protected void Detach()
		{
			if (curAttTo == null)
				return;

			myGrid.OnClose -= Detach;
			curAttTo.OnClose -= Detach;
			if (curAttToBlock != null)
			{
				curAttToBlock.OnClose -= Detach;
				curAttToBlock = null;
			}

			myLogger.debugLog("detaching " + myGrid.DisplayName + " from " + curAttTo.DisplayName, "Detach()", Logger.severity.DEBUG);
			AttachedGrid.AddRemoveConnection(AttachmentKind, myGrid, curAttTo, false);
			curAttTo = null;
		}

		private void Detach(IMyEntity obj)
		{
			myLogger.debugLog("closed object: " + obj.getBestName() + ")", "Detach()");
			try
			{ Detach(); }
			catch (Exception ex)
			{
				myLogger.debugLog("Exception: " + ex, "Detach()", Logger.severity.ERROR);
				Logger.debugNotify("Detach encountered an exception", 10000, Logger.severity.ERROR);
			}
		}

	}
}
