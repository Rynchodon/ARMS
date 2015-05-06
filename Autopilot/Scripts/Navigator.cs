#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Sandbox.Common;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using Ingame = Sandbox.ModAPI.Ingame;

using VRage.Library.Utils;
using VRageMath;

using Rynchodon.Autopilot.Instruction;
using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot.Harvest;

namespace Rynchodon.Autopilot
{
	public class Navigator
	{
		private Logger myLogger = null;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ alwaysLog(level, method, toLog); }
		private void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.WARNING)
		{ alwaysLog(level, method, toLog); }
		private void alwaysLog(Logger.severity level, string method, string toLog)
		{
			try
			{ myLogger.log(level, method, toLog, CNS.moveState.ToString() + ":" + CNS.rotateState.ToString(), CNS.landingState.ToString()); }
			catch (Exception) { }
		}

		public Sandbox.ModAPI.IMyCubeGrid myGrid { get; private set; }

		private List<Sandbox.ModAPI.IMySlimBlock> remoteControlBlocks;

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
		//internal GridDimensions myGridDim;
		internal ThrustProfiler currentThrust;
		internal Targeter myTargeter;
		private Rotator myRotator;
		internal HarvesterAsteroid myHarvester { get; private set; }

		private IMyControllableEntity currentRemoteControl_Value;
		/// <summary>
		/// Primary remote control value.
		/// </summary>
		public IMyControllableEntity currentRCcontrol
		{
			get { return currentRemoteControl_Value; }
			set
			{
				//log("setting new RC ", "currentRCcontrol.set", Logger.severity.TRACE);
				if (currentRemoteControl_Value == value)
				{
					//log("currentRemoteControl_Value == value", "currentRCcontrol.set", Logger.severity.TRACE);
					return;
				}

				if (currentRemoteControl_Value != null)
				{
					// actions on old RC
					(currentRemoteControl_Value as Sandbox.ModAPI.IMyTerminalBlock).CustomNameChanged -= remoteControl_OnNameChanged;
					fullStop("unsetting RC");
					reportState(ReportableState.OFF);
				}

				currentRemoteControl_Value = value;
				myLand = null;
				if (currentRemoteControl_Value == null)
				{
					//myGridDim = null;
					myPathfinder = null;
					CNS = new NavSettings(null);
				}
				else
				{
					//myGridDim = new GridDimensions(currentRCblock);
					myPathfinder = new Pathfinder.Pathfinder(myGrid);
					CNS = new NavSettings(this);
					myLogger.debugLog("have a new RC: " + currentRCblock.getNameOnly(), "set_currentRCcontrol()");
				}

				if (currentRemoteControl_Value != null)
				{
					// actions on new RC
					instructions = currentRCblock.getInstructions();
					(currentRemoteControl_Value as Sandbox.ModAPI.IMyTerminalBlock).CustomNameChanged += remoteControl_OnNameChanged;
					fullStop("new RC");
					reportState(ReportableState.OFF);
				}

				myRotator = new Rotator(this);
				myHarvester = new HarvesterAsteroid(this);
			}
		}
		/// <summary>
		/// Secondary remote control value.
		/// </summary>
		public Sandbox.ModAPI.IMyCubeBlock currentRCblock
		{
			get { return currentRemoteControl_Value as Sandbox.ModAPI.IMyCubeBlock; }
			set { currentRCcontrol = value as IMyControllableEntity; }
		}
		/// <summary>
		/// Secondary remote control value.
		/// </summary>
		public IMyTerminalBlock currentRCterminal
		{ get { return currentRemoteControl_Value as IMyTerminalBlock; } }

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
				return currentRCblock;
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
			remoteControlBlocks = new List<Sandbox.ModAPI.IMySlimBlock>();
			myGrid.GetBlocks(remoteControlBlocks, block => block.FatBlock != null && block.FatBlock.BlockDefinition.TypeId == remoteControlType);

			// register for events
			myGrid.OnBlockAdded += OnBlockAdded;
			myGrid.OnBlockRemoved += OnBlockRemoved;
			myGrid.OnClose += OnClose;

			currentThrust = new ThrustProfiler(myGrid);
			CNS = new NavSettings(null);
			myTargeter = new Targeter(this);
			myInterpreter = new Interpreter(this);
			needToInit = false;
		}

		internal void Close()
		{
			if (myGrid != null)
			{
				myGrid.OnClose -= OnClose;
				myGrid.OnBlockAdded -= OnBlockAdded;
				myGrid.OnBlockRemoved -= OnBlockRemoved;
			}
			currentRCcontrol = null;
		}

		private void OnClose()
		{
			Close();
			Core.remove(this);
		}

		private void OnClose(IMyEntity closing)
		{ try { OnClose(); } catch { } }

		private static MyObjectBuilderType remoteControlType = typeof(MyObjectBuilder_RemoteControl);

		private void OnBlockAdded(Sandbox.ModAPI.IMySlimBlock addedBlock)
		{
			if (addedBlock.FatBlock != null && addedBlock.FatBlock.BlockDefinition.TypeId == remoteControlType)
				remoteControlBlocks.Add(addedBlock);
		}

		private void OnBlockRemoved(Sandbox.ModAPI.IMySlimBlock removedBlock)
		{
			if (removedBlock.FatBlock != null)
			{
				if (removedBlock.FatBlock.BlockDefinition.TypeId == remoteControlType)
					remoteControlBlocks.Remove(removedBlock);
			}
		}

		private long updateCount = 0;
		private bool pass_gridCanNavigate = true;

		/// <summary>
		/// Causes the ship to fly around, following commands.
		/// </summary>
		/// <remarks>
		/// Calling more often means more precise movements, calling too often (~ every update) will break functionality.
		/// </remarks>
		public void update()
		{
			reportState();
			updateCount++;
			if (gridCanNavigate())
			{
				// when regaining the ability to navigate, reset
				if (!pass_gridCanNavigate)
				{
					reset();
					pass_gridCanNavigate = true;
				}
			}
			else
			{
				pass_gridCanNavigate = false;
				return;
			}
			if (needToInit)
				init();
			if (CNS.lockOnTarget != NavSettings.TARGET.OFF)
				myTargeter.tryLockOn();
			if (CNS.waitUntilNoCheck.CompareTo(DateTime.UtcNow) > 0)
				return;
			if (CNS.waitUntil.CompareTo(DateTime.UtcNow) > 0 || CNS.EXIT)
			{
				if (!remoteControlIsReady(currentRCblock)) // if something changes, stop waiting!
				{
					log("wait interrupted", "update()", Logger.severity.DEBUG);
					reset();
				}
				reportState(ReportableState.WAITING);
				return;
			}

			if (CNS.getTypeOfWayDest() != NavSettings.TypeOfWayDest.NULL)
				navigate();
			else // no waypoints
			{
				//log("no waypoints or destination");
				if (currentRCcontrol != null && myInterpreter.hasInstructions())
				{
					while (myInterpreter.hasInstructions())
					{
						myLogger.debugLog("invoking instruction: " + myInterpreter.getCurrentInstructionString(), "update()");
						Action instruction = myInterpreter.instructionQueue.Dequeue();
						try { instruction.Invoke(); }
						catch (Exception ex)
						{
							myLogger.log("Exception while invoking instruction: " + ex, "update()", Logger.severity.ERROR);
							continue;
						}
						switch (CNS.getTypeOfWayDest())
						{
							case NavSettings.TypeOfWayDest.BLOCK:
								log("got a block as a destination: " + CNS.GridDestName, "update()", Logger.severity.INFO);
								//reportState(ReportableState.PATHFINDING);
								return;
							case NavSettings.TypeOfWayDest.OFFSET:
								log("got an offset as a destination: " + CNS.GridDestName + ":" + CNS.BlockDestName + ":" + CNS.destination_offset, "update()", Logger.severity.INFO);
								//reportState(ReportableState.PATHFINDING);
								return;
							case NavSettings.TypeOfWayDest.GRID:
								log("got a grid as a destination: " + CNS.GridDestName, "update()", Logger.severity.INFO);
								//reportState(ReportableState.PATHFINDING);
								return;
							case NavSettings.TypeOfWayDest.COORDINATES:
								log("got a new destination " + CNS.getWayDest(), "update()", Logger.severity.INFO);
								return;
							case NavSettings.TypeOfWayDest.LAND:
								log("got a new landing destination " + CNS.getWayDest(), "update()", Logger.severity.INFO);
								//reportState(ReportableState.PATHFINDING);
								return;
							case NavSettings.TypeOfWayDest.NULL:
								break; // keep searching
							case NavSettings.TypeOfWayDest.WAYPOINT:
								log("got a new waypoint destination (harvesting) " + CNS.getWayDest(), "update()", Logger.severity.INFO);
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
					reportState(ReportableState.NO_DEST);
					//log("searching for a ready remote control", "update()", Logger.severity.TRACE);
					CNS.waitUntilNoCheck = DateTime.UtcNow.AddSeconds(1);
					foreach (Sandbox.ModAPI.IMySlimBlock remoteControlBlock in remoteControlBlocks)
					{
						Sandbox.ModAPI.IMyCubeBlock fatBlock = remoteControlBlock.FatBlock;
						if (remoteControlIsReady(fatBlock))
						{
							if (AIOverride)
							{
								if (currentRCcontrol == null)
									currentRCcontrol = (fatBlock as IMyControllableEntity);
							}
							else
							{
								//	parse display name
								string instructions = fatBlock.getInstructions();
								if (string.IsNullOrWhiteSpace(instructions))
									continue;

								currentRCcontrol = (fatBlock as IMyControllableEntity); // necessary to enqueue actions
								if (myInterpreter == null)
									myInterpreter = new Interpreter(this);
								myInterpreter.enqueueAllActions(fatBlock);
								if (myInterpreter.hasInstructions())
								{
									CNS.startOfCommands();
									log("remote control: " + fatBlock.getNameOnly() + " finished queuing " + myInterpreter.instructionQueue.Count + " instruction", "update()", Logger.severity.TRACE);
									return;
								}
								myLogger.debugLog("failed to enqueue actions from " + fatBlock.getNameOnly(), "update()", Logger.severity.DEBUG);
								currentRCcontrol = null;
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
				log("grid is gone...", "gridCanNavigate()", Logger.severity.INFO);
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
						log("player is controlling grid: " + controllingPlayer.DisplayName, "gridCanNavigate()", Logger.severity.TRACE);
						player_controlling = true;
						reportState(ReportableState.PLAYER);
					}
				}
				return false;
			}
			if (player_controlling)
			{
				log("player(s) released controls", "gridCanNavigate()", Logger.severity.TRACE);
				player_controlling = false;
			}

			return true;
		}

		private bool remoteControlIsNotReady = false;

		/// <summary>
		/// checks the working flag, current player owns it, display name has not changed
		/// </summary>
		/// <param name="remoteControl">remote control to check</param>
		/// <returns>true iff the remote control is ready</returns>
		public bool remoteControlIsReady(Sandbox.ModAPI.IMyCubeBlock remoteControl)
		{
			if (remoteControlIsNotReady)
			{
				reset();
				remoteControlIsNotReady = false;
				return false;
			}
			if (remoteControl == null)
			{
				//log("no remote control", "remoteControlIsReady()", Logger.severity.TRACE);
				return false;
			}
			if (!remoteControl.IsWorking)
			{
				log("not working", "remoteControlIsReady()", Logger.severity.TRACE);
				return false;
			}
			if (remoteControl.CubeGrid.BigOwners.Count == 0) // no owner
			{
				log("no owner", "remoteControlIsReady()", Logger.severity.TRACE);
				return false;
			}
			if (remoteControl.OwnerId != remoteControl.CubeGrid.BigOwners[0]) // remote control is not owned by grid's owner
			{
				log("remote has different owner", "remoteControlIsReady()", Logger.severity.TRACE);
				return false;
			}
			if (!(remoteControl as Ingame.IMyShipController).ControlThrusters)
			{
				//log("no thruster control", "remoteControlIsReady()", Logger.severity.TRACE);
				return false;
			}

			return true;
		}

		public bool remoteControlIsReady(IMyControllableEntity remoteControl)
		{ return remoteControlIsReady(remoteControl as Sandbox.ModAPI.IMyCubeBlock); }

		public void reset()
		{ currentRCcontrol = null; }

		private DateTime maxRotateTime;
		internal MovementMeasure MM;

		private void navigate()
		{
			if (currentRCblock == null)
				return;
			if (!remoteControlIsReady(currentRCblock))
			{
				log("remote control is not ready");
				reportState(ReportableState.OFF);
				reset();
				return;
			}

			// before navigate
			MM = new MovementMeasure(this);

			navigateSub();

			// after navigate
			checkStopped();
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
		}

		internal const int radiusLandWay = 10;

		/// <summary>
		///
		/// </summary>
		/// <param name="pitch"></param>
		/// <param name="yaw"></param>
		/// <returns>skip collisionCheckMoveAndRotate()</returns>
		private bool checkAt_wayDest()
		{
			if (CNS.isAMissile)
			{
				//log("missile never reaches destination", "checkAt_wayDest()", Logger.severity.TRACE);
				return false;
			}
			if (MM.distToWayDest > CNS.destinationRadius)
			{
				//log("keep approaching; too far (" + MM.distToWayDest + " > " + CNS.destinationRadius + ")", "checkAt_wayDest()", Logger.severity.TRACE);
				return false;
			}
			if (CNS.landLocalBlock != null && MM.distToWayDest > radiusLandWay) // distance to start landing
			{
				//log("keep approaching; getting ready to land (" + MM.distToWayDest + " > " + radiusLandWay + ")", "checkAt_wayDest()", Logger.severity.TRACE);
				return false;
			}

			if (CNS.getTypeOfWayDest() == NavSettings.TypeOfWayDest.WAYPOINT)
			{
				CNS.atWayDest();
				if (CNS.getTypeOfWayDest() == NavSettings.TypeOfWayDest.NULL)
				{
					alwaysLog(Logger.severity.ERROR, "checkAt_wayDest()", "Error no more destinations at Navigator.checkAt_wayDest() // at waypoint");
					fullStop("No more dest");
				}
				else
					log("reached waypoint, next type is " + CNS.getTypeOfWayDest() + ", coords: " + CNS.getWayDest(), "checkAt_wayDest()", Logger.severity.INFO);
				return true;
			}

			if (CNS.match_direction == null && CNS.landLocalBlock == null)
			{
				fullStop("At dest");
				log("reached destination dist = " + MM.distToWayDest + ", proximity = " + CNS.destinationRadius, "checkAt_wayDest()", Logger.severity.INFO);
				CNS.atWayDest();
				return true;
			}
			else
			{
				fullStop("At dest, orient or land");
				if (CNS.landLocalBlock != null)
				{
					log("near dest, start landing. dist=" + MM.distToWayDest + ", radius=" + CNS.destinationRadius, "checkAt_wayDest()", Logger.severity.DEBUG);
					myLand = new Lander(this);
					myLand.landGrid(MM); // start landing
				}
				else // CNS.match_direction != null
				{
					log("near dest, start orient. dist=" + MM.distToWayDest + ", radius=" + CNS.destinationRadius, "checkAt_wayDest()", Logger.severity.DEBUG);
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
				//myPathfinder.Run((Vector3D)CNS.getWayDest(false), CNS.myWaypoint, getNavigationBlock(), CNS.ignoreAsteroids);
				myPathfinder.Run(CNS, getNavigationBlock());
				if (myPathfinder_Output != null)
				{
					//myLogger.debugLog("result: " + myPathfinder_Output.PathfinderResult, "collisionCheckMoveAndRotate()");
					switch (myPathfinder_Output.PathfinderResult)
					{
						case Pathfinder.PathfinderOutput.Result.Incomplete:
							//PathfinderAllowsMovement = true;
							// leave PathfinderAllowsMovement as it was
							break;
						case Pathfinder.PathfinderOutput.Result.Searching_Alt:
							fullStop("searching for a path");
							reportState(ReportableState.PATHFINDING);
							PathfinderAllowsMovement = false;
							break;
						case Pathfinder.PathfinderOutput.Result.Alternate_Path:
							myLogger.debugLog("Setting new waypoint: " + myPathfinder_Output.Waypoint, "collisionCheckMoveAndRotate()");
							CNS.setWaypoint(myPathfinder_Output.Waypoint);
							//fullStop("new path");
							PathfinderAllowsMovement = true;
							break;
						case Pathfinder.PathfinderOutput.Result.Path_Clear:
							//myLogger.debugLog("Path forward is clear", "collisionCheckMoveAndRotate()");
							PathfinderAllowsMovement = true;
							break;
						case Pathfinder.PathfinderOutput.Result.No_Way_Forward:
							reportState(ReportableState.NO_PATH);
							fullStop("No Path");
							PathfinderAllowsMovement = false;
							return;
						default:
							myLogger.log("Error, invalid case: " + myPathfinder_Output.PathfinderResult, "collisionCheckMoveAndRotate()", Logger.severity.FATAL);
							fullStop("Invalid Pathfinder.PathfinderOutput");
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

		//public static readonly byte collisionUpdatesBeforeMove = 100;
		private double prevDistToWayDest = float.MaxValue;
		internal bool movingTooSlow = false;

		private void calcMoveAndRotate()
		{
			//log("entered calcMoveAndRotate(" + pitch + ", " + yaw + ", " + CNS.getWayDest() + ")", "calcMoveAndRotate()", Logger.severity.TRACE);

			if (!PathfinderAllowsMovement)
				return;

			SpeedControl.controlSpeed(this);
			//log("reached missile check", "calcMoveAndRotate()", Logger.severity.TRACE);

			//myLogger.debugLog("reached switch", "calcMoveAndRotate()");

			switch (CNS.moveState)
			{
				case NavSettings.Moving.MOVING:
					{
						double newDistToWayDest = MM.distToWayDest;
						//myLogger.debugLog("newDistToWayDest = " + newDistToWayDest + ", prevDistToWayDest = " + prevDistToWayDest, "calcMoveAndRotate()");
						if (newDistToWayDest > prevDistToWayDest)
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
						if (PathfinderAllowsMovement && MM.rotLenSq < myRotator.rotLenSq_stopAndRot && CNS.SpecialFlyingInstructions == NavSettings.SpecialFlying.None)
							StartMoveMove();
					}
					break;
				case NavSettings.Moving.HYBRID:
					{
						//myLogger.debugLog("movingTooSlow = " + movingTooSlow + ", currentMove = " + currentMove, "calcMoveAndRotate()");
						if (movingTooSlow
							|| (currentMove != Vector3.Zero && currentMove != SpeedControl.cruiseForward))
							calcAndMove(true); // continue in current state
						if (MM.rotLenSq < rotLenSq_switchToMove)
						{
							myLogger.debugLog("switching to move", "calcMoveAndRotate()", Logger.severity.DEBUG);
							StartMoveMove();
						}
						break;
					}
				case NavSettings.Moving.SIDELING:
					{
						if (CNS.isAMissile)
						{
							log("missile needs to stop sideling", "calcMoveAndRotate()", Logger.severity.DEBUG);
							fullStop("stop sidel: converted to missile");
							break;
						}
						calcAndMove(true); // continue in current state
						break;
					}
				case NavSettings.Moving.NOT_MOVE:
					{
						if (CNS.rotateState == NavSettings.Rotating.NOT_ROTA)
							MoveIfPossible();
						break;
					}
				default:
					{
						log("Not Yet Implemented, state = " + CNS.moveState, "calcMoveAndRotate()", Logger.severity.ERROR);
						break;
					}
			}

			if (CNS.moveState != NavSettings.Moving.SIDELING) // && CNS.landingState == NavSettings.LANDING.OFF)
				calcAndRotate();
		}

		private bool MoveIfPossible()
		{
			if (PathfinderAllowsMovement)
			{
				if (CNS.isAMissile)
				{
					StartMoveHybrid();
					return true;
				}
				if (CNS.SpecialFlyingInstructions == NavSettings.SpecialFlying.Line_SidelForward)// || MM.rotLenSq > rotLenSq_switchToMove)
				{
					//if (MM.rotLenSq <= rotLenSq_tight)
					//{
					//	StartMoveMove();
					//	return true;
					//}
					//if (!canSidel)
					//	return false;
					StartMoveSidel();
					return true;
				}
				if (myHarvester.NavigationDrill != null) // if harvester has not set Line_SidelForward, normal move
				{
					StartMoveMove();
					return true;
				}
				if (CNS.landingState == NavSettings.LANDING.OFF && MM.distToWayDest > myGrid.GetLongestDim() + CNS.destinationRadius)
				{
					StartMoveHybrid();
					return true;
				}
				//if (!canSidel)
				//	return false;
				StartMoveSidel();
				return true;
			}
			reportState(ReportableState.PATHFINDING);
			return false;
		}

		private void StartMoveHybrid()
		{
			calcAndMove(true);
			CNS.moveState = NavSettings.Moving.HYBRID;
			reportState(ReportableState.MOVING);
		}

		private void StartMoveSidel()
		{
			calcAndMove(true);
			CNS.moveState = NavSettings.Moving.SIDELING;
			reportState(ReportableState.MOVING);
		}

		private void StartMoveMove()
		{
			calcAndMove();
			CNS.moveState = NavSettings.Moving.MOVING;
			reportState(ReportableState.MOVING);
		}

		public const float rotLenSq_switchToMove = 0.00762f; // 5°

		/// <summary>
		/// start moving when less than (30°)
		/// </summary>
		public const float rotLenSq_startMove = 0.274f;

		/// <summary>
		/// stop when greater than
		/// </summary>
		private const float onCourse_sidel = 0.1f, onCourse_hybrid = 0.1f;

		private Vector3 moveDirection = Vector3.Zero;

		private void calcAndMove(bool sidel = false)//, bool anyState=false)
		{
			//log("entered calcAndMove("+doSidel+")", "calcAndMove()", Logger.severity.TRACE);
			try
			{
				if (sidel)
				{
					Vector3 worldDisplacement = ((Vector3D)CNS.getWayDest() - (Vector3D)getNavigationBlock().GetPosition());
					RelativeVector3F displacement = RelativeVector3F.createFromWorld(worldDisplacement, myGrid); // Only direction matters, we will normalize later. A multiplier helps prevent precision issues.
					Vector3 course = Vector3.Normalize(displacement.getWorld());
					float offCourse = Vector3.RectangularDistance(course, moveDirection);

					switch (CNS.moveState)
					{
						case NavSettings.Moving.SIDELING:
							{
								//log("inflight adjust sidel " + direction + " for " + displacement + " to " + destination, "calcAndMove()", Logger.severity.TRACE);
								//if (Math.Abs(moveDirection.X - course.X) < onCourse_sidel && Math.Abs(moveDirection.Y - course.Y) < onCourse_sidel && Math.Abs(moveDirection.Z - course.Z) < onCourse_sidel)
								if (offCourse < onCourse_sidel)
								{
									if (movingTooSlow)
										goto case NavSettings.Moving.NOT_MOVE;
									//log("cancel inflight adjustment: no change", "calcAndMove()", Logger.severity.TRACE);
									return;
								}
								else
								{
									myLogger.debugLog("rectangular distance between " + course + " and " + moveDirection + " is " + offCourse, "calcAndMove()");
									//log("sidel, stop to change course", "calcAndMove()", Logger.severity.TRACE);
									fullStop("change course: sidel");
									return;
								}
							}
						case NavSettings.Moving.HYBRID:
							{
								//if (Math.Abs(moveDirection.X - course.X) < onCourse_hybrid && Math.Abs(moveDirection.Y - course.Y) < onCourse_hybrid && Math.Abs(moveDirection.Z - course.Z) < onCourse_hybrid)
								if (offCourse < onCourse_hybrid)
									goto case NavSettings.Moving.NOT_MOVE;
								else
								{
									myLogger.debugLog("rectangular distance between " + course + " and " + moveDirection + " is " + offCourse, "calcAndMove()");
									//log("off course, switching to move", "calcAndMove()", Logger.severity.DEBUG);
									CNS.moveState = NavSettings.Moving.MOVING;
									calcAndMove();
									return;
								}
							}
						case NavSettings.Moving.NOT_MOVE:
							{
								RelativeVector3F scaled = currentThrust.scaleByForce(displacement, getNavigationBlock());
								moveOrder(scaled);
								if (CNS.moveState == NavSettings.Moving.NOT_MOVE)
								{
									moveDirection = course;
									log("sideling. wayDest=" + CNS.getWayDest() + ", worldDisplacement=" + worldDisplacement + ", RCdirection=" + course, "calcAndMove()", Logger.severity.DEBUG);
									log("... scaled=" + scaled.getWorld() + ":" + scaled.getLocal() + ":" + scaled.getBlock(getNavigationBlock()), "calcAndMove()", Logger.severity.DEBUG);
								}
								break;
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
					moveOrder(Vector3.Forward); // move forward
					log("moving " + MM.distToWayDest + " to " + CNS.getWayDest(), "calcAndMove()", Logger.severity.DEBUG);
				}
			}
			finally
			{
				stoppedMovingAt = DateTime.UtcNow + stoppedAfter;
				movingTooSlow = false;
			}
		}

		internal void calcAndRotate()
		{ myRotator.calcAndRotate(); }

		internal void calcAndRoll(float roll)
		{ myRotator.calcAndRoll(roll); }

		private static TimeSpan stoppedAfter = new TimeSpan(0, 0, 0, 1);
		private DateTime stoppedMovingAt;
		private static float stoppedPrecision = 0.2f;

		public bool checkStopped()
		{
			if (CNS.moveState == NavSettings.Moving.NOT_MOVE)
				return true;

			bool isStopped;

			//log("checking movementSpeed "+movementSpeed, "checkStopped()", Logger.severity.TRACE);
			if (MM.movementSpeed == null || MM.movementSpeed > stoppedPrecision)
			{
				//log("fast", "checkStopped()", Logger.severity.TRACE);
				stoppedMovingAt = DateTime.UtcNow + stoppedAfter;
				isStopped = false;
			}
			else
			{
				//log("stopped in " + (stoppedMovingAt - DateTime.UtcNow).TotalMilliseconds, "checkStopped()", Logger.severity.TRACE);
				isStopped = DateTime.UtcNow > stoppedMovingAt;
			}

			if (isStopped)
			{
				if (CNS.moveState == NavSettings.Moving.STOP_MOVE)
				{
					//log("now stopped");
					CNS.moveState = NavSettings.Moving.NOT_MOVE;
					CNS.clearSpeedInternal();
				}
				else
					CNS.moveState = NavSettings.Moving.STOP_MOVE;
			}
			else
				if (CNS.moveState == NavSettings.Moving.STOP_MOVE)
					reportState(ReportableState.STOPPING);

			return isStopped;
		}

		/// <summary>
		/// for other kinds of stop use moveOrder(Vector3.Zero) or similar
		/// </summary>
		internal void fullStop(string reason)
		{
			if (currentMove == Vector3.Zero && currentRotate == Vector2.Zero && currentRoll == 0 && dampenersEnabled()) // already stopped
				return;

			log("full stop: " + reason, "fullStop()", Logger.severity.INFO);
			reportState(ReportableState.STOPPING);
			currentMove = Vector3.Zero;
			currentRotate = Vector2.Zero;
			currentRoll = 0;
			prevDistToWayDest = float.MaxValue;

			EnableDampeners();
			currentRCcontrol.MoveAndRotateStopped();

			CNS.moveState = NavSettings.Moving.STOP_MOVE;
			CNS.rotateState = NavSettings.Rotating.STOP_ROTA;
		}

		internal Vector3 currentMove = Vector3.Zero;
		internal Vector2 currentRotate = Vector2.Zero;
		internal float currentRoll = 0;

		internal void moveOrder(Vector3 move, bool normalize = true)
		{
			//log("entered moveOrder("+move+")");
			if (normalize)
				move = Vector3.Normalize(move);
			if (!move.IsValid())
				move = Vector3.Zero;
			if (currentMove == move)
				return;
			currentMove = move;
			moveAndRotate();
			if (move != Vector3.Zero)
			{
				myLogger.debugLog("Enabling dampeners", "moveOrder()");
				EnableDampeners();
			}
		}

		internal void moveOrder(RelativeVector3F move, bool normalize = true)
		{
			moveOrder(move.getBlock(currentRCblock), normalize);
		}

		internal void moveAndRotate()
		{
			//isCruising = false;
			if (currentMove == Vector3.Zero && currentRotate == Vector2.Zero && currentRoll == 0)
			{
				log("MAR is actually stop", "moveAndRotate()");
				currentRCcontrol.MoveAndRotateStopped();
			}
			else
			{
				if (CNS.moveState != NavSettings.Moving.HYBRID)
					log("doing MAR(" + currentMove + ", " + currentRotate + ", " + currentRoll + ")", "moveAndRotate()");
				currentRCcontrol.MoveAndRotate(currentMove, currentRotate, currentRoll);
			}
		}

		public bool dampenersEnabled()
		{ return ((currentRCcontrol as Ingame.IMyShipController).DampenersOverride) && !currentThrust.disabledThrusters(); }

		internal void DisableReverseThrust()
		{
			//if (enable)
			//{
			//	currentThrust.enableAllThrusters();
			//	EnableDampeners();
			//}
			//else
			//{
				switch (CNS.moveState)
				{
					case NavSettings.Moving.HYBRID:
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
				
			//}
		}

		public void EnableDampeners(bool dampenersOn = true)
		{
			//if (dampenersOn)
			//{
			//	//myLogger.debugLog("enabling all thrusters", "setDampeners()");
			//	currentThrust.enableAllThrusters();
			//}
			//else
			//	if (CNS.moveState == NavSettings.Moving.MOVING || CNS.rotateState != NavSettings.Rotating.NOT_ROTA)
			//	{
			//		myLogger.debugLog("disabling reverse thrusters", "setDampeners()");
			//		currentThrust.disableThrusters(Base6Directions.GetFlippedDirection(currentRCblock.Orientation.Forward));
			//		return;
			//	}

			if (dampenersOn)
				currentThrust.enableAllThrusters();

			try
			{
				if ((currentRCcontrol as Ingame.IMyShipController).DampenersOverride != dampenersOn)
				{
					currentRCcontrol.SwitchDamping(); // sometimes SwitchDamping() throws a NullReferenceException while grid is being destroyed
					if (!dampenersOn)
						log("speed control: disabling dampeners. speed=" + MM.movementSpeed + ", cruise=" + CNS.getSpeedCruise() + ", slow=" + CNS.getSpeedSlow(), "setDampeners()", Logger.severity.TRACE);
					else
						log("speed control: enabling dampeners. speed=" + MM.movementSpeed + ", cruise=" + CNS.getSpeedCruise() + ", slow=" + CNS.getSpeedSlow(), "setDampeners()", Logger.severity.TRACE);
				}
			}
			catch (NullReferenceException)
			{ log("setDampeners() threw NullReferenceException", "setDampeners()", Logger.severity.DEBUG); }
		}

		public override string ToString()
		{
			return "Nav:" + myGrid.DisplayName;
		}

		public enum ReportableState : byte
		{
			OFF, PATHFINDING, NO_PATH, NO_DEST, WAITING,
			ROTATING, MOVING, STOPPING, HYBRID, SIDEL, ROLL,
			MISSILE, ENGAGING, LANDED, PLAYER, JUMP, GET_OUT_OF_SEAT
		};
		private ReportableState currentReportable = ReportableState.OFF;

		internal bool GET_OUT_OF_SEAT = false;

		internal void reportState()
		{ reportState(currentReportable); }

		/// <summary>
		/// may ignore the given state, if Nav is actually in another state
		/// </summary>
		/// <param name="newState"></param>
		internal void reportState(ReportableState newState)
		{
			//log("entered reportState()", "reportState()", Logger.severity.TRACE);
			if (currentRemoteControl_Value == null)
			{
				//log("cannot report without RC", "reportState()", Logger.severity.TRACE);
				return;
			}

			string displayName = (currentRemoteControl_Value as Sandbox.ModAPI.IMyCubeBlock).DisplayNameText;
			if (displayName == null)
			{
				alwaysLog(Logger.severity.WARNING, "reportState()", "cannot report without display name");
				return;
			}

			reportExtra(ref newState);

			if (CNS.EXIT)
				newState = ReportableState.OFF;
			if (CNS.landingState == NavSettings.LANDING.LOCKED)
				newState = ReportableState.LANDED;
			if (GET_OUT_OF_SEAT) // must override LANDED
				newState = ReportableState.GET_OUT_OF_SEAT;

			// did state actually change?
			if (newState == currentReportable && newState != ReportableState.JUMP && newState != ReportableState.WAITING) // jump and waiting update times
				return;
			currentReportable = newState;

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
			//reportPathfinding(newName);
			// error
			if (myInterpreter.instructionErrorIndex != null)
				newName.Append("ERROR(" + myInterpreter.instructionErrorIndex + ") : ");
			// actual state
			newName.Append(newState);
			// wait time
			if (newState == ReportableState.WAITING)
			{
				newName.Append(':');
				newName.Append((int)(CNS.waitUntil - DateTime.UtcNow).TotalSeconds);
			}

			newName.Append('>');
			newName.Append(displayName);
			//RCDisplayName = newName.ToString();

			//ignore_RemoteControl_nameChange = true;
			(currentRemoteControl_Value as Ingame.IMyTerminalBlock).SetCustomName(newName);
			//ignore_RemoteControl_nameChange = false;
			log("added ReportableState to RC: " + newState, "reportState()", Logger.severity.TRACE);
		}

		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void reportExtra(ref ReportableState reportState)
		{
			switch (reportState)
			{
				case ReportableState.MOVING:
					switch (CNS.moveState)
					{
						case NavSettings.Moving.SIDELING:
							reportState = ReportableState.SIDEL;
							break;
						case NavSettings.Moving.HYBRID:
							reportState = ReportableState.HYBRID;
							break;
					}
					return;
				case ReportableState.ROTATING:
					if (CNS.rollState == NavSettings.Rolling.ROLLING)
						reportState = ReportableState.ROLL;
					return;
			}
		}

		//[System.Diagnostics.Conditional("LOG_ENABLED")]
		//private void reportPathfinding(StringBuilder newName)
		//{
		//	if (myPathfinder_Output != null)
		//		newName.Append(myPathfinder_Output.PathfinderResult);
		//	newName.Append(':');
		//}

		//private bool ignore_RemoteControl_nameChange = false;
		public string instructions { get; private set; }
		private void remoteControl_OnNameChanged(IMyTerminalBlock whichBlock)
		{
			string instructionsInBlock = (whichBlock as IMyCubeBlock).getInstructions();
			if (instructions == null || !instructions.Equals(instructionsInBlock))
			{
				//log("RC name changed: " + whichBlock.CustomName+"; inst were: "+allInstructions+"; are now: "+instructionsInBlock, "remoteControl_OnNameChanged()", Logger.severity.DEBUG);
				instructions = instructionsInBlock;
				remoteControlIsNotReady = true;
				reset();
				CNS.waitUntilNoCheck = DateTime.UtcNow.AddSeconds(1);
			}
		}
	}
}
