#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

//using Sandbox.Common;
//using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
//using Sandbox.Common.ObjectBuilders.Definitions;
//using Sandbox.Definitions;
//using Sandbox.Engine;
//using Sandbox.Game;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;
//using Sandbox.ModAPI.Interfaces;
using VRageMath;

namespace Rynchodon.Autopilot
{
	class Lander
	{
		private Logger myLogger = null;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null)
				myLogger = new Logger(myGrid.DisplayName, "Lander");
			myLogger.log(level, method, toLog);
		}
		private void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null)
				myLogger = new Logger(myGrid.DisplayName, "Lander");
			myLogger.log(level, method, toLog);
		}
		private void alwaysLog(Logger.severity level, string method, string toLog)
		{
			if (myLogger == null)
				myLogger = new Logger(myGrid.DisplayName, "Lander");
			myLogger.log(level, method, toLog);
		}

		private Navigator myNav;
		private NavSettings CNS;
		private IMyCubeGrid myGrid;

		public Lander(Navigator myNav)
		{
			this.myNav = myNav;
			this.CNS = myNav.CNS;
			this.myGrid = myNav.myGrid;
		}

		internal Vector3D? targetDirection = null;
		private Vector3D? targetRoll = null;
		private bool matchOrientation_finished_rotating = false;

		private const float rotLenSq_orientRota = 0.00762f; // 5°
		private const float rotLen_orientRoll = 0.0873f; // 5°

		public void matchOrientation()
		{
			if (targetDirection == null)
			{
				CNS.getOrientationOfDest(out targetDirection, out targetRoll);
				if (targetDirection == null)
				{ matchOrientation_clear(); return; }
				log("targetDirection=" + targetDirection + ", targetRoll=" + targetRoll, "matchOrientation()", Logger.severity.DEBUG);
				matchOrientation_finished_rotating = false;
			}
			myNav.MM = new MovementMeasure(myNav, targetDirection);
			if (!matchOrientation_finished_rotating && (CNS.rotateState != NavSettings.Rotating.NOT_ROTA || myNav.MM.rotLenSq > rotLenSq_orientRota))
			{
				myNav.calcAndRotate();
			}
			else
			{
				if (!matchOrientation_finished_rotating)
				{
					log("direction successfully matched pitch=" + myNav.MM.pitch + ", yaw=" + myNav.MM.yaw + ", " + myNav.MM.rotLenSq + " < " + rotLenSq_orientRota, "matchOrientation()", Logger.severity.DEBUG);
					//fullStop();
					matchOrientation_finished_rotating = true;
				}

				if (targetRoll == null)
				{ matchOrientation_clear(); return; }

				// roll time
				double up = myNav.currentRCblock.WorldMatrix.Up.Dot((Vector3D)targetRoll);
				double right = myNav.currentRCblock.WorldMatrix.Right.Dot((Vector3D)targetRoll);
				double roll = Math.Atan2(right, up);
				if (CNS.rollState != NavSettings.Rolling.NOT_ROLL || Math.Abs(roll) > rotLen_orientRoll)
				{
					//log("roll=" + roll, "matchOrientation()", Logger.severity.TRACE);
					myNav.calcAndRoll((float)roll);
				}
				else
				{
					log("roll successfully matched, roll=" + roll + ", rotLen_orient = " + rotLen_orientRoll + ", up = " + myNav.currentRCblock.WorldMatrix.Up + ", target = " + targetRoll, "matchOrientation()", Logger.severity.DEBUG);
					//fullStop();
					matchOrientation_clear(); return;
				}
			}
		}

		private void matchOrientation_clear()
		{
			log("clearing", "matchOrientation_clear()", Logger.severity.DEBUG);
			CNS.match_direction = null;
			CNS.match_roll = null;
			targetDirection = null;
			targetRoll = null;
		}

		public void landGrid(MovementMeasure forDistNav)
		{
			if ((CNS.landLocalBlock != null && (CNS.landLocalBlock.Closed || !CNS.landLocalBlock.IsFunctional)) // test landLocalBlock for functional, we can turn it on
				|| (CNS.CurrentGridDest != null && CNS.CurrentGridDest.Block != null && (CNS.CurrentGridDest.Block.Closed || !CNS.CurrentGridDest.Block.IsWorking)))
			{
				log("cannot land with broken block. " + CNS.landLocalBlock.Closed + " : " + CNS.CurrentGridDest.Block.Closed + " : " + !CNS.landLocalBlock.IsFunctional + " && " + !CNS.CurrentGridDest.Block.IsWorking, "landGrid()", Logger.severity.DEBUG);
				CNS.landLocalBlock = null;
				CNS.landingState = NavSettings.LANDING.OFF;
				if (CNS.landingSeparateBlock != null)
					unlockLanding();
				return;
			}

			switch (CNS.landingState)
			{
				case NavSettings.LANDING.OFF:
					{
						log("started landing procedures local=" + CNS.landLocalBlock.DisplayNameText + ", target=" + CNS.BlockDestName, "landGrid()", Logger.severity.DEBUG);
						CNS.landingSeparateWaypoint = CNS.getWayDest();
						CNS.landingSeparateBlock = CNS.landLocalBlock;
						calcOrientationFromBlockDirection(CNS.landLocalBlock);
						matchOrientation(); // start
						CNS.landingState = NavSettings.LANDING.ORIENT;
						//CNS.collisionUpdateSinceWaypointAdded = 1000; // will not be calling collision
						//mergeMonitor.clearMergeStatus();
						return;
					}
				case NavSettings.LANDING.ORIENT:
					{
						if (targetDirection != null)
							matchOrientation(); //continue
						else
						{
							log("starting to land", "landGrid()", Logger.severity.DEBUG);
							CNS.landingState = NavSettings.LANDING.LINEUP;
						}
						return;
					}
				case NavSettings.LANDING.LINEUP:
					//log("LINEUP: getDistNavToWayDest()=" + getDistNavToWayDest(), "landGrid()", Logger.severity.TRACE);
					//myLogger.debugLog("Lining up, distance to wayDest = " + forDistNav.distToWayDest, "landGrid()");
					if (forDistNav.distToWayDest < 1)
					{
						if (CNS.moveState == NavSettings.Moving.NOT_MOVE)
							CNS.landingState = NavSettings.LANDING.LAND;
						else
							myNav.fullStop("lineup: within range");
						return;
					}
					goto case NavSettings.LANDING.LAND;
				case NavSettings.LANDING.LAND:
					{
						if (lockLanding())
						{
							//log("locked", "landGrid()", Logger.severity.DEBUG);
							CNS.landingState = NavSettings.LANDING.LOCKED;
							myNav.reportState(Navigator.ReportableState.LANDED);
							myNav.fullStop("landed");
							myNav.EnableDampeners(false); // dampeners should be off while docked, incase another grid is to fly
							CNS.atWayDest(NavSettings.TypeOfWayDest.LAND);
							CNS.waitUntilNoCheck = DateTime.UtcNow.AddSeconds(1);
							return;
						}
						//log("landing", "landGrid()", Logger.severity.TRACE);
						myNav.collisionCheckMoveAndRotate();
						return;
					}
				// do not access CNS.landLocalBlock or CNS.landOffset after this point
				case NavSettings.LANDING.LOCKED:
					{
						//log("separate landing", "landGrid()", Logger.severity.DEBUG);
						if (!unlockLanding())
							return;
						if (CNS.landingSeparateWaypoint == null)
						{
							alwaysLog(Logger.severity.ERROR, "landGrid()", "landingSeparateWaypoint == null");
							return;
						}
						else // waypoint exists
							//if (CNS.addWaypoint((Vector3D)CNS.landingSeparateWaypoint))
							//	log("added separate waypoint", "landGrid()", Logger.severity.TRACE);
							//else
							//{
							//	alwaysLog(Logger.severity.ERROR, "landGrid()", "failed to add separate waypoint");
							//	return;
							//}
							CNS.setWaypoint((Vector3D)CNS.landingSeparateWaypoint);
						CNS.landingState = NavSettings.LANDING.SEPARATE;
						return;
					}
				case NavSettings.LANDING.SEPARATE:
					{
						if (CNS.moveState == NavSettings.Moving.NOT_MOVE && forDistNav.distToWayDest < Navigator.radiusLandWay)
						{
							myNav.fullStop("At Dest: Separated");
							CNS.landingState = NavSettings.LANDING.OFF;
							CNS.landingSeparateBlock = null;
							CNS.landingSeparateWaypoint = null;
							CNS.atWayDest(NavSettings.TypeOfWayDest.WAYPOINT);
							//log("separated, landing procedures completed. target="+CNS.closestBlock.DisplayNameText+", local=" + CNS.landLocalBlock.DisplayNameText + ", offset=" + CNS.landOffset, "landGrid()", Logger.severity.DEBUG);
							return;
						}
						//log("moving to separate waypoint", "landGrid()", Logger.severity.TRACE);
						myNav.collisionCheckMoveAndRotate();
						return;
					}
			}
		}

		private MyObjectBuilder_Character hasPilot_value = null;

		private bool hasPilot()
		{
			List<Sandbox.ModAPI.IMySlimBlock> allCockpits = new List<Sandbox.ModAPI.IMySlimBlock>();
			myGrid.GetBlocks(allCockpits, block => block.FatBlock != null && block.FatBlock is Ingame.IMyCockpit);
			foreach (IMySlimBlock cockpit in allCockpits)
			{
				MyObjectBuilder_Character pilot = (cockpit.FatBlock.GetObjectBuilderCubeBlock() as MyObjectBuilder_Cockpit).Pilot;
				if (pilot != null)
				{
					if (hasPilot_value != pilot)
						log("got a pilot in " + cockpit.FatBlock.DisplayNameText + ", pilot is " + pilot.DisplayName, "hasPilot()", Logger.severity.DEBUG);
					hasPilot_value = pilot;
					return true;
				}
			}
			hasPilot_value = null;
			return false;
		}

		private bool lockLanding()
		{
			Ingame.IMyShipConnector connector = CNS.landLocalBlock as Ingame.IMyShipConnector;
			if (connector != null)
			{
				if (!connector.IsLocked) // this actually checks for ready to lock (yellow light)
				{
					connector.GetActionWithName("OnOff_On").Apply(connector); // on
					return false;
				}
				log("trying to lock connector", "lockLanding()", Logger.severity.TRACE);
				connector.GetActionWithName("SwitchLock").Apply(connector); // lock
				return true;
			}

			Ingame.IMyLandingGear landingGear = CNS.landLocalBlock as Ingame.IMyLandingGear;
			if (landingGear != null)
			{
				MyObjectBuilder_LandingGear builder = CNS.landLocalBlock.GetObjectBuilderCubeBlock() as MyObjectBuilder_LandingGear;
				landingGear.GetActionWithName("OnOff_On").Apply(landingGear); // on
				if (!builder.AutoLock)
				{
					log("setting autolock", "lockLanding()", Logger.severity.TRACE);
					landingGear.GetActionWithName("Autolock").Apply(landingGear); // autolock on
					return false;
				}
				return builder.IsLocked;
			}

			Ingame.IMyShipMergeBlock mergeBlock = CNS.landLocalBlock as Ingame.IMyShipMergeBlock;
			if (mergeBlock != null)
			{
				MyObjectBuilder_MergeBlock builder = CNS.landLocalBlock.GetObjectBuilderCubeBlock() as MyObjectBuilder_MergeBlock;
				if (builder.SubBlocks != null && builder.SubBlocks.Length > 0)
					log("subblock[0]=" + builder.SubBlocks[0].SubGridName, "lockLanding()", Logger.severity.TRACE);
				if (!mergeBlock.IsFunctional || !mergeBlock.IsWorking) //&& mergeMonitor.mergeStatus == MergeMonitor.MergeStatus.OFF)
				{
					log("merge block set", "lockLanding()", Logger.severity.TRACE);
					mergeBlock.GetActionWithName("OnOff_On").Apply(mergeBlock); // on
				}
				return false;
			}

			log("unknown lander block type " + CNS.landLocalBlock.DefinitionDisplayNameText, "lockLanding()", Logger.severity.INFO);
			return true; // assume there is nothing to lock
		}

		private bool unlockLanding()
		{
			if (CNS.landingSeparateBlock == null)
			{
				alwaysLog("CNS.landingSeparateBlock == null", "unlockLanding()", Logger.severity.FATAL);
				return false; // do not allow Navigator to proceed
			}

			Ingame.IMyShipConnector connector = CNS.landingSeparateBlock as Ingame.IMyShipConnector;
			if (connector != null)
			{
				// due to a bug in Space Engineers, Autopilot should not unlock a connector while a player is in any passenger seat
				if (hasPilot())
				{
					myNav.GET_OUT_OF_SEAT = true;
					myNav.reportState(Navigator.ReportableState.GET_OUT_OF_SEAT);
					return false;
				}
				myNav.GET_OUT_OF_SEAT = false;

				bool disconnected = true;
				if ((CNS.landingSeparateBlock.GetObjectBuilderCubeBlock() as MyObjectBuilder_ShipConnector).Connected)
				{
					disconnected = false;
					log("switching lock", "unlockLanding()", Logger.severity.TRACE);
					connector.GetActionWithName("SwitchLock").Apply(connector); // unlock
				}
				if ((CNS.landingSeparateBlock as Ingame.IMyFunctionalBlock).Enabled)
				{
					disconnected = false;
					log("turning off", "unlockLanding()", Logger.severity.TRACE);
					connector.GetActionWithName("OnOff_Off").Apply(connector); // off
				}
				return disconnected;
			}

			Ingame.IMyLandingGear landingGear = CNS.landingSeparateBlock as Ingame.IMyLandingGear;
			if (landingGear != null)
			{
				// due to a bug in Space Engineers, Autopilot should not unlock a landing gear while a player is in any passenger seat
				if (hasPilot())
				{
					myNav.GET_OUT_OF_SEAT = true;
					myNav.reportState(Navigator.ReportableState.GET_OUT_OF_SEAT);
					return false;
				}
				myNav.GET_OUT_OF_SEAT = false;

				bool disconnected = true;
				MyObjectBuilder_LandingGear builder = CNS.landingSeparateBlock.GetObjectBuilderCubeBlock() as MyObjectBuilder_LandingGear;
				if (builder.AutoLock)
				{
					disconnected = false;
					log("autolock off", "unlockLanding()", Logger.severity.TRACE);
					landingGear.GetActionWithName("Autolock").Apply(landingGear); // autolock off
				}
				if (builder.IsLocked)
				{
					disconnected = false;
					log("landing gear switching lock", "unlockLanding()", Logger.severity.TRACE);
					landingGear.GetActionWithName("SwitchLock").Apply(landingGear); // unlock
				}
				return disconnected;
			}

			Ingame.IMyShipMergeBlock mergeBlock = CNS.landingSeparateBlock as Ingame.IMyShipMergeBlock;
			if (mergeBlock != null)
			{
				bool disconnected = true;
				if ((CNS.landingSeparateBlock as Ingame.IMyFunctionalBlock).Enabled)
				{
					disconnected = false;
					log("turning off merge block", "unlockLanding()", Logger.severity.TRACE);
					mergeBlock.GetActionWithName("OnOff_Off").Apply(connector); // off
				}
				return disconnected;
			}

			log("unknown separate block type: " + CNS.landingSeparateBlock.DefinitionDisplayNameText, "unlockLanding()", Logger.severity.INFO);
			return true; // assume there is nothing to unlock
		}

		public static bool getDirFromOri(MyBlockOrientation orientation, Base6Directions.Direction direction, out Base6Directions.Direction? result)
		{
			switch (direction)
			{
				case Base6Directions.Direction.Left:
					result = orientation.Left;
					break;
				case Base6Directions.Direction.Up:
					result = orientation.Up;
					break;
				case Base6Directions.Direction.Forward:
					result = orientation.Forward;
					break;
				case Base6Directions.Direction.Right:
					result = Base6Directions.GetFlippedDirection(orientation.Left);
					break;
				case Base6Directions.Direction.Down:
					result = Base6Directions.GetFlippedDirection(orientation.Up);
					break;
				case Base6Directions.Direction.Backward:
					result = Base6Directions.GetFlippedDirection(orientation.Forward);
					break;
				default:
					result = null;
					return false;
			}
			return true;
		}

		public static bool landingDirection(IMyCubeBlock block, out  Base6Directions.Direction? result )
		{
			if (block is Ingame.IMyShipConnector)
			{
				result = Base6Directions.Direction.Forward;
				return true;
			}
			if (block is Ingame.IMyLandingGear)
			{
				result = Base6Directions.Direction.Down;
				return true;
			}
			if (block is Ingame.IMyShipMergeBlock)
			{
				//if (block.CubeGrid.GridSizeEnum == MyCubeSize.Large)
				//{ } //return Base6Directions.Direction.Right;
				//else // small grid
				//	(new Logger(block.CubeGrid.DisplayName, "Lander")).log(Logger.severity.TRACE, "landingDirection()", "small ship");
				result = Base6Directions.Direction.Right;
				return true;
			}

			(new Logger(block.CubeGrid.DisplayName, "Lander")).log(Logger.severity.DEBUG, "landingDirection()", "failed to get direction for block: " + block.DefinitionDisplayNameText);
			result = null;
			return false;
		}

		public static bool landingDirectionLocal(IMyCubeBlock block, out Base6Directions.Direction? result)
		{
			Base6Directions.Direction? intermediate;
			if (!landingDirection(block, out intermediate))
			{
				result = null;
				return false;
			}
			return getDirFromOri(block.Orientation, (Base6Directions.Direction)intermediate, out result);
		}

		private bool direction_RCfromGrid(Base6Directions.Direction? gridDirection, out  Base6Directions.Direction? result)
		{
			if (gridDirection == null)
			{
				result = null;
				return false;
			}

			IMyCubeBlock myRC = myNav.currentRCblock;
			if (myRC.Orientation.Left == gridDirection)
			{
				result = Base6Directions.Direction.Left;
				return true;
			}
			if (Base6Directions.GetFlippedDirection(myRC.Orientation.Left) == gridDirection)
			{
				result = Base6Directions.Direction.Right;
				return true;
			}
			if (myRC.Orientation.Up == gridDirection)
			{
				result = Base6Directions.Direction.Up;
				return true;
			}
			if (Base6Directions.GetFlippedDirection(myRC.Orientation.Up) == gridDirection)
			{
				result = Base6Directions.Direction.Down;
				return true;
			}
			if (myRC.Orientation.Forward == gridDirection)
			{
				result = Base6Directions.Direction.Forward;
				return true;
			}
			if (Base6Directions.GetFlippedDirection(myRC.Orientation.Forward) == gridDirection)
			{
				result = Base6Directions.Direction.Backward;
				return true;
			}

			myLogger.log(Logger.severity.ERROR, "direction_RCfromGrid", "failed to match direction: " + gridDirection);
			result = null;
			return false;
		}

		private void calcOrientationFromBlockDirection(IMyCubeBlock block)
		{
			if (CNS.match_direction != null)
			{
				log("already have an orientation: " + CNS.match_direction + ":" + CNS.match_roll, "calcOrientationFromBlockDirection()", Logger.severity.TRACE);
				return;
			}

			Base6Directions.Direction? landDirLocal;
			if (!landingDirectionLocal(block, out landDirLocal))
			{
				log("could not get landing direction from block: " + block.DefinitionDisplayNameText, "calcOrientationFromBlockDirection()", Logger.severity.INFO);
				return;
			}
			Base6Directions.Direction? blockDirection;
			direction_RCfromGrid(landDirLocal, out blockDirection);

			switch (blockDirection)
			{
				case Base6Directions.Direction.Forward:
					CNS.match_direction = Base6Directions.GetFlippedDirection((Base6Directions.Direction)CNS.landDirection); //CNS.match_direction = Base6Directions.Direction.Backward;
					// roll is irrelevant
					break;
				case Base6Directions.Direction.Backward:
					CNS.match_direction = CNS.landDirection; //CNS.match_direction = Base6Directions.Direction.Forward;
					// roll is irrelevant
					break;
				case Base6Directions.Direction.Up:
					CNS.match_direction = Base6Directions.GetPerpendicular((Base6Directions.Direction)CNS.landDirection); //CNS.match_direction = Base6Directions.Direction.Up;
					CNS.match_roll = Base6Directions.GetFlippedDirection((Base6Directions.Direction)CNS.landDirection); //CNS.match_roll = Base6Directions.Direction.Backward;
					break;
				case Base6Directions.Direction.Down:
					CNS.match_direction = Base6Directions.GetPerpendicular((Base6Directions.Direction)CNS.landDirection); //CNS.match_direction = Base6Directions.Direction.Down;
					CNS.match_roll = CNS.landDirection; //CNS.match_direction = Base6Directions.Direction.Forward;
					break;
				case Base6Directions.Direction.Left:
					CNS.match_direction = Base6Directions.GetPerpendicular((Base6Directions.Direction)CNS.landDirection); //CNS.match_roll = Base6Directions.Direction.Up;
					CNS.match_roll = Base6Directions.GetFlippedDirection(Base6Directions.GetCross((Base6Directions.Direction)CNS.match_direction, (Base6Directions.Direction)CNS.landDirection)); //CNS.match_direction = Base6Directions.Direction.Left;
					break;
				case Base6Directions.Direction.Right:
					CNS.match_direction = Base6Directions.GetPerpendicular((Base6Directions.Direction)CNS.landDirection); //CNS.match_roll = Base6Directions.Direction.Up;
					CNS.match_roll = Base6Directions.GetCross((Base6Directions.Direction)CNS.match_direction, (Base6Directions.Direction)CNS.landDirection); //CNS.match_direction = Base6Directions.Direction.Right;
					break;
			}
			log("landDirection = " + landDirLocal + ", blockDirection = " + blockDirection + ", match_direction = " + CNS.match_direction + ", match_roll = " + CNS.match_roll, "calcOrientationFromBlockDirection()", Logger.severity.DEBUG);
		}
	}
}
