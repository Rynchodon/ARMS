#define LOG_ENABLED //remove on build

using System;
//using System.Collections.Generic;
//using System.Linq;
using System.Text.RegularExpressions;

using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;
using VRageMath;
using Rynchodon.AntennaRelay;

namespace Rynchodon.Autopilot.Instruction
{
	public class Interpreter
	{
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

		// TODO addInstruction should be moved here, as instruction parsing is re-written
		public bool getAction(out Action asAction, string instruction)
		{
			//log("entered getAction(asAction, " + instruction + ")", "getAction()", Logger.severity.TRACE);
			asAction = null;
			if (instruction.Length < 2)
			{
				log("instruction too short: " + instruction.Length, "getAction()", Logger.severity.TRACE);
				return false;
			}

			string lowerCase = instruction.ToLower().Replace(" ", "");

			if (lowerCase == "reset")
				return getAction_dispose(out asAction);

			string data = lowerCase.Substring(1);
			//log("instruction = " + instruction + ", lowerCase = " + lowerCase + ", data = " + data + ", lowerCase[0] = " + lowerCase[0], "getAction()", Logger.severity.TRACE);
			switch (lowerCase[0])
			{
				case 'f':
					return getAction_flyTo(out asAction, owner.currentRCblock, data);
				case 'g':
					return getAction_gridDest(out asAction, data);
				//case 'h':
					// harvest
				case 'l':
					return getAction_localBlock(out asAction, data);
				case 'o':
					return getAction_offset(out asAction, data);
				case 'p':
					return getAction_Proximity(out asAction, data);
				//case 't':
					// text panel
			}
			log("could not match: " + lowerCase[0], "getAction()", Logger.severity.TRACE);
			return false;
		}


		// INDIVIDUAL METHODS


		private bool getAction_dispose(out Action execute)
		{
			IMyTerminalBlock RCterminal = owner.currentRCterminal;
			//Regex reset = new Regex("reset", RegexOptions.IgnoreCase);
			execute = () =>
			{
				//log("running reset action", "getAction_dispose()", Logger.severity.TRACE);
				//RCterminal.SetCustomName(reset.Replace(RCterminal.DisplayNameText, ""));
				if (!(owner.currentRCblock as Ingame.IMyRemoteControl).ControlThrusters)
					RCterminal.GetActionWithName("ControlThrusters").Apply(RCterminal);
				Core.remove(owner);
			};
			//log("finished building reset action", "getAction_dispose()", Logger.severity.TRACE);
			return true;
		}

		private bool getAction_flyTo(out Action execute, IMyCubeBlock remote, string instruction)
		{
			execute = null;
			RelativeVector3F result;
			//log("checking flyOldStyle", "getAction_flyTo()", Logger.severity.TRACE);
			if (!flyOldStyle(out result, remote, instruction))
			{
				//log("checking flyTo_generic", "getAction_flyTo()", Logger.severity.TRACE);
				if (!flyTo_generic(out result, remote, instruction))
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
					log("proximity action executed " + instruction + " to " + distance + ", radius = " + owner.CNS.destinationRadius, "getActionProximity()", Logger.severity.TRACE);
				};
				log("proximity action created successfully " + instruction + " to " + distance + ", radius = " + owner.CNS.destinationRadius, "getActionProximity()", Logger.severity.TRACE);
				return true;
			}
			log("failed to parse " + instruction + " to float, radius = " + owner.CNS.destinationRadius, "getActionProximity()", Logger.severity.TRACE);
			execute = null;
			return false;
		}

		/// <summary>
		/// Get instructions from TextPanel. T (panel name)[, (start)][, (end)]
		/// </summary>
		/// <param name="execute"></param>
		/// <param name="instruction"></param>
		/// <returns></returns>
		private bool getAction_TextPanel(out Action execute, string instruction)
		{
			execute = null;
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
