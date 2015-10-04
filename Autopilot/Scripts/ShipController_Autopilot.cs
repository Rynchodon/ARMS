using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Instruction;
using Rynchodon.Autopilot.Navigator;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Components;

namespace Rynchodon.Autopilot
{

	public class ShipControllerBlock
	{
		public readonly MyShipController Controller;
		public readonly IMyCubeBlock CubeBlock;
		public readonly IMyTerminalBlock Terminal;

		public IMyCubeGrid CubeGrid { get { return Controller.CubeGrid; } }
		public MyPhysicsComponentBase Physics { get { return CubeGrid.Physics; } }

		public ShipControllerBlock(IMyCubeBlock block)
		{
			Controller = block as MyShipController;
			CubeBlock = block;
			Terminal = block as IMyTerminalBlock;
		}
	}

	/// <summary>
	/// Core class for all Autopilot functionality.
	/// </summary>
	public class ShipController_Autopilot
	{

		private const string subtype_autopilotBlock = "Autopilot-Block";

		private static readonly HashSet<IMyCubeGrid> GridBeingControlled = new HashSet<IMyCubeGrid>();
		private static readonly FastResourceLock lock_GridBeingControlled = new FastResourceLock();

		/// <summary>
		/// Determines if the given block is an autopilot block. Does not check ServerSetting.
		/// </summary>
		public static bool IsControllableBlock(IMyCubeBlock block)
		{
			if (block is MyCockpit)
				return block.BlockDefinition.SubtypeId.Contains(subtype_autopilotBlock);

			return block is MyRemoteControl;
		}

		private readonly ShipControllerBlock Block;

		private readonly Logger myLogger;
		private readonly Interpreter myInterpreter;

		private IMyCubeGrid ControlledGrid;
		private bool Enabled;

		private AllNavigationSettings myNavSet { get { return myInterpreter.NavSet; } }

		/// <summary>
		/// Creates an Autopilot for the given ship controller.
		/// </summary>
		/// <param name="block">The ship controller to use</param>
		public ShipController_Autopilot(IMyCubeBlock block)
		{
			this.Block = new ShipControllerBlock(block);
			this.myLogger = new Logger("ShipController_Autopilot", block);
			this.myInterpreter = new Interpreter(Block);

			this.Block.Terminal.AppendingCustomInfo += Terminal_AppendingCustomInfo;

			myLogger.debugLog("Created autopilot for: " + block.DisplayNameText, "ShipController_Autopilot()");
		}

		/// <summary>
		/// Run the autopilot
		/// </summary>
		public void Update()
		{
			if (!CheckControl())
			{
				if (Enabled)
				{
					myLogger.debugLog("lost control", "Update()", Logger.severity.INFO);
					Enabled = false;
					this.Block.Terminal.RefreshCustomInfo();
				}
				return;
			}
			if (!Enabled)
			{
				myLogger.debugLog("gained control", "Update()", Logger.severity.INFO);
				Enabled = true;
				myNavSet.OnStartOfCommands();
			}

			this.Block.Terminal.RefreshCustomInfo();

			if (myNavSet.CurrentSettings.WaitUntil > DateTime.UtcNow)
				return;

			if (MyAPIGateway.Players.GetPlayerControllingEntity(ControlledGrid) != null)
				// wait for player to give back control, do not reset
				return;

			INavigatorMover navM = myNavSet.CurrentSettings.NavigatorMover;
			if (navM != null)
			{
				navM.Move();
				INavigatorRotator navR = myNavSet.CurrentSettings.NavigatorRotator; // fetched here because stopper might remove it
				if (navR != null)
					navR.Rotate();

				myInterpreter.Mover.MoveAndRotate();
				return;
			}

			if (myInterpreter.hasInstructions())
			{
				myLogger.debugLog("running instructions", "Update()");

				while (myInterpreter.instructionQueue.Count != 0 && myNavSet.CurrentSettings.NavigatorMover == null)
					myInterpreter.instructionQueue.Dequeue().Invoke();

				if (myNavSet.CurrentSettings.NavigatorMover == null)
				{
					myLogger.debugLog("interpreter did not yield a navigator", "Update()", Logger.severity.INFO);
					ReleaseControlledGrid();
				}
				return;
			}

			{
				INavigatorRotator navR = myNavSet.CurrentSettings.NavigatorRotator;
				if (navR != null)
				{
					//run the rotator by itself until direction is matched

					navR.Rotate();
					myInterpreter.Mover.MoveAndRotate();

					if (!navR.DirectionMatched)
						return;
					myInterpreter.Mover.FullStop();
				}
			}

			myLogger.debugLog("enqueing instructions", "Update()");
			myInterpreter.enqueueAllActions();

			if (myInterpreter.hasInstructions())
				myNavSet.OnStartOfCommands();
			else
				ReleaseControlledGrid();
		}

		#region Control

		/// <summary>
		/// Checks if the Autopilot has permission to run.
		/// </summary>
		/// <returns>True iff the Autopilot has permission to run.</returns>
		private bool CheckControl()
		{
			// cache current grid in case it changes
			IMyCubeGrid myGrid = Block.Controller.CubeGrid;

			if (ControlledGrid != null)
			{
				if (ControlledGrid != myGrid)
				{
					// a (de)merge happened
					ReleaseControlledGrid();
				}
				else if (CanControlBlockGrid(ControlledGrid))
				{
					// OK to continue controlling
					return true;
				}
				else
				{
					// cannot continue to control
					ReleaseControlledGrid();
					return false;
				}
			}

			if (!CanControlBlockGrid(myGrid))
			{
				// cannot take control
				return false;
			}

			using (lock_GridBeingControlled.AcquireExclusiveUsing())
				if (!GridBeingControlled.Add(myGrid))
				{
					myLogger.debugLog("grid is already being controlled: " + myGrid.DisplayName, "CheckControlOfGrid()", Logger.severity.DEBUG);
					return false;
				}

			ControlledGrid = myGrid;
			return true;
		}

		/// <summary>
		/// Checks if block and grid can be controlled.
		/// </summary>
		/// <returns>True iff block and grid can be controlled.</returns>
		private bool CanControlBlockGrid(IMyCubeGrid grid)
		{
			// is grid ready
			if (grid.IsStatic
				|| grid.BigOwners.Count == 0)
				return false;

			// is block ready
			if (!Block.Controller.IsWorking
				|| !grid.BigOwners.Contains(Block.Controller.OwnerId)
				|| !Block.Controller.ControlThrusters)
				return false;

			return true;
		}

		/// <summary>
		/// Release the grid so another Autopilot can control it.
		/// </summary>
		private void ReleaseControlledGrid()
		{
			if (ControlledGrid == null)
				return;

			using (lock_GridBeingControlled.AcquireExclusiveUsing())
				if (!GridBeingControlled.Remove(ControlledGrid))
				{
					myLogger.alwaysLog("Failed to remove " + ControlledGrid.DisplayName + " from GridBeingControlled", "ReleaseControlledGrid()", Logger.severity.FATAL);
					throw new InvalidOperationException("Failed to remove " + ControlledGrid.DisplayName + " from GridBeingControlled");
				}

			//myLogger.debugLog("Released control of " + ControlledGrid.DisplayName, "ReleaseControlledGrid()", Logger.severity.DEBUG);
			ControlledGrid = null;
		}

		#endregion

		/// <summary>
		/// Appends Autopilot's status to customInfo
		/// </summary>
		/// <param name="arg1">The autopilot block</param>
		/// <param name="customInfo">The autopilot block's custom info</param>
		private void Terminal_AppendingCustomInfo(IMyTerminalBlock arg1, StringBuilder customInfo)
		{
			if (myInterpreter.Errors.Length != 0)
			{
				customInfo.AppendLine("Errors:");
				customInfo.Append(myInterpreter.Errors);
				customInfo.AppendLine();
			}

			if (ControlledGrid == null)
			{
				customInfo.AppendLine("Disabled");
				return;
			}

			bool moving = true;

			double wait = (myNavSet.CurrentSettings.WaitUntil - DateTime.UtcNow).TotalSeconds;
			if (wait > 0)
			{
				moving = false;
				customInfo.Append("Waiting for ");
				customInfo.Append((int)wait);
				customInfo.AppendLine("s");
			}

			IMyPlayer controlling = MyAPIGateway.Players.GetPlayerControllingEntity(ControlledGrid);
			if (controlling != null)
			{
				moving = false;
				customInfo.Append("Player controlling: ");
				customInfo.AppendLine(controlling.DisplayName);
			}

			if (!moving)
				return;

			INavigatorMover navM = myNavSet.CurrentSettings.NavigatorMover;
			if (navM != null)
				navM.AppendCustomInfo(customInfo);

			INavigatorRotator navR = myNavSet.CurrentSettings.NavigatorRotator;
			if (navR != null && navR != navM)
				navR.AppendCustomInfo(customInfo);
		}

	}
}
