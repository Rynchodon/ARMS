using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Instruction;
using Rynchodon.Autopilot.Navigator;
using Rynchodon.Settings;
using Rynchodon.Threading;
using Sandbox.Common.ObjectBuilders;
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

		private readonly Logger m_logger;

		public IMyCubeGrid CubeGrid { get { return Controller.CubeGrid; } }
		public MyPhysicsComponentBase Physics { get { return Controller.CubeGrid.Physics; } }

		public ShipControllerBlock(IMyCubeBlock block)
		{
			m_logger = new Logger(GetType().Name, block);
			Controller = block as MyShipController;
			CubeBlock = block;
			Terminal = block as IMyTerminalBlock;
		}

		public void ApplyAction(string action)
		{
			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => Terminal.GetActionWithName(action).Apply(Terminal), m_logger);
		}

		public void SetControl(bool enable)
		{
			if (Controller.ControlThrusters != enable)
				ApplyAction("ControlThrusters");
		}

		public void SetDamping(bool enable)
		{
			IMyControllableEntity controllable = Controller as IMyControllableEntity;
			if (controllable.EnabledDamping != enable)
			{
				m_logger.debugLog("setting switch damp, EnabledDamping: " + controllable.EnabledDamping + ", enable: " + enable, "SetDamping()");
				MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
					if (controllable.EnabledDamping != enable)
						Controller.SwitchDamping();
				}, m_logger);
			}
		}

	}

	/// <summary>
	/// Core class for all Autopilot functionality.
	/// </summary>
	public class ShipController_Autopilot
	{
		public const uint UpdateFrequency = 3u;

		private const string subtype_autopilotBlock = "Autopilot-Block";

		private static readonly ThreadManager AutopilotThread = new ThreadManager(threadName: "Autopilot");
		private static readonly HashSet<IMyCubeGrid> GridBeingControlled = new HashSet<IMyCubeGrid>();

		/// <summary>
		/// Determines if the given block is an autopilot block. Does not check ServerSettings.
		/// </summary>
		/// <param name="block">The block to check</param>
		/// <returns>True iff the given block is an autopilot block.</returns>
		public static bool IsAutopilotBlock(IMyCubeBlock block)
		{
			if (block is MyCockpit)
				return block.BlockDefinition.SubtypeId.Contains(subtype_autopilotBlock);

			return block is MyRemoteControl;
		}

		/// <summary>
		/// Determines if the given grid has an autopilot block. Does check ServerSettings.
		/// </summary>
		/// <param name="grid">The grid to search</param>
		/// <returns>True iff the given grid contains one or more autopilot blocks.</returns>
		public static bool HasAutopilotBlock(IMyCubeGrid grid)
		{
			if (!ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bAllowAutopilot))
				return false;

			var cache = CubeGridCache.GetFor(grid);
			var cockpits = cache.GetBlocksOfType(typeof(MyObjectBuilder_Cockpit));
			if (cockpits != null)
				foreach (IMyCubeBlock cockpit in cockpits)
					if (IsAutopilotBlock(cockpit))
						return true;

			if (ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bUseRemoteControl))
			{
				var remotes = cache.GetBlocksOfType(typeof(MyObjectBuilder_RemoteControl));
				if (remotes != null)
					foreach (IMyCubeBlock remote in remotes)
						if (IsAutopilotBlock(remote))
							return true;
			}

			return false;
		}

		private enum State : byte { Disabled, Enabled, Halted }

		private readonly ShipControllerBlock Block;

		private readonly Logger myLogger;
		private readonly Interpreter myInterpreter;
		private readonly Pathfinder.Pathfinder myPathfinder;

		private readonly FastResourceLock lock_execution = new FastResourceLock();

		private IMyCubeGrid ControlledGrid;
		private State m_state = State.Disabled;

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
			this.myPathfinder = new Pathfinder.Pathfinder(block.CubeGrid, myNavSet);

			this.Block.Terminal.AppendingCustomInfo += Terminal_AppendingCustomInfo;

			myLogger.debugLog("Created autopilot for: " + block.DisplayNameText, "ShipController_Autopilot()");
		}

		public void Update()
		{
			if (lock_execution.TryAcquireExclusive())
				AutopilotThread.EnqueueAction(UpdateThread);
		}

		/// <summary>
		/// Run the autopilot
		/// </summary>
		private void UpdateThread()
		{
			try
			{
				this.Block.Terminal.RefreshCustomInfo(); // going to be removed anyway

				switch (m_state)
				{
					case State.Disabled:
						if (CheckControl())
						{
							myLogger.debugLog("gained control", "Update()", Logger.severity.INFO);
							myNavSet.OnStartOfCommands();
							myInterpreter.instructionQueue.Clear();
							m_state = State.Enabled;
							break;
						}
						return;
					case State.Enabled:
						if (CheckControl())
							break;
						myLogger.debugLog("lost control", "Update()", Logger.severity.INFO);
						m_state = State.Disabled;
						return;
					case State.Halted:
						if (!Block.Controller.ControlThrusters)
						{
							m_state = State.Disabled;
							Block.Terminal.AppendingCustomInfo += Terminal_AppendingCustomInfo;
							Block.Terminal.AppendingCustomInfo -= HCF_AppendingCustomInfo;
						}
						return;
				}

				if (myNavSet.Settings_Current.WaitUntil > DateTime.UtcNow)
					return;

				if (MyAPIGateway.Players.GetPlayerControllingEntity(ControlledGrid) != null)
					// wait for player to give back control, do not reset
					return;

				myPathfinder.Update();

				INavigatorMover navM = myNavSet.Settings_Current.NavigatorMover;
				if (navM != null)
				{
					navM.Move();

					INavigatorRotator navR = myNavSet.Settings_Current.NavigatorRotator; // fetched here because mover might remove it
					if (navR != null)
						navR.Rotate();

					myInterpreter.Mover.MoveAndRotate();
					return;
				}

				if (myInterpreter.hasInstructions())
				{
					myLogger.debugLog("running instructions", "Update()");

					while (myInterpreter.instructionQueue.Count != 0 && myNavSet.Settings_Current.NavigatorMover == null)
						myInterpreter.instructionQueue.Dequeue().Invoke();

					if (myNavSet.Settings_Current.NavigatorMover == null)
					{
						myLogger.debugLog("interpreter did not yield a navigator", "Update()", Logger.severity.INFO);
						ReleaseControlledGrid();
					}
					return;
				}

				{
					INavigatorRotator navR = myNavSet.Settings_Current.NavigatorRotator;
					if (navR != null)
					{
						//run the rotator by itself until direction is matched

						navR.Rotate();
						myInterpreter.Mover.MoveAndRotate();

						if (!myNavSet.DirectionMatched())
							return;
						myInterpreter.Mover.StopRotate();
					}
				}

				myLogger.debugLog("enqueing instructions", "Update()");
				myInterpreter.enqueueAllActions();

				if (!myInterpreter.hasInstructions())
					ReleaseControlledGrid();
				myNavSet.OnStartOfCommands();
			}
			catch (Exception ex)
			{
				myLogger.alwaysLog("Exception: " + ex, "Update()", Logger.severity.ERROR);
				HCF();
			}
			finally
			{ lock_execution.ReleaseExclusive(); }
		}

		#region Control

		/// <summary>
		/// Checks if the Autopilot has permission to run.
		/// </summary>
		/// <returns>True iff the Autopilot has permission to run.</returns>
		private bool CheckControl()
		{
			// cache current grid in case it changes
			IMyCubeGrid myGrid = Block.CubeGrid;

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

			MyCubeGrid mcg = grid as MyCubeGrid;
			if (mcg.HasMainCockpit() && !Block.Controller.IsMainCockpit)
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
			try
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

				double wait = (myNavSet.Settings_Current.WaitUntil - DateTime.UtcNow).TotalSeconds;
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

				INavigatorMover navM = myNavSet.Settings_Current.NavigatorMover;
				if (navM != null)
					navM.AppendCustomInfo(customInfo);

				INavigatorRotator navR = myNavSet.Settings_Current.NavigatorRotator;
				if (navR != null && navR != navM)
					navR.AppendCustomInfo(customInfo);
			}
			catch (Exception ex)
			{
				myLogger.alwaysLog("Exception: " + ex, "Terminal_AppendingCustomInfo()", Logger.severity.ERROR);
				HCF();
			}
		}

		private void HCF()
		{
			m_state = State.Halted;
			Block.Controller.MoveAndRotateStopped();

			Block.Terminal.AppendingCustomInfo -= Terminal_AppendingCustomInfo;
			Block.Terminal.AppendingCustomInfo += HCF_AppendingCustomInfo;
		}

		private void HCF_AppendingCustomInfo(IMyTerminalBlock arg1, StringBuilder customInfo)
		{ customInfo.AppendLine("Autopilot crashed, see log for details"); }

	}
}
