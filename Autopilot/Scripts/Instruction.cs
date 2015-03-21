﻿#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
//using System.Linq;
using System.Text.RegularExpressions;

using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;

using VRage.Collections;
using VRageMath;

using Rynchodon.AntennaRelay;

namespace Rynchodon.Autopilot.Instruction
{
	public static class InterpreterExtensions
	{
		public static bool hasInstructions(this Interpreter toCheck)
		{ return toCheck != null && toCheck.instructionQueue != null && toCheck.instructionQueue.Count > 0; }
	}

	public class Interpreter
	{
		public class InstructionQueueOverflow : Exception { }

		private Navigator owner;

		private Logger myLogger;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ alwaysLog(toLog, method, level); }
		private void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null) myLogger = new Logger(owner.myGrid.DisplayName, "Instruction");
			myLogger.log(level, method, toLog);
		}

		public Interpreter(Navigator owner)
		{ this.owner = owner; }

		/// <summary>
		/// System.Collections.Queue is behaving oddly. If MyQueue does not work any better, switch to LinkedList. 
		/// </summary>
		public MyQueue<Action> instructionQueue;

		public bool enqueueAllActions(string allInstructions)
		{
			allInstructions = allInstructions.Replace(" ", string.Empty);
			string[] splitInstructions = allInstructions.Split(new char[] { ':', ';' });

			if (splitInstructions == null || splitInstructions.Length == 0)
				return false;

			instructionQueue = new MyQueue<Action>(8);
			foreach (string instruction in splitInstructions)
				if (!enqueueAction(instruction))
					return false;

			return true;
		}

		/// <summary>
		/// Turn a string instruction into an Action or Actions, and Enqueue it/them to instructionQueue.
		/// </summary>
		/// <param name="instruction">unparsed instruction</param>
		/// <returns>true if an Action was queued, false if parsing failed</returns>
		private bool enqueueAction(string instruction)
		{
			VRage.Exceptions.ThrowIf<InstructionQueueOverflow>(instructionQueue.Count > 1000);

			if (instruction.Length < 2)
			{
				log("instruction too short: " + instruction.Length, "getAction()", Logger.severity.TRACE);
				return false;
			}

			Action singleAction = null;
			if (getAction_word(instruction, out singleAction))
			{
				instructionQueue.Enqueue(singleAction);
				return true;
			}
			if (getAction_multiple(instruction))
				return true;
			if (getAction_single(instruction, out singleAction))
			{
				instructionQueue.Enqueue(singleAction);
				return true;
			}
			return false;
		}

		/// <summary>
		/// Try to match instruction against keywords.
		/// </summary>
		/// <param name="instruction">unparsed instruction</param>
		/// <returns>true iff successful</returns>
		private bool getAction_word(string instruction, out Action wordAction)
		{
			string lowerCase = instruction.ToLower();
			if (lowerCase == "exit")
			{
				wordAction = () =>
				{
					owner.CNS.EXIT = true;
					owner.reportState(Navigator.ReportableState.OFF);
					owner.fullStop("EXIT");
				};
				return true;
			}
			if (lowerCase == "jump")
			{
				wordAction = () =>
				{
					log("setting jump", "addInstruction()", Logger.severity.DEBUG);
					owner.CNS.jump_to_dest = true;
					return;
				};
				return true;
			}
			if (lowerCase == "lock")
			{
				wordAction = () =>
				{
					if (owner.CNS.landingState == NavSettings.LANDING.LOCKED)
					{
						log("staying locked. local=" + owner.CNS.landingSeparateBlock.DisplayNameText, "addInstruction()", Logger.severity.TRACE);// + ", target=" + CNS.closestBlock + ", grid=" + CNS.gridDestination);
						owner.CNS.landingState = NavSettings.LANDING.OFF;
						owner.CNS.landingSeparateBlock = null;
						owner.CNS.landingSeparateWaypoint = null;
						owner.setDampeners(); // dampeners will have been turned off for docking
					}
				};
				return true;
			}
			if (lowerCase == "reset")
			{
				IMyTerminalBlock RCterminal = owner.currentRCterminal;
				wordAction = () =>
				{
					if (!(owner.currentRCblock as Ingame.IMyRemoteControl).ControlThrusters)
						RCterminal.GetActionWithName("ControlThrusters").Apply(RCterminal);
					Core.remove(owner);
				};
			}
			wordAction = null;
			return false;
		}

		/// <summary>
		/// Try to replace an instruction with multiple allInstructions. Will enqueue actions, not return them.
		/// </summary>
		/// <param name="instruction">unparsed instruction</param>
		/// <returns>true iff successful</returns>
		private bool getAction_multiple(string instruction)
		{
			return false;
		}

		private bool getAction_single(string instruction, out Action instructionAction)
		{
			string lowerCase = instruction.ToLower();
			string dataLowerCase = lowerCase.Substring(1);
			//log("instruction = " + instruction + ", lowerCase = " + lowerCase + ", data = " + data + ", lowerCase[0] = " + lowerCase[0], "getAction()", Logger.severity.TRACE);
			switch (lowerCase[0])
			{
				case 'a':
					return getAction_terminalAction(out instructionAction, instruction.Substring(1));
				case 'b':
					return getAction_blockSearch(out instructionAction, dataLowerCase);
				case 'c':
					return getAction_coordinates(out instructionAction, dataLowerCase);
				case 'e':
					return getAction_engage(out instructionAction, dataLowerCase);
				case 'f':
					return getAction_flyTo(out instructionAction, dataLowerCase);
				case 'g':
					return getAction_gridDest(out instructionAction, dataLowerCase);
				//case 'h': // harvest
				case 'l':
					return getAction_localBlock(out instructionAction, dataLowerCase);
				case 'm':
					return getAction_missile(out instructionAction, dataLowerCase);
				case 'o':
					return getAction_offset(out instructionAction, dataLowerCase);
				case 'p':
					return getAction_Proximity(out instructionAction, dataLowerCase);
				case 'r':
					return getAction_orientation(out instructionAction, dataLowerCase);
				case 'v':
					return getAction_speedLimits(out instructionAction, dataLowerCase);
				case 'w':
					return getAction_wait(out instructionAction, dataLowerCase);
			}
			log("could not match: " + lowerCase[0], "getAction()", Logger.severity.TRACE);
			instructionAction = null;
			return false;
		}


		// SINGLE ACTIONS


		/// <summary>
		/// run an action on (a) block(s)
		/// </summary>
		/// <param name="instructionAction"></param>
		/// <param name="dataPreserveCase"></param>
		/// <returns></returns>
		private bool getAction_terminalAction(out Action instructionAction, string dataPreserveCase)
		{
			string[] split = dataPreserveCase.Split(',');
			if (split.Length == 2)
			{
				instructionAction = () => { runActionOnBlock(split[0], split[1]); };
				return true;
			}
			instructionAction = null;
			return false;
		}

		private void runActionOnBlock(string blockName, string actionString)
		{
			//log("entered runActionOnBlock("+blockName+", "+actionString+")", "runActionOnBlock()", Logger.severity.TRACE);
			blockName = blockName.ToLower().Replace(" ", "");
			actionString = actionString.Trim();

			List<IMySlimBlock> blocksWithName = new List<IMySlimBlock>();
			owner.myGrid.GetBlocks(blocksWithName);
			foreach (IMySlimBlock block in blocksWithName)
			{
				IMyCubeBlock fatblock = block.FatBlock;
				if (fatblock == null)
					continue;

				Sandbox.Common.MyRelationsBetweenPlayerAndBlock relationship = fatblock.GetUserRelationToOwner(owner.currentRCblock.OwnerId);
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
					string nameOnly = fatblock.getNameOnly();
					if (nameOnly == null || !nameOnly.Contains(blockName))
						continue;
				}
				else
				{
					if (!fatblock.DisplayNameText.looseContains(blockName))
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
				ITerminalAction actionToRun = terminalBlock.GetActionWithName(actionString); // get actionToRun on every iteration so invalid blocks can be ignored
				if (actionToRun != null)
				{
					log("running action: " + actionString + " on block: " + fatblock.DisplayNameText, "runActionOnBlock()", Logger.severity.DEBUG);
					actionToRun.Apply(fatblock);
				}
				else
					log("could not get action: " + actionString + " for: " + fatblock.DisplayNameText, "runActionOnBlock()", Logger.severity.TRACE);
			}
		}

		/// <summary>
		/// register a name for block search
		/// </summary>
		/// <param name="instructionAction"></param>
		/// <param name="dataLowerCase"></param>
		/// <returns>true</returns>
		private bool getAction_blockSearch(out Action instructionAction, string dataLowerCase)
		{
			instructionAction = () =>
			{
				string[] dataParts = dataLowerCase.Split(',');
				if (dataParts.Length != 2)
				{
					owner.CNS.tempBlockName = dataLowerCase;
					return;
				}
				owner.CNS.tempBlockName = dataParts[0];
				Base6Directions.Direction? dataDir = stringToDirection(dataParts[1]);
				if (dataDir != null)
					owner.CNS.landDirection = dataDir;
			};
			return true;
		}

		/// <summary>
		/// set destination to coordinates
		/// </summary>
		/// <param name="instructionAction"></param>
		/// <param name="data"></param>
		/// <returns></returns>
		private bool getAction_coordinates(out Action instructionAction, string dataLowerCase)
		{
			string[] coordsString = dataLowerCase.Split(',');
			if (coordsString.Length == 3)
			{
				double[] coordsDouble = new double[3];
				for (int i = 0; i < coordsDouble.Length; i++)
					if (!Double.TryParse(coordsString[i], out coordsDouble[i]))
					{
						instructionAction = null;
						return false;
					}

				// successfully parsed
				Vector3D destination = new Vector3D(coordsDouble[0], coordsDouble[1], coordsDouble[2]);
				instructionAction = () => { owner.CNS.setDestination(destination); };
				return true;
			}
			instructionAction = null;
			return false;
		}

		/// <summary>
		/// set engage nearest enemy
		/// </summary>
		/// <param name="instructionAction"></param>
		/// <param name="dataLowerCase"></param>
		/// <returns>true</returns>
		private bool getAction_engage(out Action instructionAction, string dataLowerCase)
		{
			NavSettings CNS = owner.CNS;
			double parsed;
			instructionAction = () =>
			{
				if (Double.TryParse(dataLowerCase, out parsed))
				{
					CNS.lockOnTarget = NavSettings.TARGET.ENEMY;
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
			};
			return true;
		}

		private bool getAction_flyTo(out Action execute, string instruction)
		{
			execute = null;
			RelativeVector3F result;
			//log("checking flyOldStyle", "getAction_flyTo()", Logger.severity.TRACE);
			if (!flyOldStyle(out result, owner.currentRCblock, instruction))
			{
				//log("checking flyTo_generic", "getAction_flyTo()", Logger.severity.TRACE);
				if (!flyTo_generic(out result, owner.currentRCblock, instruction))
				{
					//log("failed both styles", "getAction_flyTo()", Logger.severity.TRACE);
					return false;
				}
			}

			//log("passed, destination will be "+result.getWorldAbsolute(), "getAction_flyTo()", Logger.severity.TRACE);
			execute = () => owner.CNS.setDestination(result.getWorldAbsolute());
			//log("created action: " + execute, "getAction_flyTo()", Logger.severity.TRACE);
			return true;
		}

		/// <summary>
		/// tries to read fly instruction of form (r), (u), (b)
		/// </summary>
		/// <param name="result"></param>
		/// <param name="instruction"></param>
		/// <returns>true iff successful</returns>
		private bool flyOldStyle(out RelativeVector3F result, IMyCubeBlock remote, string instruction)
		{
			result = null;
			string[] coordsString = instruction.Split(',');
			if (coordsString.Length != 3)
				return false;

			double[] coordsDouble = new double[3];
			for (int i = 0; i < coordsDouble.Length; i++)
				if (!Double.TryParse(coordsString[i], out coordsDouble[i]))
					return false;

			Vector3D fromBlock = new Vector3D(coordsDouble[0], coordsDouble[1], coordsDouble[2]);
			result = RelativeVector3F.createFromBlock(fromBlock, remote);
			return true;
		}

		private bool flyTo_generic(out RelativeVector3F result, IMyCubeBlock remote, string instruction)
		{
			//log("entered flyTo_generic(result, " + block.DisplayNameText + ", " + instruction + ")", "flyTo_generic()", Logger.severity.TRACE);

			Vector3 fromGeneric;
			if (getVector_fromGeneric(out fromGeneric, instruction))
			{
				result = RelativeVector3F.createFromBlock(fromGeneric, remote);
				return true;
			}
			result = null;
			return false;
		}

		private bool getAction_gridDest(out Action execute, string instruction)
		{
			NavSettings CNS = owner.CNS;
			string searchName = CNS.tempBlockName;
			CNS.tempBlockName = null;
			IMyCubeBlock blockBestMatch;
			LastSeen gridBestMatch;
			if (owner.myTargeter.lastSeenFriendly(instruction, out gridBestMatch, out blockBestMatch, searchName))
			{
				execute = () => { CNS.setDestination(gridBestMatch, blockBestMatch, owner.currentRCblock); };
				if (blockBestMatch != null && CNS.landLocalBlock != null && CNS.landDirection == null)
				{
					Base6Directions.Direction? landDir;
					if (!Lander.landingDirection(blockBestMatch, out landDir))
					{
						log("could not get landing direction from block: " + CNS.landLocalBlock.DefinitionDisplayNameText, "getAction_gridDest()", Logger.severity.INFO);
						return true; // still fly near dest, maybe issue is a bit more obvious
					}
					execute = () =>
					{
						CNS.setDestination(gridBestMatch, blockBestMatch, owner.currentRCblock);
						CNS.landDirection = landDir;
						log("set land offset to " + CNS.landOffset, "getAction_gridDest()", Logger.severity.TRACE);
					};
				}
				return true;
			}
			log("did not find a friendly grid", "getAction_gridDest()", Logger.severity.TRACE);
			execute = null;
			return false;
		}

		private bool getAction_localBlock(out Action execute, string instruction)
		{
			IMyCubeBlock landLocalBlock;
			if (owner.myTargeter.findBestFriendly(owner.myGrid, out landLocalBlock, instruction))
			{
				execute = () => { owner.CNS.landLocalBlock = landLocalBlock; };
				return true;
			}
			log("could not get a block for landing", "addInstruction()", Logger.severity.DEBUG);
			execute = null;
			return false;
		}

		/// <summary>
		/// set missile
		/// </summary>
		/// <param name="instructionAction"></param>
		/// <param name="dataLowerCase"></param>
		/// <returns>true</returns>
		private bool getAction_missile(out Action instructionAction, string dataLowerCase)
		{
			NavSettings CNS = owner.CNS;
			instructionAction = () =>
			{
				double parsed;
				if (Double.TryParse(dataLowerCase, out parsed))
				{
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
			};
			return true;
		}

		private bool getAction_offset(out Action execute, string instruction)
		{
			Vector3 offsetVector;
			if (!offset_oldStyle(out offsetVector, instruction))
				if (!offset_generic(out offsetVector, instruction))
				{
					execute = null;
					return false;
				}
			execute = () => { owner.CNS.destination_offset = offsetVector; };
			return true;
		}

		private bool offset_oldStyle(out Vector3 result, string instruction)
		{
			result = new Vector3();
			string[] coordsString = instruction.Split(',');
			if (coordsString.Length == 3)
			{
				float[] coordsFloat = new float[3];
				for (int i = 0; i < coordsFloat.Length; i++)
					if (!float.TryParse(coordsString[i], out coordsFloat[i]))
					{
						log("failed to parse: " + coordsString[i], "offset_oldStyle()", Logger.severity.TRACE);
						return false;
					}
				result = new Vector3(coordsFloat[0], coordsFloat[1], coordsFloat[2]);
				return true;
				//owner.CNS.destination_offset = new Vector3I((int)coordsDouble[0], (int)coordsDouble[1], (int)coordsDouble[2]);
				//log("setting offset to " + owner.CNS.destination_offset, "addInstruction()", Logger.severity.DEBUG);
			}
			log("wrong length: " + coordsString.Length, "offset_oldStyle()", Logger.severity.TRACE);
			return false;
		}

		private bool offset_generic(out Vector3 result, string instruction)
		{
			if (getVector_fromGeneric(out result, instruction))
				return true;
			return false;
		}

		private bool getAction_Proximity(out Action execute, string instruction)
		{
			float distance;
			if (stringToDistance(out distance, instruction))
			{
				execute = () =>
				{
					owner.CNS.destinationRadius = (int)distance;
					//log("proximity action executed " + instruction + " to " + distance + ", radius = " + owner.CNS.destinationRadius, "getActionProximity()", Logger.severity.TRACE);
				};
				//log("proximity action created successfully " + instruction + " to " + distance + ", radius = " + owner.CNS.destinationRadius, "getActionProximity()", Logger.severity.TRACE);
				return true;
			}
			//log("failed to parse " + instruction + " to float, radius = " + owner.CNS.destinationRadius, "getActionProximity()", Logger.severity.TRACE);
			execute = null;
			return false;
		}

		/// <summary>
		/// match orientation
		/// </summary>
		/// <param name="instructionAction"></param>
		/// <param name="dataLowerCase"></param>
		/// <returns></returns>
		private bool getAction_orientation(out Action instructionAction, string dataLowerCase)
		{
			string[] orientation = dataLowerCase.Split(',');
			if (orientation.Length == 0 || orientation.Length > 2)
			{
				instructionAction = null;
				return false;
			}
			Base6Directions.Direction? dir = stringToDirection(orientation[0]);
			//log("got dir "+dir);
			if (dir == null) // direction could not be parsed
			{
				instructionAction = null;
				return false;
			}

			if (orientation.Length == 1) // only direction specified
			{
				instructionAction = () => { owner.CNS.match_direction = (Base6Directions.Direction)dir; };
				return true;
			}

			Base6Directions.Direction? roll = stringToDirection(orientation[1]);
			//log("got roll " + roll);
			if (roll == null) // roll specified, could not be parsed
			{
				instructionAction = null;
				return false;
			}
			instructionAction = () =>
			{
				owner.CNS.match_direction = (Base6Directions.Direction)dir;
				owner.CNS.match_roll = (Base6Directions.Direction)roll;
			};
			return true;
		}

		private bool getAction_speedLimits(out Action instructionAction, string dataLowerCase)
		{
			string[] speeds = dataLowerCase.Split(',');
			if (speeds.Length == 2)
			{
				double[] parsedArray = new double[2];
				for (int i = 0; i < parsedArray.Length; i++)
				{
					if (!Double.TryParse(speeds[i], out parsedArray[i]))
					{
						instructionAction = null;
						return false;
					}
				}
				instructionAction = () =>
				{
					owner.CNS.speedCruise_external = (int)parsedArray[0];
					owner.CNS.speedSlow_external = (int)parsedArray[1];
				};
				return true;
			}
			else
			{
				double parsed;
				if (!Double.TryParse(dataLowerCase, out parsed))
				{
					instructionAction = null;
					return false;
				}
				instructionAction = () => { owner.CNS.speedCruise_external = (int)parsed; };
				return true;
			}
		}

		private bool getAction_wait(out Action instructionAction, string dataLowerCase)
		{
			double seconds = 0;
			if (Double.TryParse(dataLowerCase, out seconds))
			{
				instructionAction = () =>
				{
					if (owner.CNS.waitUntil < DateTime.UtcNow)
						owner.CNS.waitUntil = DateTime.UtcNow.AddSeconds(seconds);
				};
				return true;
			}
			instructionAction = null;
			return false;
		}


		// COMMON METHODS


		/// <summary>
		/// splits by ',', then adds each according to its imbedded direction
		/// </summary>
		/// <param name="result"></param>
		/// <param name="instruction"></param>
		/// <returns>true iff successful</returns>
		private bool getVector_fromGeneric(out Vector3 result, string instruction)
		{
			string[] parts = instruction.Split(',');
			if (parts.Length == 0)
			{
				log("parts.Length == 0", "flyTo_generic()", Logger.severity.DEBUG);
				result = new Vector3();
				return false;
			}

			result = Vector3.Zero;
			foreach (string part in parts)
			{
				Vector3 partVector;
				if (!stringToVector3(out partVector, part))
				{
					//log("stringToVector3 failed", "flyTo_generic()", Logger.severity.TRACE);
					result = new Vector3();
					return false;
				}
				result += partVector;
			}
			return true;
		}

		private static readonly Regex numberRegex = new Regex(@"\A-?\d+\.?\d*");
		
		/// <summary>
		/// 
		/// </summary>
		/// <param name="result"></param>
		/// <param name="vectorString">takes the form "(number)(direction)"</param>
		/// <returns></returns>
		private bool stringToVector3(out Vector3 result, string vectorString)
		{
			//log("entered stringToVector3(result, " + vectorString + ")", "stringToVector3()", Logger.severity.TRACE);
			result = new Vector3();

			// numbers
			string numberString = numberRegex.Match(vectorString).Value;
			if (string.IsNullOrEmpty(numberString))
			{
				log("invalid(" + numberString + ")", "stringToVector3()", Logger.severity.TRACE);
				return false;
			}
			float number;
			if (!float.TryParse(numberString, out number))
			{
				log("failed to parse number: " + numberString, "stringToVector3()", Logger.severity.TRACE);
				return false;
			}

			// lettters
			string letterString = vectorString.Replace(numberString, string.Empty);
			if (string.IsNullOrEmpty(letterString))
			{
				log("invalid(" + letterString + ")", "stringToVector3()", Logger.severity.TRACE);
				return false;
			}
			int modifier = metreModifier(ref letterString);
			Base6Directions.Direction? direction = stringToDirection(letterString);
			if (direction == null)
			{
				log("failed to parse letter: " + letterString , "stringToVector3()", Logger.severity.TRACE);
				return false;
			}

			result = (Vector3)Base6Directions.GetVector((Base6Directions.Direction)direction) * number * modifier;
			return true;
		}

		private bool stringToDistance(out float result, string distanceString)
		{
			result = 0f;

			// numbers
			string numberString = numberRegex.Match(distanceString).Value;
			if (string.IsNullOrEmpty(numberString))
			{
				log("invalid(" + numberString + ")", "stringToVector3()", Logger.severity.TRACE);
				return false;
			}
			if (!float.TryParse(numberString, out result))
			{
				log("failed to parse number: " + numberString, "stringToVector3()", Logger.severity.TRACE);
				return false;
			}

			// letters
			string letterString = distanceString.Replace(numberString, string.Empty);
			if (!string.IsNullOrEmpty(letterString))
				result *= metreModifier(ref letterString);

			return true;
		}

		/// <summary>
		/// checks for m or k. If a modifier is found, letters will be modified
		/// </summary>
		/// <param name="characters"></param>
		/// <returns></returns>
		private int metreModifier(ref string letters)
		{
			int modifier = 1;
			if (letters.Length < 1)
				return modifier;
			switch (letters[0])
			{
				case 'm':
					{
						modifier = 1000000;
						if (letters.Length > 1 && letters[1] == 'm')
							letters = letters.Substring(2);
						else
							letters = letters.Substring(1);
						break;
					}
				case 'k':
					{
						modifier = 1000;
						if (letters.Length > 1 && letters[1] == 'm')
							letters = letters.Substring(2);
						else
							letters = letters.Substring(1);
						break;
					}
			}
			return modifier;
		}

		/// <summary>
		/// checks for h or m. If a modifier is found, letters will be modified
		/// </summary>
		/// <param name="characters"></param>
		/// <returns></returns>
		private int secondsModifier(ref string letters)
		{
			int modifier = 1;
			if (letters.Length < 1)
				return modifier;
			switch (letters[0])
			{
				case 'h':
					modifier = 3600;
					letters = letters.Substring(1);
					break;
				case 'm':
					modifier = 60;
					letters = letters.Substring(1);
					break;
			}
			return modifier;
		}

		private Base6Directions.Direction? stringToDirection(string str)
		{
			if (str.Length < 1)
				return null;
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
	}
}
