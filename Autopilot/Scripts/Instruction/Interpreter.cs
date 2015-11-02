using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Rynchodon.Attached;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Harvest;
using Rynchodon.Autopilot.Movement;
using Rynchodon.Autopilot.Navigator;
using Rynchodon.Settings;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.Collections;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Autopilot.Instruction
{
	public static class InterpreterExtensions
	{
		public static bool hasInstructions(this Interpreter toCheck)
		{ return toCheck != null && toCheck.instructionQueue != null && toCheck.instructionQueue.Count > 0; }
	}

	/// <summary>
	/// Parses instructions into Actions.
	/// Information on command usage can also be found in Steam Description/Autopilot Navigation.txt
	/// </summary>
	public class Interpreter
	{
		/// <summary>
		/// When queued actions exceeds the limit, this exception will be thrown.
		/// </summary>
		public class InstructionQueueOverflow : Exception { }

		public readonly AllNavigationSettings NavSet;

		private readonly Logger m_logger;
		private readonly ShipControllerBlock Controller;
		public readonly Mover Mover;

		public bool SyntaxError { get; private set; }

		public Interpreter(ShipControllerBlock block)
		{
			this.m_logger = new Logger("Interpreter", block.Controller);
			this.Controller = block;
			this.NavSet = new AllNavigationSettings(block.Controller);
			this.Mover = new Mover(block, NavSet);
		}

		/// <summary>
		/// All the instructions queued.
		/// </summary>
		public readonly MyQueue<Action> instructionQueue = new MyQueue<Action>(8);

		public readonly StringBuilder Errors = new StringBuilder();

		/// <summary>
		/// convert instructions from block to Action, then enqueue to instructionQueue
		/// </summary>
		/// <param name="block">block with instructions</param>
		public void enqueueAllActions()
		{
			SyntaxError = false;
			string instructions = preParse().getInstructions();

			if (instructions == null)
				return;

			Errors.Clear();
			instructionQueue.Clear();

			m_logger.debugLog("block: " + Controller.Terminal.DisplayNameText + ", preParse = " + preParse() + ", instructions = " + instructions, "enqueueAllActions()");
			enqueueAllActions_continue(instructions);
		}

		/// <summary>
		/// <para>performs actions before splitting instructions by : and ;</para>
		/// <para>Currently, performs a substitution for pasted GPS tags</para>
		/// </summary>
		private string preParse()
		{
			string blockName = Controller.Terminal.DisplayNameText;

			if (blockName.GpsToCSV(out blockName) != 0)
				MyAPIGateway.Utilities.TryInvokeOnGameThread(() => Controller.Terminal.SetCustomName(blockName));

			return blockName;
		}

		/// <summary>
		/// Does the heavy lifting for enqueueAllActions
		/// </summary>
		private void enqueueAllActions_continue(string allInstructions)
		{
			string[] splitInstructions = allInstructions.Split(new char[] { ':', ';' });

			if (splitInstructions == null || splitInstructions.Length == 0)
				return;

			for (int i = 0; i < splitInstructions.Length; i++)
			{
				if (!enqueueAction(splitInstructions[i].RemoveWhitespace()))
				{
					m_logger.debugLog("Failed to parse instruction " + splitInstructions[i], "enqueueAllActions()", Logger.severity.WARNING);
					Errors.AppendLine("Syntax:" + splitInstructions[i]);
					SyntaxError = true;
				}
				else
					m_logger.debugLog("Parsed instruction " + splitInstructions[i], "enqueueAllActions()");
			}
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
				m_logger.debugLog("instruction too short: " + instruction.Length, "getAction()", Logger.severity.TRACE);
				return false;
			}

			Action singleAction = null;
			if (getAction_word(instruction, out singleAction))
			{
				instructionQueue.Enqueue(singleAction);
				//instructionQueueString.Add("[" + currentInstruction + "] " + instruction);
				return true;
			}
			if (getAction_multiple(instruction))
			{
				//instructionQueueString.Add("[" + currentInstruction + "] " + instruction);
				return true;
			}
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
			//if (instruction.Contains(","))
			//	return getAction_wordPlus(instruction, out wordAction);

			string lowerCase = instruction.ToLower();
			switch (lowerCase)
			{
				case "asteroid":
					{
						wordAction = () => { NavSet.Settings_Task_NavMove.IgnoreAsteroid = true; };
						return true;
					}
				case "disable":
					{
						wordAction = () => {
							m_logger.debugLog("Disabling", "getAction_word()", Logger.severity.DEBUG);
							Controller.DisableControl();
						};
						return true;
					}
				case "exit":
					{
						wordAction = () => new Stopper(Mover, NavSet, true);
						return true;
					}
				case "form":
					{
						wordAction = () => NavSet.Settings_Task_NavMove.Stay_In_Formation = true;
						return true;
					}
				//case "jump":
				//	{
				//		wordAction = null;
				//		return false;
				//	}
				case "line":
					{
						wordAction = () => {
							m_logger.debugLog("set line", "getAction_word()");
							NavSet.Settings_Task_NavMove.PathfinderCanChangeCourse = false;
							NavSet.Settings_Task_NavMove.NavigatorRotator = new DoNothing();
						};
						return true;
					}
				case "stop":
					{
						wordAction = () => { new Stopper(Mover, NavSet); };
						return true;
					}
				case "unlock":
					{
						wordAction = () => new UnLander(Mover, NavSet);
						return true;
					}
				default:
					{
						wordAction = null;
						return false;
					}
			}
		}

		///// <summary>
		///// Try to match instruction against keywords. Accepts comma separated params.
		///// </summary>
		///// <param name="instruction">unparsed instruction</param>
		///// <returns>true iff successful</returns>
		//private bool getAction_wordPlus(string instruction, out Action wordAction)
		//{
		//	string[] split = instruction.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

		//	if (split.Length > 1)
		//		switch (split[0].ToLower())
		//		{
		//		}

		//	wordAction = null;
		//	return false;
		//}

		/// <summary>
		/// <para>Try to replace an instruction with multiple instructions. Will enqueue actions, not return them.</para>
		/// </summary>
		/// <param name="instruction">unparsed instruction</param>
		/// <returns>true iff successful</returns>
		private bool getAction_multiple(string instruction)
		{
			string lowerCase = instruction.ToLower();

			switch (lowerCase[0])
			{
				case 't':
					return addAction_textPanel(lowerCase.Substring(1));
			}

			return false;
		}

		private bool getAction_single(string instruction, out Action instructionAction)
		{
			string lowerCase = instruction.ToLower();
			string dataLowerCase = lowerCase.Substring(1);
			m_logger.debugLog("instruction = " + instruction + ", lowerCase = " + lowerCase + ", dataLowerCase = " + dataLowerCase + ", lowerCase[0] = " + lowerCase[0], "getAction()", Logger.severity.TRACE);

			switch (lowerCase[0])
			{
				case 'a':
					return getAction_terminalAction(out instructionAction, instruction.Substring(1));
				case 'b':
					return getAction_blockSearch(out instructionAction, instruction.Substring(1));
				case 'c':
					return getAction_coordinates(out instructionAction, dataLowerCase);
				case 'e':
					return getAction_engage(out instructionAction, dataLowerCase);
				case 'f':
					return getAction_flyTo(out instructionAction, dataLowerCase);
				case 'g':
					return getAction_gridDest(out instructionAction, instruction.Substring(1));
				case 'h':
					return getAction_harvestVoxel(out instructionAction, dataLowerCase);
				case 'l':
					return getAction_landingBlock(out instructionAction, dataLowerCase);
				case 'n':
					return getAction_navigationBlock(out instructionAction, dataLowerCase);
				case 'o':
					return getAction_offset(out instructionAction, dataLowerCase);
				case 'p':
					return getAction_Proximity(out instructionAction, dataLowerCase);
				case 'r':
					return getAction_grind(out instructionAction, dataLowerCase);
				case 'u':
					return getAction_unlandBlock(out instructionAction, dataLowerCase);
				case 'v':
					return getAction_speedLimit(out instructionAction, dataLowerCase);
				case 'w':
					return getAction_wait(out instructionAction, dataLowerCase);
				default:
					{
						m_logger.debugLog("could not match: " + lowerCase[0], "getAction()", Logger.severity.TRACE);
						instructionAction = null;
						return false;
					}
			}
		}


		#region MULTI ACTIONS


		/// <summary>
		/// <para>add actions from a text panel</para>
		/// <para>Format for instruction is [ t (Text Panel Name), (Identifier) ]</para>
		/// </summary>
		private bool addAction_textPanel(string dataLowerCase)
		{
			string[] split = dataLowerCase.Split(',');

			string panelName;
			if (split.Length == 2)
				panelName = split[0];
			else
				panelName = dataLowerCase;

			IMyCubeBlock panelAsCubeBlock;
			if (!GetLocalBlock(panelName, out panelAsCubeBlock, AttachedGrid.AttachmentKind.Permanent))
			{
				return false;
			}

			Ingame.IMyTextPanel panel = panelAsCubeBlock as Ingame.IMyTextPanel;
			if (panel == null)
			{
				m_logger.debugLog("not a Text Panel: " + panelAsCubeBlock.DisplayNameText, "addAction_textPanel()", Logger.severity.DEBUG);
				return false;
			}

			string panelText = panel.GetPublicText();
			string lowerText = panelText.ToLower();

			string identifier;
			int identifierIndex, startOfCommands;

			if (split.Length == 2)
			{
				identifier = split[1];
				identifierIndex = lowerText.IndexOf(identifier);
				if (identifierIndex < 0)
				{
					m_logger.debugLog("could not find " + identifier + " in text of " + panel.DisplayNameText, "addAction_textPanel()", Logger.severity.DEBUG);
					return false;
				}
				startOfCommands = panelText.IndexOf('[', identifierIndex + identifier.Length) + 1;
			}
			else
			{
				identifier = null;
				identifierIndex = -1;
				startOfCommands = panelText.IndexOf('[') + 1;
			}

			if (startOfCommands < 0)
			{
				m_logger.debugLog("could not find start of commands following " + identifier + " in text of " + panel.DisplayNameText, "addAction_textPanel()", Logger.severity.DEBUG);
				return false;
			}

			int endOfCommands = panelText.IndexOf(']', startOfCommands + 1);
			if (endOfCommands < 0)
			{
				m_logger.debugLog("could not find end of commands following " + identifier + " in text of " + panel.DisplayNameText, "addAction_textPanel()", Logger.severity.DEBUG);
				return false;
			}

			m_logger.debugLog("fetching commands from panel: " + panel.DisplayNameText, "addAction_textPanel()", Logger.severity.TRACE);
			string commands = panelText.Substring(startOfCommands, endOfCommands - startOfCommands);
			commands.GpsToCSV(out commands);
			enqueueAllActions_continue(commands);

			return true; // this instruction was successfully executed, even if sub instructions were not
		}


		#endregion
		#region SINGLE ACTIONS


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
			blockName = blockName.ToLower().Replace(" ", "");
			actionString = actionString.Trim(); // leave spaces in actionString

			AttachedGrid.RunOnAttachedBlock(Controller.CubeGrid, AttachedGrid.AttachmentKind.Permanent, block => {
				IMyCubeBlock fatblock = block.FatBlock;
				if (fatblock == null || !(fatblock is IMyTerminalBlock))
					return false;

				if (!Controller.Controller.canControlBlock(fatblock))
					return false;

				// name test
				if (ShipController_Autopilot.IsAutopilotBlock(fatblock))
				{
					string nameOnly = fatblock.getNameOnly();
					if (nameOnly == null || !nameOnly.looseContains(blockName))
						return false;
				}
				else
				{
					if (!fatblock.DisplayNameText.looseContains(blockName))
						return false;
				}

				IMyTerminalBlock terminalBlock = fatblock as IMyTerminalBlock;
				ITerminalAction actionToRun = terminalBlock.GetActionWithName(actionString); // get actionToRun on every iteration so invalid blocks can be ignored
				if (actionToRun != null)
				{
					m_logger.debugLog("running action: " + actionString + " on block: " + fatblock.DisplayNameText, "runActionOnBlock()", Logger.severity.DEBUG);
					actionToRun.Apply(fatblock);
				}
				else
					m_logger.debugLog("could not get action: " + actionString + " for: " + fatblock.DisplayNameText, "runActionOnBlock()", Logger.severity.TRACE);

				return false;
			}, true);
		}

		/// <summary>
		/// Register a name for block search.
		/// </summary>
		private bool getAction_blockSearch(out Action instructionAction, string dataLowerCase)
		{
			string name;
			Base6Directions.Direction? forward, upward;

			if (!SplitNameDirections(dataLowerCase, out name, out forward, out upward))
			{
				instructionAction = null;
				return false;
			}

			instructionAction = () => {
				m_logger.debugLog("name: " + name + ", forward: " + forward + ", upward: " + upward, "getAction_blockSearch()");
				NavSet.Settings_Task_NavRot.DestinationBlock = new BlockNameOrientation(name, forward, upward);
			};
			return true;
		}

		private bool getAction_coordinates(out Action instructionAction, string dataLowerCase)
		{
			string[] coordsString = dataLowerCase.Split(',');
			if (coordsString.Length == 3)
			{
				double[] coordsDouble = new double[3];
				for (int i = 0; i < coordsDouble.Length; i++)
					if (!Double.TryParse(coordsString[i], out coordsDouble[i]))
					{
						// failed to parse
						instructionAction = null;
						return false;
					}

				// successfully parsed
				Vector3D destination = new Vector3D(coordsDouble[0], coordsDouble[1], coordsDouble[2]);
				instructionAction = () => { new GOLIS(Mover, NavSet, destination); };
				return true;
			}
			instructionAction = null;
			return false;
		}

		private bool getAction_engage(out Action instructionAction, string dataLowerCase)
		{
			if (!ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bAllowWeaponControl))
			{
				m_logger.debugLog("Cannot engage, weapon control is disabled.", "getAction_engage()", Logger.severity.WARNING);
				Errors.AppendLine("Weapon control is disabled");
				instructionAction = null;
				return false;
			}

			string[] split = dataLowerCase.Split(',');

			if (split[0] == "off")
			{
				instructionAction = () => NavSet.Settings_Commands.EnemyFinder = null;
				return true;
			}

			float distance;
			if (!stringToDistance(out distance, split[0]))
			{
				m_logger.debugLog("failed to get distance from: " + split[0], "getAction_engage()");
				instructionAction = null;
				return false;
			}

			List<EnemyFinder.Response> responses = new List<EnemyFinder.Response>();
			for (int i = 1; i < split.Length; i++)
			{
				string resStr = split[i].Trim().Replace('-', '_');
				m_logger.debugLog("Response: " + resStr, "getAction_engage()");

				EnemyFinder.Response r;
				if (!Enum.TryParse<EnemyFinder.Response>(resStr, true, out r))
				{
					Errors.Append("Not a response: ");
					Errors.AppendLine(resStr);
					instructionAction = null;
					return false;
				}
				else
					responses.Add(r);
			}

			if (responses.Count == 0)
			{
				Errors.Append("No responses: ");
				Errors.AppendLine(dataLowerCase);
				instructionAction = null;
				return false;
			}

			instructionAction = () => {
				if (NavSet.Settings_Commands.EnemyFinder == null)
					NavSet.Settings_Commands.EnemyFinder = new EnemyFinder(Mover, NavSet);
				NavSet.Settings_Commands.EnemyFinder.AddResponses(distance, responses);
			};
			return true;
		}

		private bool getAction_flyTo(out Action execute, string instruction)
		{
			execute = null;
			Vector3 result;
			m_logger.debugLog("checking flyOldStyle", "getAction_flyTo()", Logger.severity.TRACE);
			if (!flyOldStyle(out result, instruction))
			{
				m_logger.debugLog("checking flyTo_generic", "getAction_flyTo()", Logger.severity.TRACE);
				if (!flyTo_generic(out result, instruction))
				{
					m_logger.debugLog("failed both styles", "getAction_flyTo()", Logger.severity.TRACE);
					return false;
				}
			}

			execute = () => { new GOLIS(Mover, NavSet, result); };
			return true;
		}

		/// <summary>
		/// tries to read fly instruction of form (r), (u), (b)
		/// </summary>
		/// <returns>true iff successful</returns>
		private bool flyOldStyle(out Vector3 result, string instruction)
		{
			m_logger.debugLog("entered flyOldStyle(result, " + instruction + ")", "flyTo_generic()", Logger.severity.TRACE);

			result = Vector3.Zero;
			string[] coordsString = instruction.Split(',');
			if (coordsString.Length != 3)
				return false;

			double[] coordsDouble = new double[3];
			for (int i = 0; i < coordsDouble.Length; i++)
				if (!Double.TryParse(coordsString[i], out coordsDouble[i]))
					return false;

			Vector3 fromBlock = new Vector3(coordsDouble[0], coordsDouble[1], coordsDouble[2]);

			result = Vector3.Transform(fromBlock, NavSet.Settings_Current.NavigationBlock.WorldMatrix);
			return true;
		}

		private bool flyTo_generic(out Vector3 result, string instruction)
		{
			m_logger.debugLog("entered flyTo_generic(result, " + instruction + ")", "flyTo_generic()", Logger.severity.TRACE);

			result = Vector3.Zero;
			Vector3 fromGeneric;
			if (getVector_fromGeneric(out fromGeneric, instruction))
			{
				result = Vector3.Transform(fromGeneric, NavSet.Settings_Current.NavigationBlock.WorldMatrix);
				return true;
			}
			return false;
		}

		/// <summary>
		/// <para>Search for a grid.</para>
		/// The search happens when the action is executed. 
		/// </summary>
		private bool getAction_gridDest(out Action execute, string instruction)
		{
			m_logger.debugLog("entered getAction_gridDest(out Action execute, string " + instruction + ")", "getAction_gridDest()");
			execute = () => new FlyToGrid(Mover, NavSet, instruction);

			return true;
		}

		/// <summary>
		/// Create a MinerVoxel
		/// </summary>
		private bool getAction_harvestVoxel(out Action execute, string instruction)
		{
			byte[] oreType;

			if (instruction.Equals("arvest", StringComparison.OrdinalIgnoreCase))
				oreType = null;
			else
			{
				string[] splitComma = instruction.Split(',');
				List<byte> oreTypeList = new List<byte>();

				for (int i = 0; i < splitComma.Length; i++)
				{
					string oreName = splitComma[i];

					byte[] oreIds;
					if (!OreDetector.TryGetMaterial(splitComma[i], out oreIds))
					{
						m_logger.debugLog("Syntax Error. Not ore: " + oreName, "getAction_harvestVoxel()", Logger.severity.WARNING);
						Errors.Append("Not ore: ");
						Errors.AppendLine(oreName);
						execute = null;
						return false;
					}

					oreTypeList.AddArray(oreIds);
				}

				oreType = oreTypeList.ToArray();
			}

			execute = () => new MinerVoxel(Mover, NavSet, oreType);
			return true;
		}

		private bool getAction_landingBlock(out Action instructionAction, string instruction)
		{
			IMyCubeBlock landingBlock;
			Base6Directions.Direction? forward, upward;
			if (!GetLocalBlock(instruction, out landingBlock, out forward, out upward))
			{
				instructionAction = null;
				return false;
			}

			instructionAction = () => {
				PseudoBlock asPB = new PseudoBlock(landingBlock, forward, upward);

				m_logger.debugLog("setting LandingBlock to " + landingBlock.DisplayNameText, "GetLocalBlock()");
				NavSet.Settings_Task_NavRot.LandingBlock = asPB;
				NavSet.LastLandingBlock = asPB;
			};

			return true;
		}

		private bool getAction_navigationBlock(out Action instructionAction, string instruction)
		{
			IMyCubeBlock navigationBlock;
			Base6Directions.Direction? forward, upward;
			if (!GetLocalBlock(instruction, out navigationBlock, out forward, out upward))
			{
				instructionAction = null;
				return false;
			}

			instructionAction = () => {
				PseudoBlock asPB = new PseudoBlock(navigationBlock, forward, upward);

				if (navigationBlock is IMyLaserAntenna || navigationBlock is Ingame.IMySolarPanel || navigationBlock is Ingame.IMyOxygenFarm)
					new Facer(Mover, NavSet, asPB);
				m_logger.debugLog("setting NavigationBlock to " + navigationBlock.DisplayNameText, "GetLocalBlock()");
				NavSet.Settings_Task_NavRot.NavigationBlock = asPB;
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
			execute = () => {
				//myLogger.debugLog("setting offset vector to " + offsetVector, "getAction_offset()");
				NavSet.Settings_Task_NavMove.DestinationOffset = offsetVector;
			};
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
						m_logger.debugLog("failed to parse: " + coordsString[i], "offset_oldStyle()", Logger.severity.TRACE);
						return false;
					}
				result = new Vector3(coordsFloat[0], coordsFloat[1], coordsFloat[2]);
				return true;
				//owner.CNS.destination_offset = new Vector3I((int)coordsDouble[0], (int)coordsDouble[1], (int)coordsDouble[2]);
				//myLogger.debugLog("setting offset to " + owner.CNS.destination_offset, "addInstruction()", Logger.severity.DEBUG);
			}
			m_logger.debugLog("wrong length: " + coordsString.Length, "offset_oldStyle()", Logger.severity.TRACE);
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
				execute = () => {
					NavSet.Settings_Commands.DestinationRadius = distance;
					//myLogger.debugLog("proximity action executed " + instruction + " to " + distance + ", radius = " + owner.CNS.destinationRadius, "getActionProximity()", Logger.severity.TRACE);
				};
				//myLogger.debugLog("proximity action created successfully " + instruction + " to " + distance + ", radius = " + owner.CNS.destinationRadius, "getActionProximity()", Logger.severity.TRACE);
				return true;
			}
			//myLogger.debugLog("failed to parse " + instruction + " to float, radius = " + owner.CNS.destinationRadius, "getActionProximity()", Logger.severity.TRACE);
			execute = null;
			return false;
		}

		private bool getAction_grind(out Action instructionAction, string instruction)
		{
			float distance;
			if (stringToDistance(out distance, instruction))
			{
				instructionAction = () => { new Grinder(Mover, NavSet, distance); };
				return true;
			}
			instructionAction = null;
			return false;
		}

		private bool getAction_unlandBlock(out Action instructionAction, string instruction)
		{
			IMyCubeBlock unlandBlock;
			Base6Directions.Direction? forward, upward;
			if (!GetLocalBlock(instruction, out unlandBlock, out forward, out upward))
			{
				instructionAction = null;
				return false;
			}

			instructionAction = () => {
				PseudoBlock asPB = new PseudoBlock(unlandBlock, forward, upward);
				m_logger.debugLog("unlanding " + unlandBlock.DisplayNameText, "GetLocalBlock()");
				new UnLander(Mover, NavSet, asPB);
			};

			return true;
		}

		private bool getAction_speedLimit(out Action instructionAction, string dataLowerCase)
		{
			float parsed;
			if (!Single.TryParse(dataLowerCase, out parsed))
			{
				instructionAction = null;
				return false;
			}
			instructionAction = () => { NavSet.Settings_Commands.SpeedTarget = parsed; };
			return true;
		}

		private bool getAction_wait(out Action instructionAction, string dataLowerCase)
		{
			int seconds;
			if (!GetSeconds(dataLowerCase, out seconds))
			{
				instructionAction = null;
				return false;
			}

			instructionAction = () => {
				new Stopper(Mover, NavSet);
				NavSet.Settings_Task_NavWay.WaitUntil = DateTime.UtcNow.AddSeconds(seconds);
			};
			return true;
		}


		#endregion
		#region COMMON METHODS


		/// <summary>
		/// splits by ',', then adds each according to its imbedded direction
		/// </summary>
		/// <returns>true iff successful</returns>
		private bool getVector_fromGeneric(out Vector3 result, string instruction)
		{
			string[] parts = instruction.Split(',');
			if (parts.Length == 0)
			{
				m_logger.debugLog("parts.Length == 0", "flyTo_generic()", Logger.severity.DEBUG);
				result = new Vector3();
				return false;
			}

			result = Vector3.Zero;
			foreach (string part in parts)
			{
				Vector3 partVector;
				if (!stringToVector3(out partVector, part))
				{
					//myLogger.debugLog("stringToVector3 failed", "flyTo_generic()", Logger.severity.TRACE);
					result = new Vector3();
					return false;
				}
				result += partVector;
			}
			return true;
		}

		private static readonly Regex numberRegex = new Regex(@"\A-?\d+\.?\d*");

		/// <param name="vectorString">takes the form "(number)(direction)"</param>
		private bool stringToVector3(out Vector3 result, string vectorString)
		{
			//myLogger.debugLog("entered stringToVector3(result, " + vectorString + ")", "stringToVector3()", Logger.severity.TRACE);
			result = new Vector3();

			// numbers
			string numberString = numberRegex.Match(vectorString).Value;
			if (string.IsNullOrEmpty(numberString))
			{
				m_logger.debugLog("invalid(" + numberString + ")", "stringToVector3()", Logger.severity.TRACE);
				return false;
			}
			float number;
			if (!float.TryParse(numberString, out number))
			{
				m_logger.debugLog("failed to parse number: " + numberString, "stringToVector3()", Logger.severity.TRACE);
				return false;
			}

			// lettters
			string letterString = vectorString.Replace(numberString, string.Empty);
			if (string.IsNullOrEmpty(letterString))
			{
				m_logger.debugLog("invalid(" + letterString + ")", "stringToVector3()", Logger.severity.TRACE);
				return false;
			}
			int modifier = metreModifier(ref letterString);
			Base6Directions.Direction? direction = stringToDirection(letterString);
			if (direction == null)
			{
				m_logger.debugLog("failed to parse letter: " + letterString, "stringToVector3()", Logger.severity.TRACE);
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
				m_logger.debugLog("invalid(" + numberString + ")", "stringToVector3()", Logger.severity.TRACE);
				return false;
			}
			if (!float.TryParse(numberString, out result))
			{
				m_logger.debugLog("failed to parse number: " + numberString, "stringToVector3()", Logger.severity.TRACE);
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

		private bool GetSeconds(string command, out int seconds)
		{
			Regex expr = new Regex(@"(\d*)(\w?)");
			Match m = expr.Match(command);

			if (!int.TryParse(m.Groups[1].Value, out seconds))
			{
				m_logger.debugLog("failed to parse: " + m.Groups[1].Value + " to int", "GetSeconds()", Logger.severity.DEBUG);
				Errors.Append("Not an integer: ");
				Errors.AppendLine(m.Groups[1].Value);
				return false;
			}
			m_logger.debugLog("parsed " + m.Groups[1].Value + " to " + seconds, "GetSeconds()");

			if (m.Groups.Count == 1 || string.IsNullOrWhiteSpace(m.Groups[2].Value))
				return true;

			int modifier;

			switch (m.Groups[2].Value)
			{
				case "h":
					modifier = 3600;
					break;
				case "m":
					modifier = 60;
					break;
				default:
					seconds = 0;
					m_logger.debugLog("not recognized: " + m.Groups[2].Value, "GetSeconds()", Logger.severity.DEBUG);
					Errors.Append("Not time mod: ");
					Errors.AppendLine(m.Groups[2].Value);
					return false;
			}

			seconds *= modifier;
			return true;
		}

		private Base6Directions.Direction? stringToDirection(string str)
		{
			if (str.Length < 1)
				return null;
			switch (char.ToLower(str[0]))
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

		private bool SplitNameDirections(string toSplit, out string blockName, out Base6Directions.Direction? forward, out Base6Directions.Direction? upward)
		{
			string[] splitComma = toSplit.Split(',');

			blockName = splitComma[0].RemoveWhitespace();
			forward = null;
			upward = null;

			switch (splitComma.Length)
			{
				case 1:
					return true;
				case 2:
					forward = stringToDirection(splitComma[1]);
					if (!forward.HasValue)
					{
						m_logger.debugLog("Syntax Error. Not a direction: " + splitComma[1], "SplitNameDirections()", Logger.severity.WARNING);
						Errors.Append("Not a direction: ");
						Errors.AppendLine(splitComma[1]);
						return false;
					}
					return true;
				case 3:
					upward = stringToDirection(splitComma[2]);
					if (!upward.HasValue)
					{
						m_logger.debugLog("Syntax Error. Not a direction: " + splitComma[2], "SplitNameDirections()", Logger.severity.WARNING);
						Errors.Append("Not a direction: ");
						Errors.AppendLine(splitComma[2]);
						return false;
					}
					goto case 2;
				default:
					m_logger.debugLog("Syntax Error. Too many commas: " + toSplit, "SplitNameDirections()", Logger.severity.WARNING);
					Errors.Append("Too many commas: " + toSplit);
					Errors.AppendLine(toSplit);
					return false;
			}
		}

		/// <summary>
		/// Finds a block that is attached to Controller
		/// </summary>
		/// <remarks>
		/// Search is performed immediately.
		/// </remarks>
		private bool GetLocalBlock(string searchFor, out IMyCubeBlock localBlock, AttachedGrid.AttachmentKind allowedAttachments = AttachedGrid.AttachmentKind.None)
		{
			IMyCubeBlock foundBlock = null;
			int bestNameLength = int.MaxValue;
			m_logger.debugLog("searching for localBlock: " + searchFor, "GetLocalBlock()");

			AttachedGrid.RunOnAttachedBlock(Controller.CubeGrid, allowedAttachments, slim => {
				IMyCubeBlock Fatblock = slim.FatBlock;
				if (Fatblock != null && Controller.CubeBlock.canControlBlock(Fatblock))
				{
					string blockName = ShipController_Autopilot.IsAutopilotBlock(Fatblock)
						? Fatblock.getNameOnly().LowerRemoveWhitespace()
						: Fatblock.DisplayNameText.LowerRemoveWhitespace();

					//myLogger.debugLog("checking: " + blockName, "GetLocalBlock()");

					if (blockName.Length < bestNameLength && blockName.Contains(searchFor))
					{
						foundBlock = Fatblock;
						bestNameLength = blockName.Length;
						if (searchFor.Length == bestNameLength)
						{
							m_logger.debugLog("found a perfect match for: " + searchFor + ", block name: " + blockName, "GetLocalBlock()");
							return true;
						}
					}
				}
				return false;
			}, true);

			if (foundBlock == null)
			{
				m_logger.debugLog("could not get a localBlock for " + searchFor, "GetLocalBlock()", Logger.severity.INFO);
				Errors.AppendLine("Not Found: " + searchFor);
				localBlock = null;
				return false;
			}

			localBlock = foundBlock;
			m_logger.debugLog("found localBlock: " + foundBlock.DisplayNameText, "GetLocalBlock()");

			return true;
		}

		/// <summary>
		/// Finds a block that is part of Controller's grid
		/// </summary>
		/// <remarks>
		/// Search is performed immediately.
		/// Do not enabled attached grids, it would be a nightmare for navigation.
		/// </remarks>
		private bool GetLocalBlock(string instruction, out IMyCubeBlock localBlock, out Base6Directions.Direction? forward, out Base6Directions.Direction? upward)
		{
			string searchFor;

			if (!SplitNameDirections(instruction, out searchFor, out forward, out upward))
			{
				localBlock = null;
				return false;
			}

			return GetLocalBlock(searchFor, out localBlock, AttachedGrid.AttachmentKind.None);
		}

		#endregion
	}
}
