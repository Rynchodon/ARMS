#define DEBUG //remove on build

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
		[System.Diagnostics.Conditional("DEBUG")]
		private void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{			alwaysLog(level, method, toLog);		}
		private void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.WARNING)
		{			alwaysLog(level, method, toLog);		}
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
		internal NewTargeter myTargeter;

		private IMyControllableEntity currentRemoteControl_Value;
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
				IMyCubeBlock currentRCblock = (currentRemoteControl_Value as IMyCubeBlock);
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
				}

				if (currentRemoteControl_Value != null)
				{
					// actions on new RC
					(currentRemoteControl_Value as Sandbox.ModAPI.IMyTerminalBlock).CustomNameChanged += remoteControl_OnNameChanged;
					reportState(ReportableState.OFF);
					//if (currentRCblock.NeedsUpdate == MyEntityUpdateEnum.NONE)
					//	currentRCblock.NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
				}

				// some variables
				rotationPower = 3f;
				decelerateRotation = 1f / 2f;
				inflightRotatingPower = 3f;
				inflightDecelerateRotation = 1f / 2f;
			}
		}
		public Sandbox.ModAPI.IMyCubeBlock currentRCblock
		{
			get { return currentRemoteControl_Value as Sandbox.ModAPI.IMyCubeBlock; }
			set { currentRCcontrol = value as IMyControllableEntity; }
		}
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
			myTargeter = new NewTargeter(this);
			myInterpreter = new Interpreter(this);
			needToInit = false;
		}

		private void OnClose()
		{
			if (myGrid != null)
			{
				myGrid.OnClose -= OnClose;
				myGrid.OnBlockAdded -= OnBlockAdded;
				myGrid.OnBlockRemoved -= OnBlockRemoved;
			}
			Core.remove(this);
			currentRCcontrol = null;
		}

		private void OnClose(IMyEntity closing)
		{ OnClose(); }

		//private bool needToUpdateBlocks;
		private static MyObjectBuilderType remoteControlType = (new MyObjectBuilder_RemoteControl()).TypeId;

		//private void OnBlockOwnershipChanged(Sandbox.ModAPI.IMyCubeGrid changedBlock)
		//{
		//	//needToUpdateBlocks = true;

		//}

		private void OnBlockAdded(Sandbox.ModAPI.IMySlimBlock addedBlock)
		{
			if (addedBlock.FatBlock != null && addedBlock.FatBlock.BlockDefinition.TypeId == remoteControlType)
				remoteControlBlocks.Add(addedBlock);
				//needToUpdateBlocks = true;
		}

		private void OnBlockRemoved(Sandbox.ModAPI.IMySlimBlock removedBlock)
		{
			if (removedBlock.FatBlock != null)
			{
				if (removedBlock.FatBlock.BlockDefinition.TypeId == remoteControlType)
					remoteControlBlocks.Remove(removedBlock);
					//needToUpdateBlocks = true;
			}
		}

		private long updateCount = 0;
		private bool pass_gridCanNavigate = true;

		/// <summary>
		/// Causes the ship to fly around, following commands.
		/// Calling more often means more precise movements, calling too often (~ every update) will break functionality.
		/// </summary>
		public void update()
		{
			//if (CNS != null)
			//	log("destination radius = " + CNS.destinationRadius, "checkAt_wayDest()", Logger.severity.TRACE);
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
				return;
			}

			if (CNS.getTypeOfWayDest() != NavSettings.TypeOfWayDest.NULL)
				navigate();
			else // no waypoints
			{
				//log("no waypoints or destination");
				if (CNS.instructions.Count > 0)
				{
					while (CNS.instructions.Count > 0)
					{
						addInstruction(CNS.instructions.Dequeue());
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
					// at end of instructions
					//CNS.startOfCommands();
					CNS.waitUntilNoCheck = DateTime.UtcNow.AddSeconds(1);
					return;
				}
				else
				{
					// find a remote control with NavSettings instructions
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
								string instructions = getInstructionsFromRC(fatBlock);
								if (string.IsNullOrWhiteSpace(instructions))
									continue;
								//log("instructions = "+instructions, "update()", Logger.severity.TRACE);
								//log("instructions = " + instructions.Replace(" ", string.Empty), "update()", Logger.severity.TRACE);
								string[] inst = instructions.Replace(" ", string.Empty).Split(':'); // split into CNS.instructions
								if (inst.Length == 0)
									continue;
								//log("found a ready remote control " + fatBlock.DisplayNameText, "update()", Logger.severity.TRACE);
								CNS.instructions = new Queue<string>(inst);
								currentRCcontrol = (fatBlock as IMyControllableEntity);
								CNS.startOfCommands();
								log("remote control: " + getRCNameOnly(fatBlock) + " finished queuing " + CNS.instructions.Count + " of " + inst.Length + " instructions", "update()", Logger.severity.TRACE);
								return;
							}
						}
					}
					// failed to find a ready remote control
				}
			}
		}

		private void runActionOnBlock(string blockName, string actionString)
		{
			//log("entered runActionOnBlock("+blockName+", "+actionString+")", "runActionOnBlock()", Logger.severity.TRACE);
			blockName = blockName.ToLower().Replace(" ", "");
			actionString = actionString.Trim();

			List<IMySlimBlock> blocksWithName = new List<IMySlimBlock>();
			//ITerminalAction actionToRun = null;
			myGrid.GetBlocks(blocksWithName);//, block => block.FatBlock != null && block.FatBlock.DisplayNameText.Contains(blockName));
			foreach (IMySlimBlock block in blocksWithName){
				IMyCubeBlock fatblock = block.FatBlock;
				if (fatblock == null)
					continue;

				Sandbox.Common.MyRelationsBetweenPlayerAndBlock relationship = fatblock.GetUserRelationToOwner(currentRCblock.OwnerId);
				if (relationship != Sandbox.Common.MyRelationsBetweenPlayerAndBlock.Owner && relationship != Sandbox.Common.MyRelationsBetweenPlayerAndBlock.FactionShare)
				{
					//log("failed relationship test for " + fatblock.DisplayNameText + ", result was " + relationship.ToString(), "runActionOnBlock()", Logger.severity.TRACE);
					continue;
				}
				//log("passed relationship test for " + fatblock.DisplayNameText + ", result was " + relationship.ToString(), "runActionOnBlock()", Logger.severity.TRACE);

				//log("testing: " + fatblock.DisplayNameText, "runActionOnBlock()", Logger.severity.TRACE);
				// name test
				if (fatblock is Ingame.IMyRemoteControl)
				{
					string nameOnly = getRCNameOnly(fatblock);
					if (nameOnly == null || !nameOnly.Contains(blockName))
						continue;
				}
				else
				{
					if (!looseContains(fatblock.DisplayNameText, blockName))
					{
						//log("testing failed " + fatblock.DisplayNameText + " does not contain " + blockName, "runActionOnBlock()", Logger.severity.TRACE);
						continue;
					}
					//log("testing successfull " + fatblock.DisplayNameText + " contains " + blockName, "runActionOnBlock()", Logger.severity.TRACE);
				}

				if (!(fatblock is IMyTerminalBlock))
				{
					//log("not a terminal block: " + fatblock.DisplayNameText, "runActionOnBlock()", Logger.severity.TRACE);
					continue;
				}
				IMyTerminalBlock terminalBlock = fatblock as IMyTerminalBlock;
				ITerminalAction	actionToRun = terminalBlock.GetActionWithName(actionString); // get actionToRun on every iteration so invalid blocks can be ignored
				if (actionToRun != null)
				{
					log("running action: " + actionString + " on block: " + fatblock.DisplayNameText, "runActionOnBlock()", Logger.severity.DEBUG);
					actionToRun.Apply(fatblock);
				}
				else
					log("could not get action: " + actionString + " for: " + fatblock.DisplayNameText, "runActionOnBlock()", Logger.severity.TRACE);
			}
		}

		private Interpreter myInterpreter;

		// Do not add to this method, it is replaced by Instuction.
		/// <summary>
		/// adds a single instruction to this handler
		/// </summary>
		/// <param name="instruction">the instruction to add</param>
		private void addInstruction(string instruction)
		{
			log("entered addInstruction(" + instruction + ")", "addInstruction()", Logger.severity.TRACE);

			if (instruction.Length < 2)
				return;

			string lowerCase = instruction.ToLower();
			string data = lowerCase.Substring(1);

			if (looseContains(instruction, "EXIT"))
			{
				CNS.EXIT = true;
				reportState(ReportableState.OFF);
				fullStop("EXIT");
				return;
			}
			if (looseContains(instruction, "JUMP"))
			{
				log("setting jump", "addInstruction()", Logger.severity.DEBUG);
				CNS.jump_to_dest = true;
				return;
			}

			if (looseContains(instruction, "LOCK"))
			{
				if (CNS.landingState == NavSettings.LANDING.LOCKED)
				{
					log("staying locked. local=" + CNS.landingSeparateBlock.DisplayNameText, "addInstruction()", Logger.severity.TRACE);// + ", target=" + CNS.closestBlock + ", grid=" + CNS.gridDestination);
					CNS.landingState = NavSettings.LANDING.OFF;
					CNS.landingSeparateBlock = null;
					CNS.landingSeparateWaypoint = null;
					setDampeners(); // dampeners will have been turned off for docking
				}
				return;
			}

			switch (lowerCase[0])
			{
				case 'a': // action, run an action on (a) block(s)
					{
						data = instruction.Substring(1); // restore case
						string[] split = data.Split(',');
						if (split.Length == 2)
							runActionOnBlock(split[0], split[1]);
						return;
					}
				case 'b': // block: for friendly, search by name. for enemy, search by type
					{
						string[] dataParts = data.Split(',');
						if (dataParts.Length != 2)
						{
							CNS.tempBlockName = data;
							return;
						}
						CNS.tempBlockName = dataParts[0];
						Base6Directions.Direction? dataDir = stringToDirection(dataParts[1]);
						if (dataDir != null)
							CNS.landDirection = dataDir;
							//CNS.landOffset = Base6Directions.GetVector((Base6Directions.Direction)dataDir);
						return;
					}
				case 'c': // coordinates
					{
						string[] coordsString = data.Split(',');
						if (coordsString.Length == 3)
						{
							double[] coordsDouble = new double[3];
							for (int i = 0; i < coordsDouble.Length; i++)
								if (!Double.TryParse(coordsString[i], out coordsDouble[i]))
									return;
							Vector3D destination = new Vector3D(coordsDouble[0], coordsDouble[1], coordsDouble[2]);
							CNS.setDestination(destination);
						}
						return;
					}
				case 'e': // fly to nearest enemy, set max lock-on, block
					goto case 'm';
				//case 'f': // fly a given distance relative to RC
				//	{
						//string[] coordsString = data.Split(',');
						//if (coordsString.Length == 3)
						//{
						//	double[] coordsDouble = new double[3];
						//	for (int i = 0; i < coordsDouble.Length; i++)
						//		if (!Double.TryParse(coordsString[i], out coordsDouble[i]))
						//		{
						//			//log("failed to parse " + coordsString[i] + " to double", "addInstruction()", Logger.severity.TRACE);
						//			return;
						//		}
						//	Vector3D destination = new Vector3D(coordsDouble[0], coordsDouble[1], coordsDouble[2]);
						//	destination = GridWorld.RCtoWorld(currentRCblock, destination);
						//	CNS.setDestination(destination);
						//	//if (CNS.setDestination(destination))
						//	//{
						//		//log("added " + destination + " as a relative destination, type response=" + CNS.getTypeOfWayDest(), "addInstruction()", Logger.severity.TRACE);
						//	//}
						//	//else
						//	//	log("failed to add " + destination + " as a fly to destination", "addInstruction()", Logger.severity.TRACE);
						//}
						////else
						//	//log("wrong number of coords " + coordsString.Length, "addInstruction()", Logger.severity.TRACE);
					//	return;
					//}
				//case 'g': // grid: closest friendly grid that contains the string
				//	{
				//		CNS.searchBlockName = CNS.tempBlockName;
				//		CNS.tempBlockName = null;
				//		Sandbox.ModAPI.IMyCubeBlock closestBlock;
				//		Sandbox.ModAPI.IMyCubeGrid closestGrid = myTargeter.findCubeGrid(out closestBlock, true, data, CNS.searchBlockName);
				//		IMyCubeBlock bestBlockMatch;
				//		LastSeen bestGridMatch;

				//		if (closestGrid != null)
				//		{
				//			//if (CNS.setDestination(closestBlock, closestGrid))
				//			//{
				//			//log("grid destination set", "addInstruction()", Logger.severity.TRACE);
				//			//log("CNS.landLocalBlock = " + CNS.landLocalBlock + ", CNS.landOffset = " + CNS.landOffset, "addInstruction()", Logger.severity.TRACE);
				//			CNS.setDestination(closestBlock, closestGrid);
				//			if (CNS.closestBlock != null && CNS.landLocalBlock != null && CNS.landDirection == null)
				//			{
				//				Base6Directions.Direction? landDir;// = Lander.landingDirection(CNS.closestBlock);
				//				if (!Lander.landingDirection(CNS.closestBlock, out landDir))
				//				{
				//					log("could not get landing direction from block: " + CNS.landLocalBlock.DefinitionDisplayNameText, "calcOrientationFromBlockDirection()", Logger.severity.INFO);
				//					return;
				//				}
				//				CNS.landDirection = landDir;// = Lander.landingDirection(CNS.closestBlock);
				//				//CNS.landOffset = Base6Directions.GetVector((Base6Directions.Direction)Lander.landingDirection(CNS.closestBlock));
				//				log("set land offset to " + CNS.landOffset, "addInstruction()", Logger.severity.TRACE);
				//			}
				//			//}
				//			//else
				//			//{
				//			//	string block;
				//			//	if (closestBlock == null)
				//			//		block = "null";
				//			//	else
				//			//		block = closestBlock.DisplayNameText;
				//			//	log("could not add block/grid destination: " + block + " : " + closestGrid.DisplayName);
				//			//}
				//		}
				//		return;
				//	}
				//case 'l': // for landing or docking, specify direction and local block
				//	{
				//		//string[] dataParts = data.Split(',');
				//		//if (dataParts.Length != 2)
				//		//	return;
				//		//Base6Directions.Direction? dataDir = stringToDirection(dataParts[0]);
				//		//if (dataDir == null)
				//		//{
				//		//	log("could not get a direction for landing", "addInstruction()", Logger.severity.DEBUG);
				//		//	return;
				//		//}
				//		IMyCubeBlock landLocalBlock;
				//		myTargeter.findClosestCubeBlockOnGrid(out landLocalBlock, myGrid, data, true);
				//		CNS.landLocalBlock = landLocalBlock;
				//		if (CNS.landLocalBlock == null)
				//		{
				//			log("could not get a block for landing", "addInstruction()", Logger.severity.DEBUG);
				//			return;
				//		}

				//		//CNS.landOffset = Base6Directions.GetVector((Base6Directions.Direction)dataDir);
				//		//CNS.landRCdirection = myGridDim.direction_getLandRCdirection(landLocalBlock);
				//		return;
				//	}
				case 'm': // same as e, but will crash into target
					{
						double parsed;
						if (Double.TryParse(data, out parsed))
						{
							if (lowerCase[0] == 'e')
								CNS.lockOnTarget = NavSettings.TARGET.ENEMY;
							else
								CNS.lockOnTarget = NavSettings.TARGET.MISSILE;
							CNS.lockOnRangeEnemy = (int)parsed;
							CNS.lockOnBlock = CNS.tempBlockName;
						}
						else
						{
							CNS.lockOnTarget = NavSettings.TARGET.OFF;
							CNS.lockOnRangeEnemy = 0;
							CNS.lockOnBlock = null;
							log("stopped tracking enemies");
						}
						CNS.tempBlockName = null;
						return;
					}
				//case 'o': // destination offset, should be cleared after every waypoint
				//	{
				//		string[] coordsString = data.Split(',');
				//		if (coordsString.Length == 3)
				//		{
				//			double[] coordsDouble = new double[3];
				//			for (int i = 0; i < coordsDouble.Length; i++)
				//				if (!Double.TryParse(coordsString[i], out coordsDouble[i]))
				//					return;
				//			CNS.destination_offset = new Vector3I((int)coordsDouble[0], (int)coordsDouble[1], (int)coordsDouble[2]);
				//			log("setting offset to " + CNS.destination_offset, "addInstruction()", Logger.severity.DEBUG);
				//		}
				//		return;
				//	}
				//case 'p': // how close ship needs to be to destination
				//	{
				//		double parsed;
				//		if (double.TryParse(data, out parsed))
				//			CNS.destinationRadius = (int)parsed;
				//		return;
				//	}
				case 'r': // match orientation
					{
						CNS.match_direction = null;
						CNS.match_roll = null;
						string[] orientation = data.Split(',');
						if (orientation.Length == 0 || orientation.Length > 2)
							break;
						Base6Directions.Direction? dir = stringToDirection(orientation[0]);
						//log("got dir "+dir);
						if (dir == null)
							break;
						CNS.match_direction = (Base6Directions.Direction)dir;

						if (orientation.Length == 1)
							return;
						Base6Directions.Direction? roll = stringToDirection(orientation[1]);
						//log("got roll " + roll);
						if (roll == null)
							return;
						CNS.match_roll = (Base6Directions.Direction)roll;

						return;
					}
				case 'v': // speed limits
					{
						string[] speeds = data.Split(',');
						if (speeds.Length == 2)
						{
							double[] parsedArray = new double[2];
							for (int i = 0; i < parsedArray.Length; i++)
							{
								if (!Double.TryParse(speeds[i], out parsedArray[i]))
									return;
							}
							CNS.speedCruise_external = (int)parsedArray[0];
							CNS.speedSlow_external = (int)parsedArray[1];
						}
						else
						{
							double parsed;
							if (Double.TryParse(data, out parsed))
								CNS.speedCruise_external = (int)parsed;
							return;
						}
						return;
					}
				case 'w': // wait
					double seconds = 0;
					if (Double.TryParse(data, out seconds))
					{
						if (CNS.waitUntil < DateTime.UtcNow)
							CNS.waitUntil = DateTime.UtcNow.AddSeconds(seconds);
						//if (seconds > 1.1)
						//log("setting wait for " + CNS.waitUntil);
					}
					return;
			}
			Action instAsAction;
			log("sending to Interpreter: " + instruction, "addInstruction()", Logger.severity.TRACE);
			if (myInterpreter.getAction(out instAsAction, instruction))
			{
				//log("got action from instruction. instruction = " + instruction+", action = "+instAsAction, "addInstruction", Logger.severity.TRACE);
				log("parsed by Interpreter: " + instruction, "addInstruction()", Logger.severity.TRACE);
				instAsAction.Invoke();
				return;
			}

			log("failed to parse: " + instruction, "addInstruction()", Logger.severity.TRACE);
		}

		private Base6Directions.Direction? stringToDirection(string str)
		{
			switch (str[0])
			{
				case 'f':
					return Base6Directions.Direction.Forward;
				case 'b':
					return Base6Directions.Direction.Backward;
				case 'l':
					return Base6Directions.Direction.Left;
				case 'r':
					return Base6Directions.Direction.Right;
				case 'u':
					return Base6Directions.Direction.Up;
				case 'd':
					return Base6Directions.Direction.Down;
			}
			return null;
		}

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
		/// checks the functional and working flags, current player owns it, display name has not changed
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
			//if (!remoteControl.IsFunctional)
			//{
			//	log("not functional", "remoteControlIsReady()", Logger.severity.TRACE);
			//	return false;
			//}
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
			//if (!Core.canControl(remoteControl))
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
		{
			return remoteControlIsReady(remoteControl as Sandbox.ModAPI.IMyCubeBlock);
		}

		public void reset()
		{
			currentRCcontrol = null;
			//Core.remove(this);
			////log("resetting");
			//NavSettings startNav = CNS;
			//if (currentRCcontrol != null)
			//{
			//	try
			//	{
			//		fullStop("reset");
			//	}
			//	catch (NullReferenceException) { } // when grid is destroyed
			//	//log("clearing current remote control");
			//	currentRCcontrol = null;
			//}
			//if (object.ReferenceEquals(startNav, CNS))
			//{
			//	log("clearing CNS", "reset()", Logger.severity.DEBUG);
			//	CNS = new NavSettings(this);
			//}
			//else
			//	log("did not clear CNS", "reset()", Logger.severity.TRACE);
		}

		//public double movementSpeed { get; private set; }

		private DateTime maxRotateTime;
		internal MovementMeasure MM;

		private void navigate()
		{
			if (currentRCblock == null)
				return;
			if (!remoteControlIsReady(currentRCblock))
			{
				log("remote control is not ready");
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
				log("reached destination dist = "+MM.distToWayDest+", proximity = "+CNS.destinationRadius, "checkAt_wayDest()", Logger.severity.INFO);
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
						if (MM.distToWayDest < CNS.destinationRadius * 3)
						{
							log("close to dest, switch to moving", "calcAndRotate()", Logger.severity.DEBUG);
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
		/// start moving when less than
		/// </summary>
		public const float rotLenSq_startMove = 0.274f;
		//private const float rotLenSq_startHybrid = 9f;
		///// <summary>
		///// inflight rotate when greater than
		///// </summary>
		//public const float rotLenSq_inflight = 0.00762f;
		/// <summary>
		/// should be square root of rotLenSq_inflight
		/// </summary>
		public const float rotLen_minimum = 0.0873f;
		/// <summary>
		/// stop and rotate when greater than
		/// </summary>
		public const float rotLenSq_stopAndRot = 2.47f;

		/// <summary>
		/// stop when greater than
		/// </summary>
		private const float onCourse_sidel = 0.1f, onCourse_hybrid = 0.1f;

		private Vector3 moveDirection = Vector3.Zero;

		private void calcAndMove(bool sidel= false)//, bool anyState=false)
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
								//CNS.moveState = NavSettings.Moving.SIDELING;
							}
							break;
						}
					default:
						{
							alwaysLog("unsuported moveState: "+CNS.moveState, "calcAndMove()", Logger.severity.ERROR);
							return;
						}
				}
			}
			else // not sidel
			{
				moveOrder(Vector3.Forward); // move forward
				log("moving " + MM.distToWayDest + " to " + CNS.getWayDest(), "calcAndMove()", Logger.severity.DEBUG);
				//CNS.moveState = NavSettings.Moving.MOVING;
			}

			//reportState(ReportableState.MOVING);
			stoppedMovingAt = DateTime.UtcNow + stoppedAfter;
		}

		//private double pitchRotatePast;
		//private double yawRotatePast;

		private double pitchNeedToRotate = 0, yawNeedToRotate = 0;

		/// <summary>
		/// 
		/// </summary>
		/// <param name="pitch"></param>
		/// <param name="yaw"></param>
		/// <param name="precision_stopAndRot">for increasing precision of rotLenSq_stopAndRot</param>
		internal void calcAndRotate(float? precision_stopAndRot=null){
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
									//if (MM.rotLenSq < rotLenSq_inflight)
									//	return;
									// make a small, inflight adjustment
									//pitchRotatePast = pitchNeedToRotate / 2;
									//yawRotatePast = yawNeedToRotate / 2;
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
									//pitchRotatePast = 0;
									//yawRotatePast = 0;
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
						//pitch += pitchRotatePast;
						//yaw += yawRotatePast;
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
							log("decelerate roll, roll="+roll+", needToRoll="+needToRoll, "calcAndRoll()", Logger.severity.DEBUG);
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
		//internal float rollPower = 3f;
		//internal float decelerateRoll = 1f / 2f;

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
					//if ((x > 0 && needToRotateX > 0) || (x < 0 && needToRotateX < 0))
					overUnder--;
				else // different sign
					overUnder++;
			if (Math.Abs(MM.yaw) > 0.1 && Math.Abs(yawNeedToRotate) > 0.1)
				if (Math.Sign(MM.yaw) == Math.Sign(yawNeedToRotate)) // same sign
					//if ((y > 0 && needToRotateY > 0) || (y < 0 && needToRotateY < 0))
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

		//private void capFloat(ref float value, float min, float max)
		//{
		//	if (value < min)
		//	{
		//		//alwaysLog(Logger.severity.WARNING, "capFloat", "value too low " + value + " < " + min, CNS.moveState.ToString(), CNS.rotateState.ToString());
		//		value = min;
		//	}
		//	else if (value > max)
		//	{
		//		//alwaysLog(Logger.severity.WARNING, "capFloat", "value too high " + value + " > " + max, CNS.moveState.ToString(), CNS.rotateState.ToString());
		//		value = max;
		//	}
		//}

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
			if (currentMove == Vector3.Zero && currentRotate == Vector2.Zero && currentRoll == 0 && dampenersOn())
				return;

			reportState(ReportableState.STOPPING);
			log("full stop: "+reason, "fullStop()", Logger.severity.INFO);
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
			if (!Vector3.IsValid(move))
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

		public enum ReportableState : byte { OFF, PATHFINDING, ROTATING, MOVING, STOPPING, NO_PATH, NO_DEST, MISSILE, ENGAGING, LANDED, PLAYER, JUMP, BROKEN, HYBRID, SIDEL, ROLL, GET_OUT_OF_SEAT };
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
			if (newState == currentReportable && newState != ReportableState.JUMP)
				return;
			currentReportable = newState;

			// cut old state, if any
			int endOfState = displayName.IndexOf('>');
			if (endOfState != -1)
				displayName = displayName.Substring(endOfState + 1);

			// add new state
			StringBuilder newName = new StringBuilder();
			newName.Append('<');
			newName.Append(newState);
			if (newState == ReportableState.JUMP && myJump != null && myJump.currentState == Jumper.GridJumper.State.TRANSFER)
			{
				newName.Append(':');
				newName.Append((int)myJump.estimatedTimeToReadyMillis() / 1000);
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
		private string instructions;
		private void remoteControl_OnNameChanged(Sandbox.ModAPI.IMyTerminalBlock whichBlock)
		{
			string instructionsInBlock = getInstructionsFromRC(whichBlock as Sandbox.ModAPI.IMyCubeBlock);
			if (instructions == null || !instructions.Equals(instructionsInBlock))
			{
				//log("RC name changed: " + whichBlock.CustomName+"; inst were: "+instructions+"; are now: "+instructionsInBlock, "remoteControl_OnNameChanged()", Logger.severity.DEBUG);
				instructions = instructionsInBlock;
				remoteControlIsNotReady = true;
				reset();
				CNS.waitUntilNoCheck = DateTime.UtcNow.AddSeconds(1);
			}
		}

		private static string getInstructionsFromRC(Sandbox.ModAPI.IMyCubeBlock rc)
		{
			string displayName = rc.DisplayNameText;
			int start = displayName.IndexOf('[') + 1;
			int end = displayName.IndexOf(']');
			if (start > 0 && end > start) // has appropriate brackets
			{
				int length = end - start;
				return displayName.Substring(start, length);
			}
			return null;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="rc"></param>
		/// <returns>null iff name could not be extracted</returns>
		public static string getRCNameOnly(IMyCubeBlock rc)
		{
			string displayName = rc.DisplayNameText;
			int start = displayName.IndexOf('>') + 1;
			int end = displayName.IndexOf('[');
			if (start > 0 && end > start)
			{
				int length = end - start;
				return displayName.Substring(start, length);
			}
			if (start > 0)
			{
				return displayName.Substring(start);
			}
			if (end > 0)
			{
				return displayName.Substring(0, end);
			}
			return null;
		}
	}
}
