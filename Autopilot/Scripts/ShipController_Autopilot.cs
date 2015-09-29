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
		public readonly IMyTerminalBlock Terminal;

		public IMyCubeGrid CubeGrid { get { return Controller.CubeGrid; } }
		public MyPhysicsComponentBase Physics { get { return CubeGrid.Physics; } }

		public ShipControllerBlock(IMyCubeBlock block)
		{
			Controller = block as MyShipController;
			Terminal = block as IMyTerminalBlock;
		}
	}

	public class ShipController_Autopilot
	{

		private const string subtype_autopilotBlock = "Autopilot-Block";

		private static readonly HashSet<IMyCubeGrid> GridBeingControlled = new HashSet<IMyCubeGrid>();
		private static readonly FastResourceLock lock_GridBeingControlled = new FastResourceLock();

		/// <summary>
		/// Does not check ServerSetting
		/// </summary>
		public static bool IsControllableBlock(IMyCubeBlock block)
		{
			if (block is MyCockpit)
				return block.BlockDefinition.SubtypeId.Contains(subtype_autopilotBlock);

			return block is MyRemoteControl;
		}

		public readonly ShipControllerBlock Block;

		private readonly Logger myLogger;
		private readonly Interpreter myInterpreter;

		private IMyCubeGrid ControlledGrid;
		private bool Enabled;

		private AllNavigationSettings myNavSet { get { return myInterpreter.NavSet; } }

		public ShipController_Autopilot(IMyCubeBlock block)
		{
			this.Block = new ShipControllerBlock(block);
			this.myLogger = new Logger("ShipController_Autopilot", block);
			this.myInterpreter = new Interpreter(Block);

			this.Block.Terminal.AppendingCustomInfo += Terminal_AppendingCustomInfo;

			myLogger.debugLog("Created autopilot for: " + block.DisplayNameText, "ShipController_Autopilot()");
		}

		public void Update10()
		{
			if (!CheckControl())
			{
				if (Enabled)
				{
					myLogger.debugLog("lost control", "Update10()", Logger.severity.INFO);
					Enabled = false;
					this.Block.Terminal.RefreshCustomInfo();
				}
				return;
			}
			if (!Enabled)
			{
				myLogger.debugLog("gained control", "Update10()", Logger.severity.INFO);
				myInterpreter.Mover.Destination = null;
				myInterpreter.Mover.RotateDest = null;
				Enabled = true;
				myNavSet.OnStartOfCommands();
			}

			this.Block.Terminal.RefreshCustomInfo();

			ANavigator nav = myNavSet.CurrentSettings.Navigator;
			if (nav != null)
			{
				nav.PerformTask();
				return;
			}

			if (myInterpreter.hasInstructions())
			{
				while (myInterpreter.instructionQueue.Count != 0 && myNavSet.CurrentSettings.Navigator == null) // this is a problem!!!
					myInterpreter.instructionQueue.Dequeue().Invoke();

				if (myNavSet.CurrentSettings.Navigator == null)
				{
					myLogger.debugLog("interpreter did not yield a navigator", "Update()", Logger.severity.INFO);
					ReleaseControlledGrid();
					return;
				}
				return;
			}

			myInterpreter.enqueueAllActions();

			if (myInterpreter.hasInstructions())
				myNavSet.OnStartOfCommands();
			else
				ReleaseControlledGrid();
		}

		private void Terminal_AppendingCustomInfo(IMyTerminalBlock arg1, StringBuilder customInfo)
		{
			if (ControlledGrid == null)
			{
				customInfo.AppendLine("Disabled");
				return;
			}

			ANavigator nav = myNavSet.CurrentSettings.Navigator;
			if (nav != null)
				nav.AppendCustomInfo(customInfo);
		}


		#region Control

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
		private bool CanControlBlockGrid(IMyCubeGrid grid)
		{
			// is grid ready
			if (grid.IsStatic
				|| grid.BigOwners.Count == 0
				|| MyAPIGateway.Players.GetPlayerControllingEntity(grid) != null) // getting false positives
				return false;

			// is block ready
			if (! Block.Controller.IsWorking
				|| !grid.BigOwners.Contains(Block.Controller.OwnerId)
				|| !Block.Controller.ControlThrusters)
				return false;

			return true;
		}

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


	}
}
