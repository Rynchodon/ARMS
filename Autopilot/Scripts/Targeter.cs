// skip file on build

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
			if (myLogger == null) myLogger = new Logger(owner.myGrid.DisplayName, "Targeter");
			myLogger.log(level, method, toLog);
		}

		private Navigator owner;

		internal Targeter(Navigator owner)
		{ this.owner = owner; }

		private IMyCubeGrid isInRange_last_grid = null;
		private Vector3D isInRange_last_position;
		private double isInRange_last_distance;

		// TODO: use AntennaRelay
		private bool isInRange(out double distance, bool friend, IMyCubeGrid targetGrid)
		{
			Vector3D position = owner.currentRCblock.GetPosition();
			if (targetGrid == isInRange_last_grid && position == isInRange_last_position)
			{
				distance = isInRange_last_distance;
			}
			else
			{
				distance = targetGrid.WorldAABB.Distance(position);
				isInRange_last_grid = targetGrid;
				isInRange_last_position = position;
				isInRange_last_distance = distance;
			}

			if (friend)
			{
				//log("got distance of " + distance + ", between " + owner.myGrid.DisplayName + " and " + targetGrid.DisplayName, "canTarget()", Logger.severity.TRACE);
				return distance < Settings.intSettings[Settings.IntSetName.iMaxLockOnRangeFriend];
			}
			else
			{
				if (owner.CNS.lockOnRangeEnemy < 1)
					owner.CNS.lockOnRangeEnemy = Settings.intSettings[Settings.IntSetName.iMaxLockOnRangeEnemy];
				//log("got distance of " + distance + ", between " + owner.myGrid.DisplayName + " and " + targetGrid.DisplayName, "canTarget()", Logger.severity.TRACE);
				return distance < owner.CNS.lockOnRangeEnemy;
			}
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="distance">distance to grid if no targetBlock, distance to block squared otherwise</param>
		/// <param name="friend"></param>
		/// <param name="targetGrid"></param>
		/// <param name="targetBlock"></param>
		/// <returns></returns>
		private bool canTarget(out double distance, bool friend, IMyCubeGrid targetGrid = null, IMyCubeBlock targetBlock = null)
		{
			//log("entered canTarget(" + friend + ")", "canTarget()", Logger.severity.TRACE);

			distance = -1;
			if (targetBlock == null)
			{
				if (targetGrid == null)
					return false; // need something to target
				//if (targetGrid.IsTrash()) // never seems to come up
				//{
				//	log("cannot lock onto trash: " + targetGrid.DisplayName, "canTarget()", Logger.severity.TRACE);
				//	return false;
				//}
				if (friend)
				{
					if (!owner.currentRCblock.canConsiderFriendly(targetGrid))
						return false;
				}
				else if (!owner.currentRCblock.canConsiderHostile(targetGrid))
					return false;

				if (!isInRange(out distance, friend, targetGrid))
				{
					//log ("not in range", "canTarget()", Logger.severity.TRACE);
					return false;
				}

				//log("can lock onto grid", "canTarget()", Logger.severity.TRACE);
				return true;
			}

			targetGrid = targetBlock.CubeGrid;

			// working test
			if (!friend && !targetBlock.IsWorking)
			{
				log("cannot lock onto non-working block: " + targetBlock.DisplayNameText, "canTarget()", Logger.severity.TRACE);
				return false;
			}

			// relations test
			if (friend)
			{
				//log("checking canConsiderFriendly: " + targetBlock.DisplayNameText, "canTarget()", Logger.severity.TRACE);
				if (!owner.currentRCblock.canConsiderFriendly(targetBlock))
				{
					log("cannot lock onto non-friend: " + targetBlock.DisplayNameText, "canTarget()", Logger.severity.TRACE);
					return false;
				}
			}
			else
			{
				//log("checking canConsiderHostile: " + targetBlock.DisplayNameText, "canTarget()", Logger.severity.TRACE);
				if (!owner.currentRCblock.canConsiderHostile(targetBlock))
				{
					log("cannot lock onto non-enemy: " + targetBlock.DisplayNameText, "canTarget()", Logger.severity.TRACE);
					return false;
				}
			}

			// distance test
			if (!isInRange(out distance, friend, targetGrid))
			{
				//log("got distance of " + distance + ", between " + owner.myGrid.DisplayName + " and " + targetGrid.DisplayName, "canTarget()", Logger.severity.TRACE);
				log("block's grid is out of range", "canTarget()", Logger.severity.TRACE);
				return false;
			}

			
			// passed all tests
			distance = (owner.currentRCblock.GetPosition() - targetBlock.GetPosition()).LengthSquared();
			//log("can lock onto block", "canTarget()", Logger.severity.TRACE);
			return true;
		}

		private static DateTime tryLockOnLastGlobal;
		private DateTime tryLockOnLastLocal;
		private static DateTime sanityCheckMinTime = DateTime.Today.AddDays(-1);

		public void tryLockOn()
		{
			//log("entered tryLockOn");

			if (owner.CNS.lockOnTarget == NavSettings.TARGET.OFF)
				return;

			DateTime now = DateTime.UtcNow;

			if (tryLockOnLastLocal > sanityCheckMinTime)
			{
				double secondsSinceLocalUpdate = (now - tryLockOnLastLocal).TotalSeconds;
				if (secondsSinceLocalUpdate < 1)
					return;
				double millisecondDelayGlobal = 9000 / secondsSinceLocalUpdate + 100;
				if (now < tryLockOnLastGlobal.AddMilliseconds(millisecondDelayGlobal))
					return;
			}
			else if (tryLockOnLastGlobal > sanityCheckMinTime && now < tryLockOnLastGlobal.AddMilliseconds(50)) // delay for first run
				return;

			log("trying to lock on type=" + owner.CNS.lockOnTarget, "tryLockOn()", Logger.severity.TRACE);
			tryLockOnLastGlobal = now;
			tryLockOnLastLocal = now;

			Sandbox.ModAPI.IMyCubeBlock closestBlock;
			Sandbox.ModAPI.IMyCubeGrid closestEnemy = findCubeGrid(out closestBlock, false, null, owner.CNS.lockOnBlock);
			if (closestEnemy == null)
			{
				double distance;
				if (owner.CNS.gridDestination != null && !canTarget(out distance, false, null, owner.CNS.closestBlock))
				{
					log("lost lock on " + owner.CNS.gridDestination + ":" + owner.CNS.getDestGridName());
					owner.CNS.atWayDest();
				}
				return;
			}

			// found an enemy, setting as destination
			if (closestBlock != null)
				log("found an enemy: " + closestEnemy.DisplayName + ":" + closestBlock.DisplayNameText, "tryLockOn()");
			else
				log("found an enemy: " + closestEnemy.DisplayName, "tryLockOn()");
			owner.CNS.setDestination(closestBlock, closestEnemy);

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

		public Sandbox.ModAPI.IMyCubeGrid findCubeGrid(out Sandbox.ModAPI.IMyCubeBlock closestBlock, bool friend = true, string gridNameContains = null, string blockContains = null)
		{
			closestBlock = null;

			Dictionary<double, Sandbox.ModAPI.IMyCubeGrid> nearbyGrids = new Dictionary<double, Sandbox.ModAPI.IMyCubeGrid>();

			HashSet<IMyEntity> entities = new HashSet<IMyEntity>();
			MyAPIGateway.Entities.GetEntities(entities);
			foreach (IMyEntity entity in entities)
			{
				IMyCubeGrid grid = entity as IMyCubeGrid;
				if (grid == null)
					continue;
				if (owner.myGrid == grid)
					continue;

				double distance;
				if (!canTarget(out distance, friend, grid))
					continue;
				if (gridNameContains == null || Navigator.looseContains(grid.DisplayName, gridNameContains))
				{
					//log("adding to nearbyGrids: " + grid.DisplayName, "findCubeGrid()", Logger.severity.TRACE);
					bool added = false;
					while (!added)
					{
						try
						{
							nearbyGrids.Add(distance, grid);
							added = true;
						}
						catch (ArgumentException) { distance += 0.001; }
					}
				}
			}
			
			if (nearbyGrids.Count > 0)
				foreach (KeyValuePair<double, Sandbox.ModAPI.IMyCubeGrid> pair in nearbyGrids.OrderBy(i => i.Key))
				{
					log("checking pair: " + pair.Key + ", " + pair.Value.DisplayName + ". block contains=" + blockContains, "findCubeGrid()", Logger.severity.TRACE);
					if (blockContains == null || findClosestCubeBlockOnGrid(out closestBlock, pair.Value, blockContains, friend))
					{
						log("pair OK", "findCubeGrid()", Logger.severity.TRACE);
						return pair.Value;
					}
				}

			return null;
		}

		/// <summary>
		/// finds the closest block on a grid that contains the specified string
		/// </summary>
		/// <param name="grid"></param>
		/// <param name="blockContains"></param>
		/// <param name="friend"></param>
		/// <returns></returns>
		public bool findClosestCubeBlockOnGrid(out Sandbox.ModAPI.IMyCubeBlock closestBlock, Sandbox.ModAPI.IMyCubeGrid grid, string blockContains, bool friend) //, bool getAny = false)
		{
			List<Sandbox.ModAPI.IMySlimBlock> allBlocks = new List<Sandbox.ModAPI.IMySlimBlock>();
			closestBlock = null;
			double distanceToClosest = 0;
			grid.GetBlocks(allBlocks);
			foreach (Sandbox.ModAPI.IMySlimBlock blockInGrid in allBlocks)
			{
				Sandbox.ModAPI.IMyCubeBlock fatBlock = blockInGrid.FatBlock;
				if (fatBlock == null || fatBlock == owner.currentRCblock)
					continue;

				double distance;
				if (!canTarget(out distance, friend, null, fatBlock))
					continue;

				string toSearch;
				if (friend)
				{
					if (fatBlock is Ingame.IMyRemoteControl)
						toSearch = Navigator.getRCNameOnly(fatBlock);
					else
						toSearch = fatBlock.DisplayNameText;
				}
				else
					toSearch = fatBlock.DefinitionDisplayNameText;
				
				//toSearch = toSearch.ToLower();
				if (Navigator.looseContains(toSearch, blockContains))
				{
					//log("got a match for " + blockContains + ", match is " + toSearch, "findClosestCubeBlockOnGrid()", Logger.severity.TRACE);
					//double distance = myGridDim.getRCdistanceTo(fatBlock);
					if (closestBlock == null || distance < distanceToClosest)
					{
						closestBlock = fatBlock;
						distanceToClosest = distance;
					}
				}
				//else
				//	log("did not match " + toSearch + " to " + blockContains, "findClosestCubeBlockOnGrid()", Logger.severity.TRACE);
			}
			return (closestBlock != null);
		}
	}
}
