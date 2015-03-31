#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using System.Linq;
//using System.Text;

using Sandbox.Common;
//using Sandbox.Common.Components;
//using Sandbox.Common.ObjectBuilders;
//using Sandbox.Definitions;
//using Sandbox.Engine;
//using Sandbox.Game;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;
//using Sandbox.ModAPI.Interfaces;
using VRageMath;

using Rynchodon.AntennaRelay;

namespace Rynchodon.Autopilot
{
	internal class Targeter
	{
		private Logger myLogger;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ alwaysLog(toLog, method, level); }
		private void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null) myLogger = new Logger(owner.myGrid.DisplayName, "NewTargeter");
			myLogger.log(level, method, toLog);
		}

		private Navigator owner;

		internal Targeter(Navigator owner)
		{ this.owner = owner; }

		private DateTime nextTryLockOn;

		public void tryLockOn()
		{
			//log("entered tryLockOn");

			if (owner.CNS.lockOnTarget == NavSettings.TARGET.OFF)
				return;

			if (DateTime.UtcNow.CompareTo(nextTryLockOn) < 0)
			{
				//log("bailing, too soon to retarget", "tryLockOn()", Logger.severity.TRACE);
				return;
			}

			log("trying to lock on type=" + owner.CNS.lockOnTarget, "tryLockOn()", Logger.severity.TRACE);
			
			IMyCubeBlock bestMatchBlock;
			LastSeen bestMatchGrid;
			if (!lastSeenHostile(out bestMatchGrid, out bestMatchBlock, owner.CNS.lockOnBlock)) // did not find a target
			{
				if (owner.CNS.target_locked)
				{
					log("lost lock on " + owner.CNS.GridDestName);
					owner.CNS.target_locked = false;
					owner.CNS.atWayDest();
				}
				nextTryLockOn = DateTime.UtcNow.AddSeconds(1);
				return;
			}

			// found an enemy, setting as destination
			if (bestMatchBlock != null)
				log("found an enemy: " + bestMatchGrid.Entity.DisplayName + ":" + bestMatchBlock.DisplayNameText, "tryLockOn()");
			else
				log("found an enemy: " + bestMatchGrid.Entity.DisplayName, "tryLockOn()");

			owner.CNS.target_locked = true;
			nextTryLockOn = DateTime.UtcNow.AddSeconds(10);
			owner.CNS.setDestination(bestMatchGrid, bestMatchBlock, owner.currentRCblock);

			if (owner.CNS.lockOnTarget == NavSettings.TARGET.MISSILE)
			{
				owner.CNS.isAMissile = true;
				owner.reportState(Navigator.ReportableState.MISSILE);
			}
			else
				owner.reportState(Navigator.ReportableState.ENGAGING);
			owner.CNS.waitUntil = DateTime.UtcNow; // stop waiting
			owner.CNS.clearSpeedInternal();
		}

		/// <summary>
		/// Search by best match (shortest string matching gridNameContains).
		/// </summary>
		/// <param name="gridNameContains"></param>
		public bool lastSeenFriendly(string gridNameContains, out LastSeen bestMatchGrid, out IMyCubeBlock bestMatchBlock, string blockContains = null)
		{
			log("entered lastSeenFriendly(" + gridNameContains + ", " + blockContains + ")", "lastSeenFriendly()", Logger.severity.TRACE);
			bestMatchGrid = null;
			bestMatchBlock = null;
			int bestMatchLength = -1;

			RemoteControl myAR;
			if (!RemoteControl.TryGet(owner.currentRCblock, out myAR))
			{
				alwaysLog("failed to get ARRemoteControl for currentRC(" + owner.currentRCblock.getNameOnly() + ")", "lastSeenFriendly()", Logger.severity.WARNING);
				//log("needs update is " + owner.currentRCblock.NeedsUpdate, "lastSeenFriendly()", Logger.severity.DEBUG);
				return false;
			}
			IEnumerator<LastSeen> allLastSeen = myAR.lastSeenEnumerator();
			while (allLastSeen.MoveNext())
			{
				IMyCubeGrid grid = allLastSeen.Current.Entity as IMyCubeGrid;
				if (grid == null || grid == owner.myGrid)
					continue;
				//log("testing " + grid.DisplayName + " contains " + gridNameContains, "lastSeenFriendly()", Logger.severity.TRACE);
				if (grid.DisplayName.looseContains(gridNameContains))
				{
					log("compare match "+grid.DisplayName+"(" + grid.DisplayName.Length + ") to " + bestMatchLength, "lastSeenFriendly()", Logger.severity.TRACE);
					if (bestMatchGrid == null || grid.DisplayName.Length < bestMatchLength) // if better grid match
					{
						IMyCubeBlock matchBlock = null;
						if (!string.IsNullOrEmpty(blockContains) && !findBestFriendly(grid, out matchBlock, blockContains)) // grid does not contain at least one matching block
						{
							log("no matching block in "+grid.DisplayName, "lastSeenFriendly()", Logger.severity.TRACE);
							continue;
						}

						bestMatchGrid = allLastSeen.Current;
						bestMatchLength = grid.DisplayName.Length;
						bestMatchBlock = matchBlock;
					}
				}
			}
			return bestMatchGrid != null;
		}

		/// <summary>
		/// Finds a last seen hostile ordered by distance(in metres) + time since last seen(in millis).
		/// </summary>
		/// <returns></returns>
		public bool lastSeenHostile(out LastSeen bestMatchGrid, out IMyCubeBlock bestMatchBlock, string blockContains = null)
		{
			bestMatchGrid = null;
			bestMatchBlock = null;
			double bestMatchValue = -1;

			RemoteControl myAR;
			if (!RemoteControl.TryGet(owner.currentRCblock, out myAR))
			{
				alwaysLog("failed to get ARRemoteControl for currentRC(" + owner.currentRCblock.DisplayNameText + ")", "lastSeenHostile()", Logger.severity.WARNING);
				log("needs update is " + owner.currentRCblock.NeedsUpdate, "lastSeenFriendly()", Logger.severity.DEBUG);
				return false;
			}
			IEnumerator<LastSeen> allLastSeen = myAR.lastSeenEnumerator();
			while (allLastSeen.MoveNext())
			{
				IMyCubeGrid grid = allLastSeen.Current.Entity as IMyCubeGrid;
				if (grid == null || grid == owner.myGrid || !owner.currentRCblock.canConsiderHostile(grid))
					continue;

				IMyCubeBlock matchBlock = null;
				if (!string.IsNullOrEmpty(blockContains) && !findBestHostile(grid, out matchBlock, blockContains)) // grid does not contain at least one matching block
					continue;

				TimeSpan timeSinceSeen = DateTime.UtcNow - allLastSeen.Current.LastSeenAt;
				double distance = owner.myGrid.WorldAABB.Distance(allLastSeen.Current.predictPosition(timeSinceSeen));
				if (owner.CNS.lockOnRangeEnemy > 0 && distance > owner.CNS.lockOnRangeEnemy)
					continue;
				double matchValue = distance + timeSinceSeen.TotalMilliseconds;
				if (bestMatchGrid == null || matchValue < bestMatchValue) // if better grid match
				{
					bestMatchGrid = allLastSeen.Current;
					bestMatchValue = matchValue;
					bestMatchBlock = matchBlock;
				}
			}
			return bestMatchGrid != null;
		}

		private string getBlockName(IMyCubeBlock Fatblock)
		{
			string blockName;
			if (Fatblock is Ingame.IMyRemoteControl)
			{
				blockName = Fatblock.getNameOnly();
				if (blockName == null)
					blockName = Fatblock.DisplayNameText;
			}
			else
				blockName = Fatblock.DisplayNameText;
			return blockName;
		}

		private bool collect_findBestFriendly(IMySlimBlock slim, string blockContains)
		{
			IMyCubeBlock Fatblock = slim.FatBlock;
			if (Fatblock == null // armour or something
				|| !owner.currentRCblock.canControlBlock(Fatblock)) // neutral is OK too
				return false;

			string blockName = getBlockName(Fatblock);
			return blockName.looseContains(blockContains); // must contain blockContains
		}

		/// <summary>
		/// Finds the best matching block (shortest string matching blockContains).
		/// </summary>
		/// <param name="bestMatchBlock"></param>
		/// <param name="blockContains"></param>
		/// <returns></returns>
		public bool findBestFriendly(IMyCubeGrid grid, out IMyCubeBlock bestMatchBlock, string blockContains)
		{
			List<IMySlimBlock> collected = new List<IMySlimBlock>();
			bestMatchBlock = null;
			int bestMatchLength = -1;
			grid.GetBlocks(collected, block => collect_findBestFriendly(block, blockContains));

			foreach (IMySlimBlock block in collected)
			{
				IMyCubeBlock Fatblock = block.FatBlock;

				//log("checking block: "+fatblock.DisplayNameText, "findBestFriendly()", Logger.severity.TRACE);

				string blockName = getBlockName(Fatblock);

				//log("checking " + blockName + " contains " + blockContains, "findBestFriendly()", Logger.severity.TRACE);
					log("compare match " + blockName + "(" + blockName.Length + ") to " + bestMatchLength, "findBestFriendly()", Logger.severity.TRACE);
					if ((bestMatchBlock == null || blockName.Length < bestMatchLength)) // if better match
					{
						bestMatchBlock = Fatblock;
						bestMatchLength = grid.DisplayName.Length;
					}
			}
			return bestMatchBlock != null;
		}

		private bool collect_findBestHostile(IMySlimBlock slim, string blockContains)
		{
			IMyCubeBlock Fatblock = slim.FatBlock;
			return Fatblock != null // not armour
				&& Fatblock.IsWorking // ignore inactive hostile blocks
				&& owner.currentRCblock.canConsiderHostile(Fatblock) // block must be hostile
				&& Fatblock.DefinitionDisplayNameText.looseContains(blockContains); // must contain blockContains
		}

		/// <summary>
		/// Finds the best matching block ordered by distance(in metres) + time since last seen(in millis).
		/// </summary>
		/// <param name="bestMatchBlock"></param>
		/// <param name="blockContains"></param>
		/// <returns></returns>
		public bool findBestHostile(IMyCubeGrid grid, out IMyCubeBlock bestMatchBlock, string blockContains)
		{
			List<IMySlimBlock> collected = new List<IMySlimBlock>();
			bestMatchBlock = null;
			double bestMatchDist = -1;
			grid.GetBlocks(collected, block => collect_findBestHostile(block, blockContains));

			foreach (IMySlimBlock block in collected)
			{
				IMyCubeBlock Fatblock = block.FatBlock;
				double distance = owner.myGrid.WorldAABB.Distance(Fatblock.GetPosition());
				if (bestMatchBlock == null || distance < bestMatchDist) // if better match
				{
					bestMatchBlock = Fatblock;
					bestMatchDist = distance;
				}
			}
			return bestMatchBlock != null;
		}
	}
}
