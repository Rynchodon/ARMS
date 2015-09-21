#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Autopilot.Harvest;
using Rynchodon.Autopilot.Instruction;
using Rynchodon.Autopilot.NavigationSettings;
using Rynchodon.Settings;
using Rynchodon.Weapons;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Autopilot
{
	public class Navigator
	{
		private Logger myLogger = null;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void debugLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ myLogger.debugLog(toLog, method, level); }
		private void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.WARNING)
		{ myLogger.alwaysLog(toLog, method, level); }

		public Sandbox.ModAPI.IMyCubeGrid myGrid { get; private set; }

		private List<Sandbox.ModAPI.IMySlimBlock> autopilotBlocks;

		/// <summary>
		/// overrids fetching commands from display name when true
		/// </summary>
		public bool AIOverride = false;

		/// <summary>
		/// current navigation settings
		/// </summary>
		internal NavSettings CNS;
		private Pathfinder.Pathfinder myPathfinder;
		internal Pathfinder.PathfinderOutput myPathfinder_Output { get; private set; }
		internal ThrustProfiler currentThrust;
		internal GridTargeter myTargeter;
		private Rotator myRotator;
		internal HarvesterAsteroid myHarvester { get; private set; }
		internal Engager myEngager;

		private IMyControllableEntity currentAutopilotBlock_Value;
		/// <summary>
		/// Primary remote control value.
		/// </summary>
		public IMyControllableEntity currentAPcontrollable
		{
			get { return currentAutopilotBlock_Value; }
			set
			{
				if (currentAutopilotBlock_Value == value)
					return;

				if (currentAutopilotBlock_Value != null)
				{
					// actions on old RC
					fullStop("unsetting RC");
					reportState(ReportableState.Off, true);
				}

				currentAutopilotBlock_Value = value;
				myLand = null;
				if (currentAutopilotBlock_Value == null)
				{
					myPathfinder = null;
					CNS = new NavSettings(null);
				}
				else
				{
					myPathfinder = new Pathfinder.Pathfinder(myGrid);
					CNS = new NavSettings(this);
					myLogger.debugLog("have a new RC: " + currentAPblock.getNameOnly(), "set_currentRCcontrol()");

					// actions on new RC
					fullStop("new RC");
					reportState(ReportableState.Off, true);
				}

				myRotator = new Rotator(this);
				myHarvester = new HarvesterAsteroid(this);
			}
		}
		/// <summary>
		/// Secondary remote control value.
		/// </summary>
		public Sandbox.ModAPI.IMyCubeBlock currentAPblock
		{
			get { return currentAutopilotBlock_Value as Sandbox.ModAPI.IMyCubeBlock; }
			set { currentAPcontrollable = value as IMyControllableEntity; }
		}
		/// <summary>
		/// Secondary remote control value.
		/// </summary>
		public IMyTerminalBlock currentAPterminal
		{ get { return currentAutopilotBlock_Value as IMyTerminalBlock; } }
		public Ingame.IMyShipController currentAPcontroller
		{ get { return currentAutopilotBlock_Value as Ingame.IMyShipController; } }

		/// <summary>
		/// only use for position or distance, for rotation it is simpler to only use RC directions
		/// </summary>
		/// <returns></returns>
		public Sandbox.ModAPI.IMyCubeBlock getNavigationBlock()
		{
			if (CNS.landingState == NavSettings.LANDING.OFF || CNS.landLocalBlock == null)
			{
				if (myHarvester.NavigationDrill != null)
					return myHarvester.NavigationDrill;
				if (myEngager.CurrentStage == Engager.Stage.Engaging)
				{
					FixedWeapon primary = myEngager.GetPrimaryWeapon();
					if (primary != null)
						return primary.CubeBlock;
				}
				return currentAPblock;
			}
			else
			{
				//log("using "+CNS.landLocalBlock.DisplayNameText+" as navigation block");
				if (CNS.landingSeparateBlock != null)
					return CNS.landingSeparateBlock;
				return CNS.landLocalBlock;
			}
		}

		internal Navigator(Sandbox.ModAPI.IMyCubeGrid grid)
		{
			myGrid = grid;
			myLogger = new Logger("Navigator", () => myGrid.DisplayName, () => { return CNS.moveState.ToString() + ':' + CNS.rotateState.ToString(); }, () => CNS.landingState.ToString());
		}

		private bool needToInit = true;

		private void init()
		{
			//	find remote control blocks
			autopilotBlocks = new List<Sandbox.ModAPI.IMySlimBlock>();
			myGrid.GetBlocks(autopilotBlocks, block => IsControllableBlock(block.FatBlock));

			// register for events
			myGrid.OnBlockAdded += OnBlockAdded;
			myGrid.OnBlockRemoved += OnBlockRemoved;
			myGrid.OnClose += OnClose;

			myEngager = new Engager(myGrid);
			currentThrust = new ThrustProfiler(myGrid);
			CNS = new NavSettings(null);
			myTargeter = new GridTargeter(this);
			myInterpreter = new Interpreter(this);
			needToInit = false;
		}

		internal void Close()
		{
			myLogger.debugLog("entered Close()", "Close()");
			if (myGrid != null)
			{
				myGrid.OnClose -= OnClose;
				myGrid.OnBlockAdded -= OnBlockAdded;
				myGrid.OnBlockRemoved -= OnBlockRemoved;
			}
			currentAPcontrollable = null;
			if (myEngager != null)
				myEngager.Disarm();
		}

		private void OnClose()
		{
			myLogger.debugLog("entered OnClose()", "OnClose()");
			Close();
			Core.remove(this);
			myLogger.debugLog("leaving OnClose()", "OnClose()");
		}

		private void OnClose(IMyEntity closing)
		{ try { OnClose(); } catch { } }

		private void OnBlockAdded(Sandbox.ModAPI.IMySlimBlock addedBlock)
		{
			if (IsControllableBlock(addedBlock.FatBlock))
				autopilotBlocks.Add(addedBlock);
		}

		private void OnBlockRemoved(Sandbox.ModAPI.IMySlimBlock removedBlock)
		{
			if (IsControllableBlock(removedBlock.FatBlock))
				autopilotBlocks.Remove(removedBlock);
		}

		private long updateCount = 0;
		private bool previous_gridCanNavigate = false;

		/// <summary>
		/// Causes the ship to fly around, following commands.
		/// </summary>
		/// <remarks>
		/// Calling more often means more precise movements, calling too often (~ every update) will break functionality.
		/// </remarks>
		public void update()
		{
			updateCount++;
			reportState();
			if (needToInit)
				init();
			if (CNS.lockOnTarget != NavSettings.TARGET.OFF)
				myTargeter.tryLockOn();
			if (CNS.waitUntilNoCheck.CompareTo(DateTime.UtcNow) > 0)
				return;
			if (CNS.waitUntil.CompareTo(DateTime.UtcNow) > 0 || CNS.EXIT)
			{
				if (!autopilotBlockIsReady(currentAPblock)) // if something changes, stop waiting!
					reset("wait interrupted");
				return;
			}
			if (gridCanNavigate())
			{
				previous_gridCanNavigate = true;
			}
			else
			{
				if (previous_gridCanNavigate)
				{
					reset("grid lost ability to navigate");
					previous_gridCanNavigate = false;
				}
				return;
			}

			if (CNS.getTypeOfWayDest() != NavSettings.TypeOfWayDest.NONE)
				navigate();
			else // no waypoints
			{
				if (currentAPcontrollable != null && myInterpreter.hasInstructions())
				{
					while (myInterpreter.hasInstructions())
					{
						//myLogger.debugLog("invoking instruction: " + myInterpreter.getCurrentInstructionString(), "update()");
						Action instruction = myInterpreter.instructionQueue.Dequeue();
						try { instruction.Invoke(); }
						catch (Exception ex)
						{
							myLogger.alwaysLog("Exception while invoking instruction: " + ex, "update()", Logger.severity.ERROR);
							continue;
						}
						switch (CNS.getTypeOfWayDest())
						{
							case NavSettings.TypeOfWayDest.BLOCK:
								debugLog("got a block as a destination: " + CNS.GridDestName, "update()", Logger.severity.INFO);
								return;
							case NavSettings.TypeOfWayDest.OFFSET:
								debugLog("got an offset as a destination: " + CNS.GridDestName + ":" + CNS.BlockDestName + ":" + CNS.destination_offset, "update()", Logger.severity.INFO);
								return;
							case NavSettings.TypeOfWayDest.GRID:
								debugLog("got a grid as a destination: " + CNS.GridDestName, "update()", Logger.severity.INFO);
								return;
							case NavSettings.TypeOfWayDest.COORDINATES:
								debugLog("got a new destination " + CNS.getWayDest(), "update()", Logger.severity.INFO);
								return;
							case NavSettings.TypeOfWayDest.LAND:
								debugLog("got a new landing destination " + CNS.getWayDest(), "update()", Logger.severity.INFO);
								return;
							case NavSettings.TypeOfWayDest.NONE:
								break; // keep searching
							case NavSettings.TypeOfWayDest.WAYPOINT:
								debugLog("got a new waypoint destination (harvesting) " + CNS.getWayDest(), "update()", Logger.severity.INFO);
								return;
							default:
								alwaysLog("got an invalid TypeOfWayDest: " + CNS.getTypeOfWayDest(), "update()", Logger.severity.FATAL);
								return;
						}
						if (CNS.waitUntil.CompareTo(DateTime.UtcNow) > 0)
						{
							myLogger.debugLog("Waiting for " + (CNS.waitUntil - DateTime.UtcNow), "update()", Logger.severity.DEBUG);
							return;
						}
					}
					// at end of allInstructions
					CNS.waitUntilNoCheck = DateTime.UtcNow.AddSeconds(1);
					return;
				}
				else
				{
					// find a remote control with NavSettings allInstructions
					CNS.waitUntilNoCheck = DateTime.UtcNow.AddSeconds(1);
					foreach (Sandbox.ModAPI.IMySlimBlock apBlock in autopilotBlocks)
					{
						Sandbox.ModAPI.IMyCubeBlock fatBlock = apBlock.FatBlock;
						if (autopilotBlockIsReady(fatBlock))
						{
							if (AIOverride)
							{
								if (currentAPcontrollable == null)
								{
									myLogger.debugLog("chose a block for AIOverride", "update()");
									currentAPcontrollable = (fatBlock as IMyControllableEntity);
								}
							}
							else
							{
								//	parse display name
								string instructions = fatBlock.getInstructions();
								if (string.IsNullOrWhiteSpace(instructions))
									continue;

								myLogger.debugLog("trying block: " + fatBlock.DisplayNameText, "update()");
								currentAPcontrollable = (fatBlock as IMyControllableEntity); // necessary to enqueue actions
								if (myInterpreter == null)
									myInterpreter = new Interpreter(this);
								myInterpreter.enqueueAllActions(fatBlock);
								if (myInterpreter.hasInstructions())
								{
									CNS.startOfCommands();
									debugLog("remote control: " + fatBlock.getNameOnly() + " finished queuing " + myInterpreter.instructionQueue.Count + " instruction", "update()", Logger.severity.TRACE);
									return;
								}
								myLogger.debugLog("failed to enqueue actions from " + fatBlock.getNameOnly(), "update()", Logger.severity.DEBUG);
								currentAPcontrollable = null;
								continue;
							}
						}
					}
					// failed to find a ready remote control
				}
			}
		}

		private Interpreter myInterpreter;

		public static bool looseContains(string bigstring, string substring)
		{
			bigstring = bigstring.ToLower().Replace(" ", "");
			substring = substring.ToLower().Replace(" ", "");

			return bigstring.Contains(substring);
		}

		private bool player_controlling = false;

		/// <summary>
		/// checks for is a station, is owned by current session's player, grid exists
		/// </summary>
		/// <returns>true iff it is possible for this grid to navigate</returns>
		public bool gridCanNavigate()
		{
			if (myGrid == null || myGrid.Closed)
			{
				debugLog("grid is gone...", "gridCanNavigate()", Logger.severity.INFO);
				OnClose();
				return false;
			}
			if (myGrid.IsStatic)
				return false;

			if (MyAPIGateway.Players.GetPlayerControllingEntity(myGrid) != null)
			{
				if (!player_controlling)
				{
					IMyPlayer controllingPlayer = MyAPIGateway.Players.GetPlayerControllingEntity(myGrid);
					if (controllingPlayer != null)
					{
						debugLog("player is controlling grid: " + controllingPlayer.DisplayName, "gridCanNavigate()", Logger.severity.TRACE);
						player_controlling = true;
					}
				}
				return false;
			}
			if (player_controlling)
			{
				debugLog("player(s) released controls", "gridCanNavigate()", Logger.severity.TRACE);
				player_controlling = false;
			}

			return true;
		}

		/// <summary>
		/// checks the working flag, current player owns it, display name has not changed
		/// </summary>
		/// <param name="autopilotBlock">remote control to check</param>
		/// <returns>true iff the remote control is ready</returns>
		public bool autopilotBlockIsReady(Sandbox.ModAPI.IMyCubeBlock autopilotBlock)
		{
			if (autopilotBlock == null)
			{
				debugLog("no remote control", "autopilotBlockIsReady()", Logger.severity.TRACE);
				return false;
			}
			if (!autopilotBlock.IsWorking)
			{
				debugLog("not working", "autopilotBlockIsReady()", Logger.severity.TRACE);
				return false;
			}
			if (autopilotBlock.CubeGrid.BigOwners.Count == 0) // no owner
			{
				debugLog("no owner", "autopilotBlockIsReady()", Logger.severity.TRACE);
				return false;
			}
			if (autopilotBlock.OwnerId != autopilotBlock.CubeGrid.BigOwners[0]) // remote control is not owned by grid's owner
			{
				debugLog("remote has different owner", "autopilotBlockIsReady()", Logger.severity.TRACE);
				return false;
			}
			if (!(autopilotBlock as Ingame.IMyShipController).ControlThrusters)
			{
				//log("no thruster control", "autopilotBlockIsReady()", Logger.severity.TRACE);
				return false;
			}

			//myLogger.debugLog("remote is ready: " + autopilotBlock.DisplayNameText, "autopilotBlockIsReady()");
			return true;
		}

		public bool autopilotBlockIsReady(IMyControllableEntity autopilotBlock)
		{ return autopilotBlockIsReady(autopilotBlock as Sandbox.ModAPI.IMyCubeBlock); }

		private void reset(string reason)
		{
			myLogger.debugLog("reset reason = " + reason, "reset()");
			myEngager.Disarm();
			currentAPcontrollable = null;
		}

		private DateTime maxRotateTime;
		internal MovementMeasure MM;

		private void navigate()
		{
			//myLogger.debugLog("entered navigate()", "navigate()");

			if (currentAPblock == null)
				return;
			if (!autopilotBlockIsReady(currentAPblock))
			{
				reportState(ReportableState.Off, true);
				reset("remote control is not ready");
				return;
			}

			if (myEngager == null)
				throw new NullReferenceException("myEngager");

			// before navigate
			if (myEngager.CurrentStage == Engager.Stage.Engaging)
			{
				//myLogger.debugLog("engager is armed", "navigate()");
				// set rotate to point
				FixedWeapon primary = myEngager.GetPrimaryWeapon();
				if (primary == null)
				{
					if (myEngager.HasWeaponControl())
					{
						myLogger.debugLog("only turrets remain", "navigate()");
						CNS.rotateToPoint = null;
					}
					else
					{
						myLogger.debugLog("no weapons remain", "navigate()", Logger.severity.DEBUG);
						fullStop("no weapons remain");
						myEngager.Disarm();
						CNS.atWayDest(NavSettings.TypeOfWayDest.GRID);
						return;
					}
				}
				else // primary != null
				{
					//myLogger.debugLog("have a primary weapon: " + primary.CubeBlock.DisplayNameText, "navigate()");
					CNS.rotateToPoint = primary.CurrentTarget.InterceptionPoint;
				}

				if (!CNS.rotateToPoint.HasValue)
				{
					//myLogger.debugLog("primary weapon does not have a target.", "navigate()");
					//if (CNS.CurrentGridDest != null)
					//{
					myLogger.debugLog("primary weapon does not have a target, setting rotateToPoint to centre of target grid", "navigate()");
						CNS.rotateToPoint = CNS.CurrentGridDest.Grid.GetCentre();
					//}
					//else
					//	myLogger.debugLog("not setting a rotateToPoint", "navigate()");
				}
				else
					myLogger.debugLog("primary weapon has a target: " + CNS.rotateToPoint, "navigate()");
			}
			else
				CNS.rotateToPoint = null;

			MM = new MovementMeasure(this);

			if (myEngager.CurrentStage == Engager.Stage.Engaging && CNS.getTypeOfWayDest() != NavSettings.TypeOfWayDest.WAYPOINT) // engager flies to waypoints normally
			{
				myLogger.debugLog("Engager is armed. TypeOfWayDest = " + CNS.getTypeOfWayDest() + ", distToPoint = " + MM.distToPoint + ", distToDestGrid = " + MM.distToDestGrid + ", MaxWeaponRange = " + myEngager.MaxWeaponRange, "navigate()");
				if (CNS.CurrentGridDest.Offset.HasValue)
				{
					if (MM.distToPoint < 100)
					{
						CNS.CurrentGridDest.Offset = myEngager.GetRandomOffset();
						myLogger.debugLog("Near way/dest, setting random offset: " + CNS.CurrentGridDest.Offset, "navigate()");
						return;
					}
					else
						myLogger.debugLog("flying towards offset: " + CNS.CurrentGridDest.Offset + " from grid at " + CNS.CurrentGridDest.Grid.GetPosition(), "navigate()");
				}
				else
				{
					//myLogger.debugLog("no offset value", "navigate()");
					//if (MM.distToDestGrid < myEngager.MaxWeaponRange * 2)
					//{
					CNS.CurrentGridDest.Offset = myEngager.GetRandomOffset();
					//	myLogger.debugLog("Near weapon range, setting random offset: " + CNS.CurrentGridDest.Offset, "navigate()");
					myLogger.debugLog("Setting first offset: " + CNS.CurrentGridDest.Offset, "navigate()");
					return;
					//}
					//else
					//	myLogger.debugLog("far away, flying normally", "navigate()");
				}
			}

			navigateSub();

			// after navigate
			checkStopped();
			myRotator.isRotating();
		}

		private Lander myLand;

		private void navigateSub()
		{
			//log("entered navigate(" + myWaypoint + ")", "navigate()", Logger.severity.TRACE);

			if (CNS.landingState != NavSettings.LANDING.OFF)
			{
				myLand.landGrid(MM); // continue landing
				return;
			}
			if (myLand != null && myLand.targetDirection != null)
			{
				myLand.matchOrientation(); // continue match
				return;
			}
			if (myHarvester.Run())
				return;

			if (!checkAt_wayDest())
				collisionCheckMoveAndRotate();

			//if (CNS.moveState != NavSettings.Moving.SIDELING)
			calcAndRotate();
		}

		internal const int radiusLandWay = 10;

		/// <returns>skip collisionCheckMoveAndRotate()</returns>
		private bool checkAt_wayDest()
		{
			if (CNS.isAMissile)
				return false;
			if (MM.distToWayDest > CNS.destinationRadius)
				return false;
			if (CNS.landLocalBlock != null && MM.distToWayDest > radiusLandWay) // distance to start landing
				return false;

			if (CNS.getTypeOfWayDest() == NavSettings.TypeOfWayDest.WAYPOINT)
			{
				CNS.atWayDest();
				if (CNS.getTypeOfWayDest() == NavSettings.TypeOfWayDest.NONE)
				{
					alwaysLog("Error no more destinations at Navigator.checkAt_wayDest() // at waypoint", "checkAt_wayDest()", Logger.severity.ERROR);
					fullStop("No more dest");
				}
				else
					debugLog("reached waypoint, next type is " + CNS.getTypeOfWayDest() + ", coords: " + CNS.getWayDest(), "checkAt_wayDest()", Logger.severity.INFO);
				return true;
			}

			if (myEngager.CurrentStage == Engager.Stage.Engaging)
				return false;

			if (CNS.match_direction == null && CNS.landLocalBlock == null)
			{
				fullStop("At dest");
				debugLog("reached destination dist = " + MM.distToWayDest + ", proximity = " + CNS.destinationRadius, "checkAt_wayDest()", Logger.severity.INFO);
				CNS.atWayDest();
				return true;
			}
			else
			{
				fullStop("At dest, orient or land");
				if (CNS.landLocalBlock != null)
				{
					debugLog("near dest, start landing. dist=" + MM.distToWayDest + ", radius=" + CNS.destinationRadius, "checkAt_wayDest()", Logger.severity.DEBUG);
					myLand = new Lander(this);
					myLand.landGrid(MM); // start landing
				}
				else // CNS.match_direction != null
				{
					debugLog("near dest, start orient. dist=" + MM.distToWayDest + ", radius=" + CNS.destinationRadius, "checkAt_wayDest()", Logger.severity.DEBUG);
					myLand = new Lander(this);
					myLand.matchOrientation(); // start match
				}
				return true;
			}
		}

		internal void collisionCheckMoveAndRotate()
		{
			if (!CNS.isAMissile)
			{
				myPathfinder_Output = myPathfinder.GetOutput();
				myPathfinder.Run(CNS, getNavigationBlock(), myEngager);
				if (myPathfinder_Output != null)
				{
					//myLogger.debugLog("result: " + myPathfinder_Output.PathfinderResult, "collisionCheckMoveAndRotate()");
					switch (myPathfinder_Output.PathfinderResult)
					{
						case Pathfinder.PathfinderOutput.Result.Incomplete:
							// if ship is not moving, wait for a path. if ship is moving, keep previous PathfinderAllowsMovement
							if (CNS.moveState == NavSettings.Moving.NOT_MOVE)
								PathfinderAllowsMovement = false;
							break;
						case Pathfinder.PathfinderOutput.Result.Searching_Alt:
							fullStop("searching for a path");
							pathfinderState = ReportableState.Pathfinding;
							PathfinderAllowsMovement = false;
							break;
						case Pathfinder.PathfinderOutput.Result.Alternate_Path:
							myLogger.debugLog("Setting new waypoint: " + myPathfinder_Output.Waypoint, "collisionCheckMoveAndRotate()");
							CNS.setWaypoint(myPathfinder_Output.Waypoint);
							pathfinderState = ReportableState.Path_OK;
							PathfinderAllowsMovement = true;
							break;
						case Pathfinder.PathfinderOutput.Result.Path_Clear:
							//myLogger.debugLog("Path forward is clear", "collisionCheckMoveAndRotate()");
							pathfinderState = ReportableState.Path_OK;
							PathfinderAllowsMovement = true;
							break;
						case Pathfinder.PathfinderOutput.Result.No_Way_Forward:
							fullStop("No Path");
							pathfinderState = ReportableState.No_Path;
							PathfinderAllowsMovement = false;
							return;
						default:
							myLogger.alwaysLog("Error, invalid case: " + myPathfinder_Output.PathfinderResult, "collisionCheckMoveAndRotate()", Logger.severity.FATAL);
							fullStop("Invalid Pathfinder.PathfinderOutput");
							pathfinderState = ReportableState.No_Path;
							PathfinderAllowsMovement = false;
							return;
					}
				}
			}
			calcMoveAndRotate();
		}

		private bool value_PathfinderAllowsMovement = false;
		/// <summary>Does the pathfinder permit the grid to move?</summary>
		public bool PathfinderAllowsMovement
		{
			get { return CNS.isAMissile || value_PathfinderAllowsMovement; }
			private set { value_PathfinderAllowsMovement = value; }
		}

		private double prevDistToWayDest = float.MaxValue;
		internal bool movingTooSlow = false;

		private void calcMoveAndRotate()
		{
			if (!PathfinderAllowsMovement)
				return;

			SpeedControl.controlSpeed(this);

			switch (CNS.moveState)
			{
				case NavSettings.Moving.MOVING:
					{
						double newDistToWayDest = MM.distToWayDest;
						//myLogger.debugLog("newDistToWayDest = " + newDistToWayDest + ", prevDistToWayDest = " + prevDistToWayDest, "calcMoveAndRotate()");
						if (newDistToWayDest > prevDistToWayDest + 1)
						{
							myLogger.debugLog("Moving away from destination, newDistToWayDest = " + newDistToWayDest + " > prevDistToWayDest = " + prevDistToWayDest, "calcMoveAndRotate()");
							fullStop("moving away from destination");
							return;
						}
						prevDistToWayDest = newDistToWayDest;

						//myLogger.debugLog("movingTooSlow = " + movingTooSlow + ", PathfinderAllowsMovement = " + PathfinderAllowsMovement + ", MM.rotLenSq = " + MM.rotLenSq + ", rotLenSq_startMove = " + rotLenSq_startMove, "calcMoveAndRotate()");
						if (movingTooSlow && PathfinderAllowsMovement //speed up test. missile will never pass this test
							&& MM.rotLenSq < rotLenSq_startMove)
							StartMoveMove();
					}
					break;
				case NavSettings.Moving.STOP_MOVE:
					{
						//if (CNS.rotateToPoint.HasValue)
						//{
						//	if (MM.movementSpeed < 10)
						//		StartMoveHybrid();
						//}
						//else
						if (PathfinderAllowsMovement && MM.rotLenSq < myRotator.rotLenSq_stopAndRot && CNS.SpecialFlyingInstructions == NavSettings.SpecialFlying.None)
							StartMoveMove();
					}
					break;
				case NavSettings.Moving.HYBRID:
					{
						// when tight to destination, switch to move
						if (!CNS.rotateToPoint.HasValue && MM.rotLenSq < rotLenSq_switchToMove)
						{
							myLogger.debugLog("switching to move", "calcMoveAndRotate()", Logger.severity.DEBUG);
							StartMoveMove();
						}
						else
							calcAndMove(true); // continue in current state
						break;
					}
				case NavSettings.Moving.SIDELING:
					{
						if (CNS.isAMissile)
						{
							debugLog("missile needs to stop sideling", "calcMoveAndRotate()", Logger.severity.DEBUG);
							fullStop("stop sidel: converted to missile");
							break;
						}
						calcAndMove(true); // continue in current state
						break;
					}
				case NavSettings.Moving.NOT_MOVE:
					{
						//if (CNS.rotateState == NavSettings.Rotating.NOT_ROTA) // why do we care?
						MoveIfPossible();
						break;
					}
				default:
					{
						debugLog("Not Yet Implemented, state = " + CNS.moveState, "calcMoveAndRotate()", Logger.severity.ERROR);
						break;
					}
			}
		}

		private bool MoveIfPossible()
		{
			if (PathfinderAllowsMovement)
			{
				if (CNS.isAMissile || CNS.rotateToPoint.HasValue)
				{
					StartMoveHybrid();
					return true;
				}
				if (CNS.SpecialFlyingInstructions == NavSettings.SpecialFlying.Line_SidelForward)
				{
					StartMoveSidel();
					return true;
				}
				if (CNS.SpecialFlyingInstructions == NavSettings.SpecialFlying.Line_Any)
				{
					StartMoveMove();
					return true;
				}
				if (CNS.landingState == NavSettings.LANDING.OFF && MM.distToWayDest > myGrid.GetLongestDim() + CNS.destinationRadius)
				{
					StartMoveHybrid();
					return true;
				}
				StartMoveSidel();
				return true;
			}
			return false;
		}

		private void StartMoveHybrid()
		{
			calcAndMove(true);
			CNS.moveState = NavSettings.Moving.HYBRID;
		}

		private void StartMoveSidel()
		{
			//if (CNS.rotateState != NavSettings.Rotating.NOT_ROTA)
			//	throw new InvalidOperationException("Cannot sidel while rotating.");

			calcAndMove(true);
			CNS.moveState = NavSettings.Moving.SIDELING;
		}

		private void StartMoveMove()
		{
			calcAndMove();
			CNS.moveState = NavSettings.Moving.MOVING;
		}

		public const float rotLenSq_switchToMove = 0.00762f; // 5°

		/// <summary>
		/// start moving when less than (30°)
		/// </summary>
		public const float rotLenSq_startMove = 0.274f;

		/// <summary>
		/// stop when greater than
		/// </summary>
		private const float offCourse_sidel = 0.1f, offCourse_hybrid = 0.1f, offCourse_engage = MathHelper.PiOver2;

		private Vector3 moveDirection = Vector3.Zero;

		private void calcAndMove(bool sidel = false)//, bool anyState=false)
		{
			//log("entered calcAndMove("+doSidel+")", "calcAndMove()", Logger.severity.TRACE);
			try
			{
				if (sidel)
				{
					//Vector3 worldDisplacement = ((Vector3D)CNS.getWayDest() - (Vector3D)getNavigationBlock().GetPosition());
					//RelativeDirection3F displacement = MM.displacement; //RelativeVector3F.createFromWorld(worldDisplacement, myGrid); // Only direction matters, we will normalize later. A multiplier helps prevent precision issues.
					Vector3 course = MM.displacement.ToWorldNormalized();
					float offCourse = Vector3.DistanceSquared(course, moveDirection);

					switch (CNS.moveState)
					{
						case NavSettings.Moving.SIDELING:
							{
								if (offCourse < offCourse_sidel)
								{
									if (movingTooSlow || currentMove != Vector3.Zero)
										goto case NavSettings.Moving.NOT_MOVE;
								}
								else
								{
									myLogger.debugLog("distance squared between " + course + " and " + moveDirection + " is " + offCourse, "calcAndMove()");
									fullStop("change course: sidel");
								}
								return;
							}
						case NavSettings.Moving.HYBRID:
							{
								float offCourseThresh;
								if (CNS.rotateToPoint.HasValue)
									offCourseThresh = offCourse_engage;
								else
									offCourseThresh = offCourse_hybrid;
								if (MM.movementSpeed > 10 && offCourse > offCourseThresh)
								{
									myLogger.debugLog("distance squared between " + course + " and " + moveDirection + " is " + offCourse, "calcAndMove()");
									if (CNS.rotateToPoint.HasValue)
									{
										fullStop("off course");
										return;
									}

									CNS.moveState = NavSettings.Moving.MOVING;
									calcAndMove();
									return;
								}

								if (movingTooSlow || currentMove != Vector3.Zero)
									goto case NavSettings.Moving.NOT_MOVE;

								if (currentMove != Vector3.Zero)
									goto case NavSettings.Moving.NOT_MOVE;

								return;
							}
						case NavSettings.Moving.NOT_MOVE:
							{
								RelativeDirection3F scaled = currentThrust.scaleByForce(MM.displacement, getNavigationBlock());
								moveOrder(scaled);
								if (CNS.moveState == NavSettings.Moving.NOT_MOVE)
								{
									moveDirection = course;
									debugLog("sideling. wayDest=" + CNS.getWayDest() + ", worldDisplacement=" + MM.displacement.ToWorld() + ", RCdirection=" + course, "calcAndMove()", Logger.severity.DEBUG);
									debugLog("... scaled=" + scaled.ToWorld() + ":" + scaled.ToLocal() + ":" + scaled.ToBlock(getNavigationBlock()), "calcAndMove()", Logger.severity.DEBUG);
								}
								return;
							}
						default:
							{
								alwaysLog("unsuported moveState: " + CNS.moveState, "calcAndMove()", Logger.severity.ERROR);
								return;
							}
					}
				}
				else // not sidel
				{
					Vector3 NavForward = getNavigationBlock().LocalMatrix.Forward;
					Vector3 RemFromNF = Base6Directions.GetVector(currentAPblock.LocalMatrix.GetClosestDirection(ref NavForward));

					moveOrder(RemFromNF); // move forward
					debugLog("forward = " + RemFromNF + ", moving " + MM.distToWayDest + " to " + CNS.getWayDest(), "calcAndMove()", Logger.severity.DEBUG);
				}
			}
			finally
			{
				stoppedMovingAt = DateTime.UtcNow + stoppedAfter;
				movingTooSlow = false;
			}
		}

		internal void calcAndRotate()
		{
			myRotator.calcAndRotate();
			myRotator.calcAndRoll(MM.roll);
		}

		internal void calcAndRoll(float roll)
		{ myRotator.calcAndRoll(roll); }

		private static TimeSpan stoppedAfter = new TimeSpan(0, 0, 1);
		private DateTime stoppedMovingAt;
		private const float stoppedPrecision = 0.2f;

		public bool checkStopped()
		{
			if (CNS.moveState == NavSettings.Moving.NOT_MOVE)
				return true;

			if (MM.movementSpeed == null || MM.movementSpeed > stoppedPrecision)
			{
				stoppedMovingAt = DateTime.UtcNow + stoppedAfter;
				//myLogger.debugLog("Still moving, stopped at " + stoppedMovingAt, "checkStopped()");
			}
			else
			{
				if (DateTime.UtcNow > stoppedMovingAt)
				{
					if (CNS.moveState == NavSettings.Moving.STOP_MOVE)
					{
						CNS.moveState = NavSettings.Moving.NOT_MOVE;
						CNS.clearSpeedInternal();
					}
					else
						fullStop("not moving");
					//myLogger.debugLog("not moving for a time: " + (DateTime.UtcNow - stoppedMovingAt).TotalSeconds, "checkStopped()");
					return true;
				}
				//myLogger.debugLog("speed is low, for a short time: " + (DateTime.UtcNow - stoppedMovingAt).TotalSeconds, "checkStopped()");
			}

			return false;
		}

		/// <summary>
		/// for other kinds of stop use moveOrder(Vector3.Zero) or similar
		/// </summary>
		internal void fullStop(string reason)
		{
			debugLog("full stop: " + reason, "fullStop()", Logger.severity.INFO);
			currentMove = Vector3.Zero;
			currentRotate = Vector2.Zero;
			currentRoll = 0;
			prevDistToWayDest = float.MaxValue;

			EnableDampeners();
			currentAPcontrollable.MoveAndRotateStopped();

			CNS.moveState = NavSettings.Moving.STOP_MOVE;
			CNS.rotateState = NavSettings.Rotating.STOP_ROTA;
		}

		internal Vector3 currentMove = Vector3.One; // for initial fullStop
		internal Vector2 currentRotate = Vector2.Zero;
		internal float currentRoll = 0;

		internal void moveOrder(Vector3 move)
		{
			//if (normalize)
			//	move = Vector3.Normalize(move);
			if (!move.IsValid())
				move = Vector3.Zero;
			if (currentMove == move)
				return;
			currentMove = move;
			moveAndRotate();
			if (move != Vector3.Zero)
			{
				//myLogger.debugLog("Enabling dampeners", "moveOrder()");
				EnableDampeners();
			}
		}

		internal void moveOrder(RelativeDirection3F move)
		{
			moveOrder(move.ToBlockNormalized(currentAPblock));
		}

		internal void moveAndRotate()
		{
			if (currentMove == Vector3.Zero && currentRotate == Vector2.Zero && currentRoll == 0)
			{
				debugLog("MAR is actually stop", "moveAndRotate()");
				currentAPcontrollable.MoveAndRotateStopped();
			}
			else
			{
				if (CNS.moveState != NavSettings.Moving.HYBRID)
					debugLog("doing MAR(" + currentMove + ", " + currentRotate + ", " + currentRoll + ")", "moveAndRotate()");
				currentAPcontrollable.MoveAndRotate(currentMove, currentRotate, currentRoll);
			}
		}

		public bool dampenersEnabled()
		{ return ((currentAPcontrollable as Ingame.IMyShipController).DampenersOverride) && !currentThrust.disabledThrusters(); }

		internal void DisableReverseThrust()
		{
			switch (CNS.moveState)
			{
				//case NavSettings.Moving.HYBRID:
				case NavSettings.Moving.MOVING:
					myLogger.debugLog("disabling reverse thrust", "DisableReverseThrust()");
					EnableDampeners();
					currentThrust.disableThrusters(Base6Directions.GetFlippedDirection(getNavigationBlock().Orientation.Forward));
					break;
				default:
					myLogger.debugLog("disabling dampeners", "DisableReverseThrust()");
					EnableDampeners(false);
					break;
			}
		}

		public void EnableDampeners(bool dampenersOn = true)
		{
			if (dampenersOn)
				currentThrust.enableAllThrusters();

			try
			{
				if ((currentAPcontrollable as Ingame.IMyShipController).DampenersOverride != dampenersOn)
				{
					currentAPcontrollable.SwitchDamping(); // sometimes SwitchDamping() throws a NullReferenceException while grid is being destroyed
					if (!dampenersOn)
						debugLog("speed control: disabling dampeners. speed=" + MM.movementSpeed + ", cruise=" + CNS.getSpeedCruise() + ", slow=" + CNS.getSpeedSlow(), "setDampeners()", Logger.severity.TRACE);
					else
						debugLog("speed control: enabling dampeners. speed=" + MM.movementSpeed + ", cruise=" + CNS.getSpeedCruise() + ", slow=" + CNS.getSpeedSlow(), "setDampeners()", Logger.severity.TRACE);
				}
			}
			catch (NullReferenceException)
			{ debugLog("setDampeners() threw NullReferenceException", "setDampeners()", Logger.severity.DEBUG); }
		}

		public override string ToString()
		{
			return "Nav:" + myGrid.DisplayName;
		}

		#region Report State

		public enum ReportableState : byte
		{
			None, Off, No_Dest, Waiting, Landed,
			Path_OK, Pathfinding, No_Path,
			Rotating, Moving, Hybrid, Sidel, Roll,
			Stop_Move, Stop_Rotate, Stop_Roll,
			H_Ready, Harvest, H_Stuck, H_Back, H_Tunnel,
			Missile, Engaging, Player, Jump //, GET_OUT_OF_SEAT
		};
		/// <summary>The state of the pathinfinder</summary>
		private ReportableState pathfinderState = ReportableState.Path_OK;

		internal bool GET_OUT_OF_SEAT = false;

		private Color currentAPcolour = Color.Pink;

		internal void reportState(ReportableState newState = ReportableState.Off, bool forced = false)
		{
			if (currentAutopilotBlock_Value == null)
				return;

			string displayName = (currentAutopilotBlock_Value as Sandbox.ModAPI.IMyCubeBlock).DisplayNameText;
			if (displayName == null)
			{
				alwaysLog("cannot report without display name", "reportState()", Logger.severity.WARNING);
				return;
			}

			if (!forced)
				newState = GetState();

			if (newState == ReportableState.None)
				return;

			Color reportColour = ReportableColour[newState];

			// cut old state, if any
			if (displayName[0] == '<')
			{
				int endOfState = displayName.IndexOf('>');
				if (endOfState != -1)
					displayName = displayName.Substring(endOfState + 1);
			}

			// add new state
			StringBuilder newName = new StringBuilder();
			newName.Append('<');
			// error
			if (myInterpreter.instructionErrorIndex != null)
			{
				myLogger.debugLog("adding error, index = " + myInterpreter.instructionErrorIndex, "reportState()");
				newName.Append("ERROR(" + myInterpreter.instructionErrorIndex + ") : ");
				reportColour = Color.Purple;
			}
			// actual state
			newName.Append(newState);
			// wait time
			if (newState == ReportableState.Waiting || newState == ReportableState.Landed)
			{
				int seconds = (int)Math.Ceiling((CNS.waitUntil - DateTime.UtcNow).TotalSeconds);
				if (seconds >= 0)
				{
					newName.Append(':');
					newName.Append(seconds);
				}
			}

			newName.Append('>');
			newName.Append(displayName);

			string newNameString = newName.ToString();

			if (newNameString != displayName)
			{
				(currentAutopilotBlock_Value as Ingame.IMyTerminalBlock).SetCustomName(newNameString);
				//log("added ReportableState to RC: " + newName, "reportState()", Logger.severity.TRACE);
			}
			if (IsAutopilotBlock(currentAPblock) && ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bUseColourState) && reportColour != currentAPcolour)
			{
				currentAPcolour = reportColour;
				var position = (currentAutopilotBlock_Value as IMyCubeBlock).Position;
				Vector3 HSV = reportColour.ColorToHSV();

				//myLogger.debugLog("colour = " + HSV, "reportState()");

				HSV.Y = HSV.Y * 2 - 1;
				HSV.Z = HSV.Z * 2 - 1;

				//myLogger.debugLog("mod colour = " + HSV, "reportState()");

				myGrid.ColorBlocks(position, position, HSV);
			}
		}

		private ReportableState GetState()
		{
			if (CNS.EXIT)
				return ReportableState.Off;
			if (player_controlling)
				return ReportableState.Player;

			// landing
			//if (GET_OUT_OF_SEAT) // must override LANDED
			//	return ReportableState.GET_OUT_OF_SEAT;
			if (CNS.landingState == NavSettings.LANDING.LOCKED)
				return ReportableState.Landed;
			if (CNS.waitUntil.CompareTo(DateTime.UtcNow) > 0)
				return ReportableState.Waiting;

			// pathfinding
			switch (pathfinderState)
			{
				case ReportableState.No_Path:
					return ReportableState.No_Path;
				case ReportableState.Pathfinding:
					return ReportableState.Pathfinding;
			}

			// harvest
			if (myHarvester.IsActive())
				if (myHarvester.HarvestState != ReportableState.H_Ready)
					return myHarvester.HarvestState;

			if (CNS.getTypeOfWayDest() == NavSettings.TypeOfWayDest.NONE)
				return ReportableState.No_Dest;

			// targeting
			if (CNS.target_locked)
			{
				if (CNS.lockOnTarget == NavSettings.TARGET.ENEMY)
					return ReportableState.Engaging;
				if (CNS.lockOnTarget == NavSettings.TARGET.MISSILE)
					return ReportableState.Missile;
			}

			// moving
			switch (CNS.moveState)
			{
				case NavSettings.Moving.SIDELING:
					return ReportableState.Sidel;
				case NavSettings.Moving.HYBRID:
					return ReportableState.Hybrid;
				case NavSettings.Moving.MOVING:
					return ReportableState.Moving;
			}

			// rotating
			if (CNS.rotateState == NavSettings.Rotating.ROTATING)
				return ReportableState.Rotating;
			if (CNS.rollState == NavSettings.Rolling.ROLLING)
				return ReportableState.Roll;

			// stopping
			if (CNS.moveState == NavSettings.Moving.STOP_MOVE)
				return ReportableState.Stop_Move;
			if (CNS.rotateState == NavSettings.Rotating.STOP_ROTA)
				return ReportableState.Stop_Rotate;
			if (CNS.rollState == NavSettings.Rolling.STOP_ROLL)
				return ReportableState.Stop_Roll;

			return ReportableState.None;
		}

		private static Dictionary<ReportableState, Color> value_ReportableColour;
		private static Dictionary<ReportableState, Color> ReportableColour
		{
			get
			{
				if (value_ReportableColour == null)
				{
					value_ReportableColour = new Dictionary<ReportableState, Color>();

					// pink is used for a state that is not intended to be reported
					value_ReportableColour.Add(ReportableState.None, Color.Pink);
					value_ReportableColour.Add(ReportableState.Off, Color.Black);
					value_ReportableColour.Add(ReportableState.No_Dest, Color.DarkGreen);
					value_ReportableColour.Add(ReportableState.Waiting, Color.Yellow);
					value_ReportableColour.Add(ReportableState.Landed, Color.Yellow);

					value_ReportableColour.Add(ReportableState.Path_OK, Color.Pink);
					value_ReportableColour.Add(ReportableState.Pathfinding, Color.DarkGreen);
					value_ReportableColour.Add(ReportableState.No_Path, Color.Purple);

					value_ReportableColour.Add(ReportableState.Rotating, Color.Green);
					value_ReportableColour.Add(ReportableState.Moving, Color.Green);
					value_ReportableColour.Add(ReportableState.Hybrid, Color.Green);
					value_ReportableColour.Add(ReportableState.Sidel, Color.Green);
					value_ReportableColour.Add(ReportableState.Roll, Color.Green);

					value_ReportableColour.Add(ReportableState.Stop_Move, Color.Green);
					value_ReportableColour.Add(ReportableState.Stop_Rotate, Color.Green);
					value_ReportableColour.Add(ReportableState.Stop_Roll, Color.Green);

					value_ReportableColour.Add(ReportableState.H_Ready, Color.Cyan);
					value_ReportableColour.Add(ReportableState.Harvest, Color.Cyan);
					value_ReportableColour.Add(ReportableState.H_Stuck, Color.Cyan);
					value_ReportableColour.Add(ReportableState.H_Back, Color.Cyan);
					value_ReportableColour.Add(ReportableState.H_Tunnel, Color.Cyan);

					value_ReportableColour.Add(ReportableState.Missile, Color.Red);
					value_ReportableColour.Add(ReportableState.Engaging, Color.Orange);
					value_ReportableColour.Add(ReportableState.Player, Color.Gray);
					value_ReportableColour.Add(ReportableState.Jump, Color.Blue);
					//value_ReportableColour.Add(ReportableState.GET_OUT_OF_SEAT, Color.Purple);
				}
				return value_ReportableColour;
			}
		}

		#endregion

		private static readonly MyObjectBuilderType type_cockpit = typeof(MyObjectBuilder_Cockpit);
		private const string subtype_autopilotBlock = "Autopilot-Block";

		private static readonly MyObjectBuilderType type_remoteControl = typeof(MyObjectBuilder_RemoteControl);

		/// <summary>
		/// Tests if a block is an acutal Autopilot block
		/// </summary>
		/// <param name="block"></param>
		/// <returns></returns>
		public static bool IsAutopilotBlock(IMyCubeBlock block)
		{
			var definition = block.BlockDefinition;
			return definition.TypeId == type_cockpit && definition.SubtypeId.Contains(subtype_autopilotBlock);
		}

		/// <summary>
		/// Tests if a block can be used by Navigator as an Autopilot block
		/// </summary>
		/// <param name="block">block to test</param>
		/// <returns>true iff block can be used as Autopilot block</returns>
		public static bool IsControllableBlock(IMyCubeBlock block)
		{
			if (block == null || !(block is Ingame.IMyShipController))
				return false;

			if (block.BlockDefinition.TypeId == type_remoteControl && ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bUseRemoteControl))
				return true;

			return IsAutopilotBlock(block);
		}
	}
}
