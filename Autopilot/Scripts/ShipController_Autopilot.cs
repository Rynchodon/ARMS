using System;
using System.Collections.Generic;
using Rynchodon.Autopilot.Instruction;
using Rynchodon.Autopilot.NavigationSettings;
using Rynchodon.Autopilot.Navigator;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;

namespace Rynchodon.Autopilot
{

	public interface ShipController_Block : IMyCubeBlock, IMyTerminalBlock, IMyControllableEntity { }

	public class ShipController_Autopilot
	{

		private static readonly HashSet<IMyCubeGrid> GridBeingControlled = new HashSet<IMyCubeGrid>();
		private static readonly FastResourceLock lock_GridBeingControlled = new FastResourceLock();

		private readonly Logger myLogger;
		private readonly Interpreter myInterpreter;

		private IMyCubeGrid ControlledGrid;

		public ShipController_Block myBlock { get; private set; }
		public AllNavigationSettings.SettingsLevel CurrentSettings { get { return myNavSet.CurrentSettings; } }

		private AllNavigationSettings myNavSet { get { return myInterpreter.myNavSet; } }
		private ANavigator myNavigator { get { return myInterpreter.currentNavigator; } }

		public ShipController_Autopilot(IMyCubeBlock block)
		{
			myBlock = block as ShipController_Block;
			myLogger = new Logger("ShipController_Autopilot", block);
		}

		public void Update()
		{
			if (myNavigator != null && myNavigator.CurrentState == ANavigator.NavigatorState.Finished)
				myInterpreter.currentNavigator = null;

			if (!CheckControl())
				return;

			if (myInterpreter.hasInstructions())
			{
				while (myInterpreter.instructionQueue.Count != 0 && myNavigator == null)
					myInterpreter.instructionQueue.Dequeue().Invoke();

				if (myNavigator == null)
				{
					myLogger.debugLog("interpreter did not yield a navigator", "Update()", Logger.severity.INFO);
					return;
				}

				myNavigator.PerformTask();
			}
			else
			{
				myInterpreter.enqueueAllActions(myBlock);

				if (myInterpreter.hasInstructions())
				{
					myNavSet.OnStartOfCommands();
				}
				else
					ReleaseControlledGrid();
			}
		}


		#region Control

		private bool CheckControl()
		{
			// cache current grid in case it changes
			IMyCubeGrid myGrid = myBlock.CubeGrid;

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
				|| MyAPIGateway.Players.GetPlayerControllingEntity(grid) != null)
				return false;

			// is block ready
			if (!myBlock.IsWorking
				|| !grid.BigOwners.Contains(myBlock.OwnerId)
				|| !myBlock.EnabledThrusts)
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

			myLogger.debugLog("Released control of " + ControlledGrid.DisplayName, "ReleaseControlledGrid()", Logger.severity.DEBUG);
			ControlledGrid = null;
		}

		#endregion


	}
}
