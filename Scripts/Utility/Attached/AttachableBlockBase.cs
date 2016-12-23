using System;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace Rynchodon.Attached
{
	/// <summary>
	/// All attach and detach should go through here. Handles block and/or grid closings.
	/// </summary>
	public abstract class AttachableBlockBase
	{

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
			AttachmentKind = kind;
			myBlock = block;

			Logger.DebugLog("Created for: " + block.DisplayNameText);

			block.OnMarkForClose += Detach;
		}

		protected void Attach(IMyCubeBlock block)
		{
			if (curAttToBlock == block)
				return;

			Attach(block.CubeGrid, true);
			block.OnMarkForClose += Detach;
			curAttToBlock = block;
		}

		protected void Attach(IMyCubeGrid grid)
		{
			Attach(grid, false);
		}

		private void Attach(IMyCubeGrid grid, bool force)
		{
			if (!force && grid == curAttTo)
				return;

			Detach();

			myGrid = myBlock.CubeGrid;
			myGrid.OnMarkForClose += Detach;
			grid.OnMarkForClose += Detach;

			Logger.DebugLog("attaching " + myGrid.DisplayName + " to " + grid.DisplayName, Logger.severity.DEBUG);
			AttachedGrid.AddRemoveConnection(AttachmentKind, myGrid, grid, true);
			curAttTo = grid;
		}

		protected void Detach()
		{
			if (curAttTo == null)
				return;

			if (Globals.WorldClosed)
				return;

			myGrid.OnMarkForClose -= Detach;
			curAttTo.OnMarkForClose -= Detach;
			if (curAttToBlock != null)
			{
				curAttToBlock.OnMarkForClose -= Detach;
				curAttToBlock = null;
			}

			Logger.DebugLog("detaching " + myGrid.DisplayName + " from " + curAttTo.DisplayName, Logger.severity.DEBUG);
			AttachedGrid.AddRemoveConnection(AttachmentKind, myGrid, curAttTo, false);
			curAttTo = null;
		}

		private void Detach(IMyEntity obj)
		{
			Logger.DebugLog("closed object: " + obj.getBestName());
			try
			{ Detach(); }
			catch (Exception ex)
			{
				Logger.DebugLog("Exception: " + ex, Logger.severity.ERROR);
				Logger.DebugNotify("Detach encountered an exception", 10000, Logger.severity.ERROR);
			}
		}

	}
}
