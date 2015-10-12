// skip file on build

using System;
using System.Collections.Generic;
using Rynchodon.AntennaRelay;
using Rynchodon.Attached;
using Rynchodon.Autopilot.Data;
using Sandbox.ModAPI;

namespace Rynchodon.Autopilot
{
	public static class GridTargeter
	{
		private static Logger myLogger = new Logger("GridTargeter");

		//private Navigator owner;

		//internal GridTargeter(Navigator owner)
		//{
		//	this.owner = owner;
		//	this.myLogger = new Logger("GridTargeter", () => owner.myGrid.DisplayName);
		//}

		//private DateTime nextTryLockOn;

		//public void tryLockOn()
		//{
		//	if (owner.CNS.lockOnTarget == NavSettings.TARGET.OFF)
		//		return;

		//	if (DateTime.UtcNow.CompareTo(nextTryLockOn) < 0)
		//		return;

		//	//GridDestination GridDest = owner.CNS.CurrentGridDest;
		//	//if (GridDest != null && owner.myEngager.CurrentStage == Weapons.Engager.Stage.Engaging && owner.myEngager.CanTarget(GridDest.Grid))
		//	//{
		//	//	myLogger.debugLog("Engager can continue.", "tryLockOn()");
		//	//	nextTryLockOn = DateTime.UtcNow.AddSeconds(10);
		//	//	return;
		//	//}
		//	if (owner.CNS.lockOnTarget == NavSettings.TARGET.ENEMY)
		//		owner.myEngager.Arm();

		//	myLogger.debugLog("trying to lock on type=" + owner.CNS.lockOnTarget, "tryLockOn()", Logger.severity.TRACE);

		//	IMyCubeBlock bestMatchBlock;
		//	LastSeen bestMatchGrid;
		//	if (!lastSeenHostile(out bestMatchGrid, out bestMatchBlock, owner.CNS.lockOnBlock)) // did not find a target
		//	{
		//		myLogger.debugLog("found nothing", "tryLockOn()");
		//		if (owner.CNS.target_locked)
		//		{
		//			myLogger.debugLog("lost lock on " + owner.CNS.GridDestName, "tryLockOn()");
		//			owner.fullStop("lost lock on " + owner.CNS.GridDestName);
		//			if (owner.CNS.lockOnTarget == NavSettings.TARGET.ENEMY)
		//				owner.myEngager.Disarm();
		//			owner.CNS.target_locked = false;
		//			owner.CNS.atWayDest(NavSettings.TypeOfWayDest.GRID);
		//		}
		//		nextTryLockOn = DateTime.UtcNow.AddSeconds(1);
		//		return;
		//	}

		//	// found an enemy, setting as destination
		//	if (bestMatchBlock != null)
		//		myLogger.debugLog("found an enemy: " + bestMatchGrid.Entity.DisplayName + ":" + bestMatchBlock.DisplayNameText, "tryLockOn()");
		//	else
		//		myLogger.debugLog("found an enemy: " + bestMatchGrid.Entity.DisplayName, "tryLockOn()");

		//	nextTryLockOn = DateTime.UtcNow.AddSeconds(10);
		//	owner.CNS.target_locked = true;
		//	owner.CNS.setDestination(bestMatchGrid, bestMatchBlock, owner.currentAPblock);
		//	//owner.CNS.SpecialFlyingInstructions = NavSettings.SpecialFlying.Split_MoveRotate;

		//	if (owner.CNS.lockOnTarget == NavSettings.TARGET.MISSILE)
		//	{
		//		owner.CNS.isAMissile = true;
		//		owner.reportState(Navigator.ReportableState.Missile);
		//	}
		//	else
		//		owner.reportState(Navigator.ReportableState.Engaging);
		//	owner.CNS.waitUntil = DateTime.UtcNow; // stop waiting
		//	owner.CNS.clearSpeedInternal();
		//	if (owner.CNS.lockOnTarget == NavSettings.TARGET.ENEMY)
		//		owner.myEngager.Engage();
		//}

		///// <summary>
		///// Search by best match (shortest string matching gridNameContains).
		///// </summary>
		///// <param name="gridNameContains"></param>
		//public bool lastSeenFriendly(string gridNameContains, out LastSeen bestMatchGrid, out IMyCubeBlock bestMatchBlock, string blockContains = null)
		//{
		//	myLogger.debugLog("entered lastSeenFriendly(" + gridNameContains + ", " + blockContains + ")", "lastSeenFriendly()", Logger.severity.TRACE);
		//	bestMatchGrid = null;
		//	bestMatchBlock = null;
		//	int bestMatchLength = -1;

		//	ShipController myAR;
		//	if (!ShipController.TryGet(owner.currentAPblock, out myAR))
		//	{
		//		myLogger.alwaysLog("failed to get ARShipController for currentAP(" + owner.currentAPblock.getNameOnly() + ")", "lastSeenFriendly()", Logger.severity.WARNING);
		//		return false;
		//	}
		//	IEnumerator<LastSeen> allLastSeen = myAR.lastSeenEnumerator();
		//	while (allLastSeen.MoveNext())
		//	{
		//		IMyCubeGrid grid = allLastSeen.Current.Entity as IMyCubeGrid;
		//		if (grid == null || grid == owner.myGrid)
		//			continue;
		//		if (grid.DisplayName.looseContains(gridNameContains))
		//		{
		//			myLogger.debugLog("compare match " + grid.DisplayName + "(" + grid.DisplayName.Length + ") to " + bestMatchLength, "lastSeenFriendly()", Logger.severity.TRACE);
		//			if (bestMatchGrid == null || grid.DisplayName.Length < bestMatchLength) // if better grid match
		//			{
		//				IMyCubeBlock matchBlock = null;
		//				if (!string.IsNullOrEmpty(blockContains) && !findBestFriendly(grid, out matchBlock, blockContains)) // grid does not contain at least one matching block
		//				{
		//					myLogger.debugLog("no matching block in " + grid.DisplayName, "lastSeenFriendly()", Logger.severity.TRACE);
		//					continue;
		//				}

		//				bestMatchGrid = allLastSeen.Current;
		//				bestMatchLength = grid.DisplayName.Length;
		//				bestMatchBlock = matchBlock;
		//			}
		//		}
		//	}
		//	return bestMatchGrid != null;
		//}

		///// <summary>
		///// Finds a last seen hostile ordered by distance(in metres) + time since last seen(in millis).
		///// </summary>
		///// <returns></returns>
		//public bool lastSeenHostile(out LastSeen bestMatchGrid, out IMyCubeBlock bestMatchBlock, string blockContains = null)
		//{
		//	bestMatchGrid = null;
		//	bestMatchBlock = null;
		//	double bestMatchValue = -1;

		//	ShipController myAR;
		//	if (!ShipController.TryGet(owner.currentAPblock, out myAR))
		//	{
		//		myLogger.alwaysLog("failed to get ARShipController for currentRC(" + owner.currentAPblock.DisplayNameText + ")", "lastSeenHostile()", Logger.severity.WARNING);
		//		myLogger.debugLog("needs update is " + owner.currentAPblock.NeedsUpdate, "lastSeenFriendly()", Logger.severity.DEBUG);
		//		return false;
		//	}
		//	IEnumerator<LastSeen> allLastSeen = myAR.lastSeenEnumerator();
		//	while (allLastSeen.MoveNext())
		//	{
		//		IMyCubeGrid grid = allLastSeen.Current.Entity as IMyCubeGrid;
		//		if (grid == null || grid == owner.myGrid || !owner.currentAPblock.canConsiderHostile(grid))
		//			continue;

		//		myLogger.debugLog("checking hostile grid: " + grid.DisplayName, "lastSeenHostile()");

		//		if (owner.CNS.lockOnTarget == NavSettings.TARGET.ENEMY)
		//			if (!owner.myEngager.CanTarget(grid))
		//			{
		//				myLogger.debugLog("engager cannot target grid: " + grid.DisplayName, "lastSeenHostile()");
		//				continue;
		//			}

		//		IMyCubeBlock matchBlock = null;
		//		if (!string.IsNullOrEmpty(blockContains) && !findBestHostile(grid, out matchBlock, blockContains)) // grid does not contain at least one matching block
		//			continue;

		//		TimeSpan timeSinceSeen = DateTime.UtcNow - allLastSeen.Current.LastSeenAt;
		//		double distance = owner.myGrid.WorldAABB.Distance(allLastSeen.Current.predictPosition(timeSinceSeen));
		//		if (owner.CNS.lockOnRangeEnemy > 0 && distance > owner.CNS.lockOnRangeEnemy)
		//			continue;
		//		double matchValue = distance + timeSinceSeen.TotalMilliseconds;
		//		if ((bestMatchGrid == null || matchValue < bestMatchValue)) // if better grid match
		//		{
		//			bestMatchGrid = allLastSeen.Current;
		//			bestMatchValue = matchValue;
		//			bestMatchBlock = matchBlock;
		//		}
		//	}
		//	return bestMatchGrid != null;
		//}

		private static string getBlockName(IMyCubeBlock Fatblock)
		{
			string blockName;
			if (ShipController_Autopilot.IsAutopilotBlock(Fatblock))
			{
				blockName = Fatblock.getNameOnly();
				if (blockName == null)
					blockName = Fatblock.DisplayNameText;
			}
			else
				blockName = Fatblock.DisplayNameText;
			return blockName;
		}

		private static bool collect_findBestFriendly(IMyCubeBlock doingSearch, IMySlimBlock slim, string blockContains)
		{
			IMyCubeBlock Fatblock = slim.FatBlock;
			if (Fatblock == null // armour or something
				|| !doingSearch.canControlBlock(Fatblock)) // neutral is OK too
				return false;

			return getBlockName(Fatblock).looseContains(blockContains); // must contain blockContains
		}

		/// <summary>
		/// Finds the best matching block (shortest string matching blockContains). Searches permanently attached grids.
		/// </summary>
		public static IMyCubeBlock findBestControl(IMyCubeBlock doingSearch, IMyCubeGrid grid, string blockContains, bool searchAttached = true)
		{
			List<IMySlimBlock> collected = new List<IMySlimBlock>();
			IMyCubeBlock bestMatchBlock = null;
			int bestMatchLength = -1;
			grid.GetBlocks(collected, block => collect_findBestFriendly(doingSearch, block, blockContains));

			foreach (IMySlimBlock block in collected)
			{
				IMyCubeBlock Fatblock = block.FatBlock;

				string blockName = getBlockName(Fatblock);

				myLogger.debugLog("compare match " + blockName + "(" + blockName.Length + ") to " + bestMatchLength, "findBestFriendly()", Logger.severity.TRACE);
				if ((bestMatchBlock == null || blockName.Length < bestMatchLength)) // if better match
				{
					bestMatchBlock = Fatblock;
					bestMatchLength = grid.DisplayName.Length;
				}
			}

			if (bestMatchBlock == null && searchAttached)
				AttachedGrid.RunOnAttached(grid, AttachedGrid.AttachmentKind.Permanent, attGrid => {
					bestMatchBlock = findBestControl(doingSearch, attGrid, blockContains, false);
					return bestMatchBlock != null;
				});

			return bestMatchBlock;
		}

		//private bool collect_findBestHostile(IMySlimBlock slim, string blockContains)
		//{
		//	IMyCubeBlock Fatblock = slim.FatBlock;
		//	return Fatblock != null // not armour
		//		&& Fatblock.IsWorking // ignore inactive hostile blocks
		//		&& owner.currentAPblock.canConsiderHostile(Fatblock) // block must be hostile
		//		&& Fatblock.DefinitionDisplayNameText.looseContains(blockContains); // must contain blockContains
		//}

		///// <summary>
		///// Finds the best matching block ordered by distance(in metres) + time since last seen(in millis).
		///// </summary>
		//public bool findBestHostile(IMyCubeGrid grid, out IMyCubeBlock bestMatchBlock, string blockContains)
		//{
		//	List<IMySlimBlock> collected = new List<IMySlimBlock>();
		//	bestMatchBlock = null;
		//	double bestMatchDist = -1;
		//	grid.GetBlocks(collected, block => collect_findBestHostile(block, blockContains));

		//	foreach (IMySlimBlock block in collected)
		//	{
		//		IMyCubeBlock Fatblock = block.FatBlock;
		//		double distance = owner.myGrid.WorldAABB.Distance(Fatblock.GetPosition());
		//		if (bestMatchBlock == null || distance < bestMatchDist) // if better match
		//		{
		//			bestMatchBlock = Fatblock;
		//			bestMatchDist = distance;
		//		}
		//	}
		//	return bestMatchBlock != null;
		//}
	}
}
