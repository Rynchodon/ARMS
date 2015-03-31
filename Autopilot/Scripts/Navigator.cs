#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Sandbox.Common;
//using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
//using Sandbox.Definitions;
//using Sandbox.Engine;
//using Sandbox.Game;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRageMath;

using Rynchodon.Autopilot.Instruction;
using Rynchodon.Autopilot.Pathfinder;
using Rynchodon.AntennaRelay;

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
		private Collision myCollisionObject;
		internal GridDimensions myGridDim;
		internal ThrustProfiler currentThrust;
		internal Targeter myTargeter;

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
				//IMyCubeBlock currentRCblock = (currentRemoteControl_Value as IMyCubeBlock); // WTF?
				myLand = null;
				if (currentRemoteControl_Value == null)
				{
					myGridDim = null;
					myCollisionObject = null;
					CNS = new NavSettings(null);
				}
				else
				{
					myGridDim = new GridDimensions(currentRCblock);
					myCollisionObject = new Collision(myGridDim); //(currentRCblock, out distance_from_RC_to_front);
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

				// some variables
				rotationPower = 3f;
				decelerateRotation = 1f / 2f;
				inflightRotatingPower = 3f;
				inflightDecelerateRotation = 1f / 2f;
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
				return currentRCblock;
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
			myLogger = new Logger(myGrid.DisplayName, "Navigator");
		}

		private bool needToInit = true;

		private void init()
		{
			//	find remote control blocks
			remoteControlBlocks = new List<Sandbox.ModAPI.IMySlimBlock>();
			myGrid.GetBlocks(remoteControlBlocks, block => block.FatBlock != null && block.FatBlock.BlockDefinition.TypeId == remoteControlType);

			// register for events
			myGrid.OnBlockAdded += OnBlockAdded;
			//myGrid.OnBlockOwnershipChanged += OnBlockOwnershipChanged;
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
								log("got a block as a destination: " + CNS.GridDestName + ":" + CNS.searchBlockName, "update()", Logger.severity.INFO);
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
								//reportState(ReportableState.PATHFINDING);
								return;
							case NavSettings.TypeOfWayDest.LAND:
								log("got a new landing destination " + CNS.getWayDest(), "update()", Logger.severity.INFO);
								//reportState(ReportableState.PATHFINDING);
								return;
							case NavSettings.TypeOfWayDest.NULL:
								break; // keep searching
							default:
								alwaysLog("got an invalid TypeOfWayDest: " + CNS.getTypeOfWayDest(), "update()", Logger.severity.WARNING);
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
								myInterpreter.enqueueAllActions(instructions);
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
		private Rynchodon.Autopilot.Jumper.GridJumper myJump = null;

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
			if (myJump != null && myJump.currentState != Jumper.GridJumper.State.OFF)
			{
				myJump.update(); // continue jumping
				reportState(ReportableState.JUMP);
				return;
			}
			if (CNS.jump_to_dest && CNS.moveState == NavSettings.Moving.NOT_MOVE && CNS.rotateState == NavSettings.Rotating.NOT_ROTA && MM.distToWayDest > 100)
			{
				myJump = new Jumper.GridJumper(myGridDim, MM.currentWaypoint);
				if (myJump.trySetJump())
				{
					log("started Jumper", "navigateSub()", Logger.severity.DEBUG);
					reportState(ReportableState.JUMP);
					return;
				}
				else
					log("could not start Jumper", "navigateSub()", Logger.severity.DEBUG);
			}

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
				log("missile never reaches destination", "checkAt_wayDest()", Logger.severity.TRACE);
				return false;
			}
			if (MM.distToWayDest > CNS.destinationRadius)
			{
				log("keep approaching; too far (" + MM.distToWayDest + " > " + CNS.destinationRadius + ")", "checkAt_wayDest()", Logger.severity.TRACE);
				return false;
			}
			if (CNS.landLocalBlock != null && MM.distToWayDest > radiusLandWay) // distance to start landing
			{
				log("keep approaching; getting ready to land (" + MM.distToWayDest + " > " + radiusLandWay + ")", "checkAt_wayDest()", Logger.severity.TRACE);
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
				CNS.moveState = NavSettings.Moving.MOVING; // to allow speed control to restart movement
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
				Collision.collisionAvoidResult currentAvoidResult = myCollisionObject.avoidCollisions(ref CNS, updateCount);
				switch (currentAvoidResult)
				{
					case Collision.collisionAvoidResult.NOT_FINISHED:
						CNS.collisionUpdateSinceWaypointAdded++;
						break;
					case Collision.collisionAvoidResult.NOT_PERFORMED:
						break;
					case Collision.collisionAvoidResult.ALTERNATE_PATH:
						CNS.noWayForward = false;
						log("got a currentAvoidResult " + currentAvoidResult + ", new waypoint is " + CNS.getWayDest(), "navigate()", Logger.severity.TRACE);
						CNS.collisionUpdateSinceWaypointAdded += collisionUpdatesBeforeMove;
						break;
					case Collision.collisionAvoidResult.NO_WAY_FORWARD:
						log("got a currentAvoidResult " + currentAvoidResult, "navigate()", Logger.severity.INFO);
						CNS.noWayForward = true;
						reportState(ReportableState.NO_PATH);
						fullStop("No Path");
						return;
					case Collision.collisionAvoidResult.NO_OBSTRUCTION:
						CNS.noWayForward = false;
						CNS.collisionUpdateSinceWaypointAdded += collisionUpdatesBeforeMove;
						break;
					default:
						alwaysLog(Logger.severity.ERROR, "navigate()", "Error: unsuitable case from avoidCollisions(): " + currentAvoidResult);
						fullStop("Invalid collisionAvoidResult");
						return;
				}
			}
			calcMoveAndRotate();
		}

		private static readonly byte collisionUpdatesBeforeMove = 100;
		internal bool movingTooSlow = false;

		private void calcMoveAndRotate()
		{
			//log("entered calcMoveAndRotate(" + pitch + ", " + yaw + ", " + CNS.getWayDest() + ")", "calcMoveAndRotate()", Logger.severity.TRACE);

			if (CNS.noWayForward)
				return;

			SpeedControl.controlSpeed(this);
			//log("reached missile check", "calcMoveAndRotate()", Logger.severity.TRACE);

			switch (CNS.moveState)
			{
				case NavSettings.Moving.MOVING:
					// will go here after clearing a waypoint or destination, do not want to switch to hybrid, as we may need to kill alot of inertia
					{
						if (movingTooSlow && CNS.collisionUpdateSinceWaypointAdded >= collisionUpdatesBeforeMove) //speed up test. missile will never pass this test
							if (MM.rotLenSq < rotLenSq_startMove)
							{
								calcAndMove();
								reportState(ReportableState.MOVING);
							}
					}
					break;
				case NavSettings.Moving.STOP_MOVE:
					break;
				case NavSettings.Moving.HYBRID:
					{
						if (MM.distToWayDest < CNS.destinationRadius * 1.5
							|| MM.rotLenSq < rotLenSq_switchToMove)
						{
							log("on course or nearing dest, switch to moving", "calcAndRotate()", Logger.severity.DEBUG);
							calcAndMove();
							CNS.moveState = NavSettings.Moving.MOVING; // switch to moving
							reportState(ReportableState.MOVING);
							break;
						}
						if (currentMove != Vector3.Zero && currentMove != SpeedControl.cruiseForward) // otherwise we should be slowing
							calcAndMove(true); // continue in current state
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
						{
							if (CNS.isAMissile || CNS.collisionUpdateSinceWaypointAdded >= collisionUpdatesBeforeMove)
							{
								if (!CNS.isAMissile && MM.distToWayDest < myGridDim.getLongestDim() + CNS.destinationRadius) // need to decide between sidel or hybrid. might need an option for moving
								{
									calcAndMove(true);
									CNS.moveState = NavSettings.Moving.SIDELING; // switch to sideling
									reportState(ReportableState.MOVING);
								}
								else // missile will always end up here (for case NOT_MOVE)
								{
									calcAndMove(true);
									CNS.moveState = NavSettings.Moving.HYBRID; // switch to hybrid
									reportState(ReportableState.MOVING);
								}
							}
							else
								reportState(ReportableState.PATHFINDING);
						}
						break;
					}
				default:
					{
						log("Not Yet Implemented, state = " + CNS.moveState, "calcMoveAndRotate()", Logger.severity.ERROR);
						break;
					}
			}

			if (CNS.moveState != NavSettings.Moving.SIDELING)
				calcAndRotate();
		}

		/// <summary>
		/// not squared (5°)
		/// </summary>
		public const float rotLen_minimum = 0.0873f;
		/// <summary>
		/// switch from hybrid to moving when less than (5°)
		/// </summary>
		public const float rotLenSq_switchToMove = 0.00762f;
		/// <summary>
		/// start moving when less than (30°)
		/// </summary>
		public const float rotLenSq_startMove = 0.274f;
		/// <summary>
		/// stop and rotate when greater than (90°)
		/// </summary>
		public const float rotLenSq_stopAndRot = 2.47f;

		/// <summary>
		/// stop when greater than
		/// </summary>
		private const float onCourse_sidel = 0.1f, onCourse_hybrid = 0.1f;

		private Vector3 moveDirection = Vector3.Zero;

		private void calcAndMove(bool sidel = false)//, bool anyState=false)
		{
			//log("entered calcAndMove("+doSidel+")", "calcAndMove()", Logger.severity.TRACE);
			movingTooSlow = false;
			if (sidel)
			{
				Vector3 worldDisplacement = ((Vector3D)CNS.getWayDest() - (Vector3D)getNavigationBlock().GetPosition());
				RelativeVector3F displacement = RelativeVector3F.createFromWorld(worldDisplacement, myGrid); // Only direction matters, we will normalize later. A multiplier helps prevent precision issues.
				Vector3 course = Vector3.Normalize(displacement.getWorld());

				switch (CNS.moveState)
				{
					case NavSettings.Moving.SIDELING:
						{
							//log("inflight adjust sidel " + direction + " for " + displacement + " to " + destination, "calcAndMove()", Logger.severity.TRACE);
							if (Math.Abs(moveDirection.X - course.X) < onCourse_sidel && Math.Abs(moveDirection.Y - course.Y) < onCourse_sidel && Math.Abs(moveDirection.Z - course.Z) < onCourse_sidel)
							{
								//log("cancel inflight adjustment: no change", "calcAndMove()", Logger.severity.TRACE);
								return;
							}
							else
							{
								//log("sidel, stop to change course", "calcAndMove()", Logger.severity.TRACE);
								fullStop("change course: sidel");
								return;
							}
						}
					case NavSettings.Moving.HYBRID:
						{
							if (Math.Abs(moveDirection.X - course.X) < onCourse_hybrid && Math.Abs(moveDirection.Y - course.Y) < onCourse_hybrid && Math.Abs(moveDirection.Z - course.Z) < onCourse_hybrid)
								goto case NavSettings.Moving.NOT_MOVE;
							else
							{
								log("off course, switching to move", "calcAndMove()", Logger.severity.DEBUG);
								CNS.moveState = NavSettings.Moving.MOVING;
								calcAndMove();
								return;
							}
						}
					case NavSettings.Moving.NOT_MOVE:
						{
							RelativeVector3F scaled = currentThrust.scaleByForce(displacement, currentRCblock);
							moveOrder(scaled);
							if (CNS.moveState == NavSettings.Moving.NOT_MOVE)
							{
								moveDirection = course;
								log("sideling. wayDest=" + CNS.getWayDest() + ", worldDisplacement=" + worldDisplacement + ", RCdirection=" + course, "calcAndMove()", Logger.severity.DEBUG);
								log("... scaled=" + scaled.getWorld() + ":" + scaled.getGrid() + ":" + scaled.getBlock(currentRCblock), "calcAndMove()", Logger.severity.DEBUG);
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

			stoppedMovingAt = DateTime.UtcNow + stoppedAfter;
		}

		private double pitchNeedToRotate = 0, yawNeedToRotate = 0;

		/// <summary>
		/// 
		/// </summary>
		/// <param name="pitch"></param>
		/// <param name="yaw"></param>
		/// <param name="precision_stopAndRot">for increasing precision of rotLenSq_stopAndRot</param>
		internal void calcAndRotate(float? precision_stopAndRot = null)
		{
			if (precision_stopAndRot == null)
				precision_stopAndRot = rotLenSq_stopAndRot;

			//log("need to rotate "+rot.Length());
			// need to rotate
			switch (CNS.rotateState)
			{
				case NavSettings.Rotating.NOT_ROTA:
					{
						switch (CNS.moveState)
						{
							case NavSettings.Moving.MOVING:
								{
									if (MM.rotLenSq > precision_stopAndRot)
									{
										//log("stopping to rotate", "calcAndRotate()");
										fullStop("stopping to rorate");
										return;
									}
									else
										goto case NavSettings.Moving.HYBRID;
								}
							case NavSettings.Moving.HYBRID:
								{
									pitchNeedToRotate = MM.pitch;// +pitchRotatePast;
									yawNeedToRotate = MM.yaw;// +yawRotatePast;
									if (Math.Abs(pitchNeedToRotate) < rotLen_minimum)
										pitchNeedToRotate = 0;
									if (Math.Abs(yawNeedToRotate) < rotLen_minimum)
										yawNeedToRotate = 0;
									if (pitchNeedToRotate == 0 && yawNeedToRotate == 0)
										return;
									log("need to adjust by " + MM.pitch + " & " + MM.yaw, "calcAndRotate()");
									changeRotationPower = !changeRotationPower;
									rotateOrder(); // rotate towards target
									CNS.rotateState = NavSettings.Rotating.ROTATING;
									maxRotateTime = DateTime.UtcNow.AddSeconds(3);
									return;
								}
							case NavSettings.Moving.NOT_MOVE:
								{
									if (!CNS.isAMissile && CNS.collisionUpdateSinceWaypointAdded < collisionUpdatesBeforeMove)
										return;
									pitchNeedToRotate = MM.pitch;
									yawNeedToRotate = MM.yaw;
									if (Math.Abs(pitchNeedToRotate) < rotLen_minimum)
										pitchNeedToRotate = 0;
									if (Math.Abs(yawNeedToRotate) < rotLen_minimum)
										yawNeedToRotate = 0;
									if (pitchNeedToRotate == 0 && yawNeedToRotate == 0)
										return;
									log("starting rotation: " + MM.pitch + ", " + MM.yaw + ", updates=" + collisionUpdatesBeforeMove, "calcAndRotate()");
									changeRotationPower = !changeRotationPower;
									rotateOrder(); // rotate towards target
									CNS.rotateState = NavSettings.Rotating.ROTATING;
									reportState(ReportableState.ROTATING);
									maxRotateTime = DateTime.UtcNow.AddSeconds(3);
									return;
								}
						}
						return;
					}
				case NavSettings.Rotating.STOP_ROTA:
					{
						if (isNotRotating())
						{
							adjustRotationPower();
							pitchNeedToRotate = 0;
							yawNeedToRotate = 0;
							CNS.rotateState = NavSettings.Rotating.NOT_ROTA;
						}
						return;
					}
				case NavSettings.Rotating.ROTATING:
					{
						// check for need to derotate
						float whichDecelRot;
						if (CNS.moveState == NavSettings.Moving.MOVING)
							whichDecelRot = inflightDecelerateRotation;
						else
							whichDecelRot = decelerateRotation;
						bool needToStopRot = false;
						if (pitchNeedToRotate > rotLen_minimum && MM.pitch < pitchNeedToRotate * whichDecelRot)
						{
							log("decelerate rotation: first case " + MM.pitch + " < " + pitchNeedToRotate * whichDecelRot, "calcAndRotate()", Logger.severity.TRACE);
							needToStopRot = true;
						}
						else if (pitchNeedToRotate < -rotLen_minimum && MM.pitch > pitchNeedToRotate * whichDecelRot)
						{
							log("decelerate rotation: second case " + MM.pitch + " > " + pitchNeedToRotate * whichDecelRot, "calcAndRotate()", Logger.severity.TRACE);
							needToStopRot = true;
						}
						else if (yawNeedToRotate > rotLen_minimum && MM.yaw < yawNeedToRotate * whichDecelRot)
						{
							log("decelerate rotation: third case " + MM.yaw + " < " + yawNeedToRotate * whichDecelRot, "calcAndRotate()", Logger.severity.TRACE);
							needToStopRot = true;
						}
						else if (yawNeedToRotate < -rotLen_minimum && MM.yaw > yawNeedToRotate * whichDecelRot)
						{
							log("decelerate rotation: fourth case " + MM.yaw + " > " + yawNeedToRotate * whichDecelRot, "calcAndRotate()", Logger.severity.TRACE);
							needToStopRot = true;
						}
						else if (DateTime.UtcNow.CompareTo(maxRotateTime) > 0)
						{
							log("decelerate rotation: times up ", "calcAndRotate()", Logger.severity.TRACE);
							needToStopRot = true;
						}

						if (needToStopRot)
						{
							log("decelerate rotation (" + MM.pitch + " / " + pitchNeedToRotate + ", " + MM.yaw + " / " + yawNeedToRotate + ")");
							rotateOrder(Vector2.Zero); // stop rotating
							CNS.rotateState = NavSettings.Rotating.STOP_ROTA;
						}
						return;
					}
			}
		}

		private float needToRoll = 0;
		private float? lastRoll = null;
		private DateTime stoppedRollingAt = DateTime.UtcNow;

		/// <summary>
		/// does not check for moving or rotating
		/// </summary>
		/// <param name="roll"></param>
		internal void calcAndRoll(float roll)
		{
			switch (CNS.rollState)
			{
				case NavSettings.Rolling.NOT_ROLL:
					{
						log("rollin' rollin' rollin' " + roll, "calcAndRoll()", Logger.severity.DEBUG);
						needToRoll = roll;
						rollOrder(roll * rotationPower);
						CNS.rollState = NavSettings.Rolling.ROLLING;
						maxRotateTime = DateTime.UtcNow.AddSeconds(3);
						reportState(ReportableState.ROTATING);
					}
					return;
				case NavSettings.Rolling.ROLLING:
					{
						if (Math.Sign(roll) * Math.Sign(needToRoll) <= 0 || Math.Abs(roll) < Math.Abs(needToRoll) * decelerateRotation || DateTime.UtcNow.CompareTo(maxRotateTime) > 0)
						{
							//log("Math.Sign(roll) = " + Math.Sign(roll) + ", Math.Sign(needToRoll) = " + Math.Sign(needToRoll) + ", Math.Abs(roll) = " + Math.Abs(roll) + ", Math.Abs(needToRoll) = " + Math.Abs(needToRoll)
							//	+ ", decelerateRotation = " + decelerateRotation + ", DateTime.UtcNow = " + DateTime.UtcNow + ", maxRotateTime = " + maxRotateTime);
							log("decelerate roll, roll=" + roll + ", needToRoll=" + needToRoll, "calcAndRoll()", Logger.severity.DEBUG);
							rollOrder(0);
							CNS.rollState = NavSettings.Rolling.STOP_ROLL;
						}
					}
					return;
				case NavSettings.Rolling.STOP_ROLL:
					{
						if (lastRoll == null || lastRoll != roll) // is rolling
						{
							stoppedRollingAt = DateTime.UtcNow + stoppedAfter;
							lastRoll = roll;
						}
						else // stopped rolling
							if (DateTime.UtcNow.CompareTo(stoppedRollingAt) > 0)
							{
								log("get off the log (done rolling) ", "calcAndRoll()", Logger.severity.DEBUG);
								adjustRotationPower();
								lastRoll = null;
								CNS.rollState = NavSettings.Rolling.NOT_ROLL;
							}
					}
					return;
			}
		}

		// rotationPower and decelerateRotation are also used and affected by rolling
		internal float rotationPower = 3f;
		private float decelerateRotation = 1f / 4f; // how much of rotation should be deceleration
		internal float inflightRotatingPower = 10;
		private float inflightDecelerateRotation = 1f / 4f;

		private static float decelerateAdjustmentOver = 1.15f; // adjust decelerate by this much when overshoot
		private static float decelerateAdjustmentUnder = 0.90f; // adjust decelerate by this much when undershoot
		private static float rotationPowerAdjustmentOver = 0.85f;
		private static float rotationPowerAdjustmentUnder = 1.10f;

		private void adjustRotationPower()
		{
			int overUnder = 0;
			//	check for overshoot/undershoot
			if (Math.Abs(MM.pitch) > 0.1 && Math.Abs(pitchNeedToRotate) > 0.1)
				if (Math.Sign(MM.pitch) == Math.Sign(pitchNeedToRotate)) // same sign
					overUnder--;
				else // different sign
					overUnder++;
			if (Math.Abs(MM.yaw) > 0.1 && Math.Abs(yawNeedToRotate) > 0.1)
				if (Math.Sign(MM.yaw) == Math.Sign(yawNeedToRotate)) // same sign
					overUnder--;
				else // different sign
					overUnder++;

			if (overUnder != 0)
			{
				log("checking for over/under shoot on rotation: pitch=" + MM.pitch + ", needPitch=" + pitchNeedToRotate + ", yaw=" + MM.yaw + ", needYaw=" + yawNeedToRotate);
				if (overUnder > 0) // over rotated
					if (changeRotationPower)
						adjustRotationPowerBy(rotationPowerAdjustmentOver);
					else
						adjustRotationPowerBy(decelerateAdjustmentOver);
				else // under rotated
					if (changeRotationPower)
						adjustRotationPowerBy(rotationPowerAdjustmentUnder);
					else
						adjustRotationPowerBy(decelerateAdjustmentUnder);

				log("power adjusted, under/over is " + overUnder + " pitch=" + MM.pitch + "/" + pitchNeedToRotate + ", yaw=" + MM.yaw + "/" + yawNeedToRotate);
			}
		}

		private bool changeRotationPower;

		private void adjustRotationPowerBy(float adjustBy)
		{
			if (changeRotationPower)
			{
				if (CNS.moveState == NavSettings.Moving.MOVING)
				{
					inflightRotatingPower *= adjustBy;
					log("adjusted inflightRotatingPower, new value is " + inflightRotatingPower, "adjustRotationPowerBy", Logger.severity.DEBUG);
				}
				else
				{
					rotationPower *= adjustBy;
					log("adjusted rotationPower, new value is " + rotationPower, "adjustRotationPowerBy", Logger.severity.DEBUG);
				}
			}
			else
			{
				if (CNS.moveState == NavSettings.Moving.MOVING)
				{
					inflightDecelerateRotation *= adjustBy;
					log("adjusted inflightDecelerateRotation, new value is " + inflightDecelerateRotation, "adjustRotationPowerBy", Logger.severity.DEBUG);
				}
				else
				{
					decelerateRotation *= adjustBy;
					log("adjusted decelerateRotation, new value is " + decelerateRotation, "adjustRotationPowerBy", Logger.severity.DEBUG);
				}
			}
		}

		private static TimeSpan stoppedAfter = new TimeSpan(0, 0, 0, 1);
		private DateTime stoppedMovingAt;

		private void checkStopped()
		{
			if (CNS.moveState == NavSettings.Moving.NOT_MOVE)
				return;

			bool isStopped;

			//log("checking movementSpeed "+movementSpeed, "checkStopped()", Logger.severity.TRACE);
			if (MM.movementSpeed == null || MM.movementSpeed > 0.1f)
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
		}

		private DateTime stoppedRotatingAt;
		/// <summary>
		/// this is based on ship forward
		/// </summary>
		private Vector3D? facing = null;

		private static float notRotPrecision = 0.01f;

		private bool isNotRotating()
		{
			Vector3D origin = myGrid.GetPosition();
			Vector3D forward = myGrid.GridIntegerToWorld(Vector3I.Forward);
			Vector3D currentFace = forward - origin;

			bool currentlyRotating;
			if (facing == null)
				currentlyRotating = true;
			else
			{
				Vector3D prevFace = (Vector3D)facing;
				if (Math.Abs(currentFace.X - prevFace.X) > notRotPrecision || Math.Abs(currentFace.Y - prevFace.Y) > notRotPrecision || Math.Abs(currentFace.Z - prevFace.Z) > notRotPrecision)
				{
					currentlyRotating = true;
					//log("rotating at this instant, dx=" + Math.Abs(currentFace.X - prevFace.X) + ", dy=" + Math.Abs(currentFace.Y - prevFace.Y) + ", dz=" + Math.Abs(currentFace.Z - prevFace.Z) + ", S.A.=" + stoppedRotatingAt, "isNotRotating()", Logger.severity.TRACE);
				}
				else
				{
					currentlyRotating = false;
					//log("not rotating at this instant, dx=" + Math.Abs(currentFace.X - prevFace.X) + ", dy=" + Math.Abs(currentFace.Y - prevFace.Y) + ", dz=" + Math.Abs(currentFace.Z - prevFace.Z) + ", S.A.=" + stoppedRotatingAt, "isNotRotating()", Logger.severity.TRACE);
				}
			}
			facing = currentFace;

			if (currentlyRotating)
			{
				//log("rotating");
				stoppedRotatingAt = DateTime.UtcNow + stoppedAfter;
				return false;
			}
			else
				return DateTime.UtcNow > stoppedRotatingAt;
		}

		/// <summary>
		/// for other kinds of stop use moveOrder(Vector3.Zero) or similar
		/// </summary>
		internal void fullStop(string reason)
		{
			if (currentMove == Vector3.Zero && currentRotate == Vector2.Zero && currentRoll == 0 && dampenersOn()) // already stopped
				return;

			log("full stop: " + reason, "fullStop()", Logger.severity.INFO);
			reportState(ReportableState.STOPPING);
			currentMove = Vector3.Zero;
			currentRotate = Vector2.Zero;
			currentRoll = 0;

			setDampeners();
			currentRCcontrol.MoveAndRotateStopped();

			CNS.moveState = NavSettings.Moving.STOP_MOVE;
			CNS.rotateState = NavSettings.Rotating.STOP_ROTA;
			pitchNeedToRotate = 0;
			yawNeedToRotate = 0;
		}

		internal Vector3 currentMove = Vector3.Zero;
		private Vector2 currentRotate = Vector2.Zero;
		private float currentRoll = 0;

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
		}

		internal void moveOrder(RelativeVector3F move, bool normalize = true)
		{
			moveOrder(move.getBlock(currentRCblock), normalize);
		}

		/// <summary>
		/// builds vector from MM.pitch and MM.yaw
		/// </summary>
		private void rotateOrder()
		{
			rotateOrder(new Vector2((float)MM.pitchPower, (float)MM.yawPower));
			//log("entered rotateOrder("+rotate+")");
		}

		private void rotateOrder(Vector2 rotate)
		{
			if (currentRotate == rotate)
				return;
			currentRotate = rotate;
			moveAndRotate();
		}

		private void rollOrder(float roll)
		{
			//log("entered rollOrder("+roll+")");
			if (currentRoll == roll)
				return;
			currentRoll = roll;
			moveAndRotate();
		}

		private void moveAndRotate()
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

		public bool dampenersOn()
		{ return ((currentRCcontrol as Ingame.IMyShipController).DampenersOverride); }

		internal void setDampeners(bool dampenersOn = true)
		{
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

			if (CNS.noWayForward)
			{
				//log("changing report to NO_PATH(noWayForward)", "reportState()", Logger.severity.TRACE);
				newState = ReportableState.NO_PATH;
			}
			if (CNS.EXIT)
				newState = ReportableState.OFF;
			if (CNS.landingState == NavSettings.LANDING.LOCKED)
				newState = ReportableState.LANDED;
			if (GET_OUT_OF_SEAT) // must override LANDED
				newState = ReportableState.GET_OUT_OF_SEAT;
			if (myJump != null && myJump.currentState != Jumper.GridJumper.State.OFF && myJump.currentState != Jumper.GridJumper.State.FAILED)
				newState = ReportableState.JUMP;

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
			// error
			if (myInterpreter.instructionErrorIndex != null)
				newName.Append("ERROR(" + myInterpreter.instructionErrorIndex + ") : ");
			// actual state
			newName.Append(newState);
			// jump time
			if (newState == ReportableState.JUMP && myJump != null && myJump.currentState == Jumper.GridJumper.State.TRANSFER)
			{
				newName.Append(':');
				newName.Append((int)myJump.estimatedTimeToReadyMillis() / 1000);
			}
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

		[System.Diagnostics.Conditional("DEBUG")]
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
