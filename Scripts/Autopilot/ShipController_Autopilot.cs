﻿using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Instruction;
using Rynchodon.Autopilot.Navigator;
using Rynchodon.Settings;
using Rynchodon.Threading;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;

namespace Rynchodon.Autopilot
{

	public class ShipControllerBlock
	{

		public readonly MyShipController Controller;
		public readonly IMyCubeBlock CubeBlock;
		public readonly IMyTerminalBlock Terminal;
		public readonly PseudoBlock Pseudo;
		public readonly NetworkClient NetClient;

		private readonly Logger m_logger;

		public IMyCubeGrid CubeGrid { get { return Controller.CubeGrid; } }
		public MyPhysicsComponentBase Physics { get { return Controller.CubeGrid.Physics; } }

		public ShipControllerBlock(IMyCubeBlock block, NetworkClient netClient)
		{
			m_logger = new Logger(GetType().Name, block);
			Controller = block as MyShipController;
			CubeBlock = block;
			Terminal = block as IMyTerminalBlock;
			Pseudo = new PseudoBlock(block);
			NetClient = netClient;
		}

		public void ApplyAction(string action)
		{
			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => Terminal.GetActionWithName(action).Apply(Terminal), m_logger);
		}

		public void SetControl(bool enable)
		{
			if (Controller.ControlThrusters != enable)
			{
				m_logger.debugLog("setting control, ControlThrusters: " + Controller.ControlThrusters + ", enable: " + enable, "SetDamping()");
				MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
					if (!enable)
						Controller.MoveAndRotateStopped();
					if (Controller.ControlThrusters != enable)
						// SwitchThrusts() only works for jetpacks
						CubeBlock.ApplyAction("ControlThrusters");
				}, m_logger);
			}
		}

		public void SetDamping(bool enable)
		{
			Sandbox.Game.Entities.IMyControllableEntity control = Controller as Sandbox.Game.Entities.IMyControllableEntity;
			if (control.EnabledDamping != enable)
			{
				m_logger.debugLog("setting damp, EnabledDamping: " + control.EnabledDamping + ", enable: " + enable, "SetDamping()");
				MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
					if (control.EnabledDamping != enable)
						control.SwitchDamping();
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
		public const ushort ModId_CustomInfo = 54311;

		private const string subtype_autopilotBlock = "Autopilot-Block";

		private static readonly TimeSpan MinTimeInstructions = new TimeSpan(0, 0, 10);
		private static ThreadManager AutopilotThread = new ThreadManager(threadName: "Autopilot");
		private static HashSet<IMyCubeGrid> GridBeingControlled = new HashSet<IMyCubeGrid>();

		static ShipController_Autopilot()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			AutopilotThread = null;
			GridBeingControlled = null;
		}

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

		private enum State : byte { Disabled, Enabled, Halted, Closed }

		public readonly ShipControllerBlock m_block;

		private readonly Logger m_logger;
		private Interpreter m_interpreter;

		private readonly FastResourceLock lock_execution = new FastResourceLock();

		private IMyCubeGrid m_controlledGrid;
		private State value_state = State.Disabled;
		private TimeSpan m_nextAllowedInstructions = TimeSpan.MinValue;
		private TimeSpan m_endOfHalt;

		private StringBuilder m_customInfo_build = new StringBuilder(), m_customInfo_send = new StringBuilder();
		private List<byte> m_customInfo_message = new List<byte>();
		private ulong m_nextCustomInfo;
		private Message m_message;

		private AllNavigationSettings m_navSet { get { return m_interpreter.NavSet; } }

		public StringBuilder CustomInfo { get { return m_customInfo_send; } }

		public bool Enabled { get { return value_state == State.Enabled; } }

		private State m_state
		{
			get { return value_state; }
			set
			{
				if (value_state == value)
					return;
				m_logger.debugLog("state change from " + value_state + " to " + value, "set_m_state()", Logger.severity.DEBUG);
				value_state = value;

				switch (value_state)
				{
					case State.Enabled:
						return;

					case State.Disabled:
						m_navSet.OnStartOfCommands(); // here so that fighter gets thrown out and weapons disabled
						m_interpreter.instructionQueue.Clear();
						m_nextAllowedInstructions = Globals.ElapsedTime;
						return;

					case State.Halted:
						m_endOfHalt = Globals.ElapsedTime.Add(new TimeSpan(0, 5, 0));
						m_block.SetDamping(true);
						m_block.Controller.MoveAndRotateStopped();
						return;

					case State.Closed:
						if (GridBeingControlled != null)
							ReleaseControlledGrid();
						m_interpreter = null;
						return;
				}
			}
		}

		/// <summary>
		/// Creates an Autopilot for the given ship controller.
		/// </summary>
		/// <param name="block">The ship controller to use</param>
		public ShipController_Autopilot(IMyCubeBlock block)
		{
			this.m_block = new ShipControllerBlock(block, new NetworkClient(block, HandleMessage));
			this.m_logger = new Logger("ShipController_Autopilot", block);
			this.m_interpreter = new Interpreter(m_block);

			this.m_block.CubeBlock.OnClosing += CubeBlock_OnClosing;

			// toggle thrusters off and on to make sure thrusters are actually online
			if (this.m_block.Controller.ControlThrusters)
			{
				this.m_block.CubeBlock.ApplyAction("ControlThrusters");
				this.m_block.CubeBlock.ApplyAction("ControlThrusters");
			}

			// for my German friends...
			if (!m_block.Terminal.DisplayNameText.Contains("[") && !m_block.Terminal.DisplayNameText.Contains("]"))
				m_block.Terminal.SetCustomName(m_block.Terminal.DisplayNameText + " []");

			m_logger.debugLog("Created autopilot for: " + block.DisplayNameText, "ShipController_Autopilot()");

			Registrar.Add(block, this);
		}

		private void CubeBlock_OnClosing(VRage.ModAPI.IMyEntity obj)
		{
			m_block.CubeBlock.OnClosing -= CubeBlock_OnClosing;
			m_state = State.Closed;
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
				if (Globals.UpdateCount > m_nextCustomInfo)
				{
					m_nextCustomInfo = Globals.UpdateCount + 100ul;
					UpdateCustomInfo();
				}

				switch (m_state)
				{
					case State.Disabled:
						if (CheckControl())
							m_state = State.Enabled;
						return;
					case State.Enabled:
						if (CheckControl())
							break;
						m_state = State.Disabled;
						return;
					case State.Halted:
						if (!m_block.Controller.ControlThrusters || Globals.ElapsedTime > m_endOfHalt)
							m_state = State.Disabled;
						return;
					case State.Closed:
						return;
				}

				if (MyAPIGateway.Players.GetPlayerControllingEntity(m_controlledGrid) != null)
					// wait for player to give back control, do not reset
					return;

				if (m_message != null)
				{
					m_interpreter.enqueueAllActions(m_message.Content);
					m_message = null;
					m_navSet.OnStartOfCommands();
				}

				EnemyFinder ef = m_navSet.Settings_Current.EnemyFinder;
				if (ef != null)
					ef.Update();

				if (m_navSet.Settings_Current.WaitUntil > Globals.ElapsedTime)
					return;

				if (m_interpreter.SyntaxError)
					m_interpreter.Mover.MoveAndRotateStop();
				else if (MoveAndRotate())
					return;

				if (m_interpreter.hasInstructions())
				{
					m_logger.debugLog("running instructions", "Update()");

					while (m_interpreter.instructionQueue.Count != 0 && m_navSet.Settings_Current.NavigatorMover == null)
					{
						m_interpreter.instructionQueue.Dequeue().Invoke();
						if (m_navSet.Settings_Current.WaitUntil > Globals.ElapsedTime)
						{
							m_logger.debugLog("now waiting until " + m_navSet.Settings_Current.WaitUntil, "Update()");
							return;
						}
					}

					if (m_navSet.Settings_Current.NavigatorMover == null)
					{
						m_logger.debugLog("interpreter did not yield a navigator", "Update()", Logger.severity.INFO);
						ReleaseControlledGrid();
					}
					return;
				}

				if (!m_interpreter.SyntaxError)
					if (Rotate())
						return;

				if (m_nextAllowedInstructions > Globals.ElapsedTime)
				{
					m_logger.debugLog("Delaying instructions", "UpdateThread()", Logger.severity.INFO);
					m_navSet.Settings_Task_NavWay.WaitUntil = m_nextAllowedInstructions;
					return;
				}

				m_logger.debugLog("enqueing instructions", "Update()", Logger.severity.DEBUG);
				m_nextAllowedInstructions = Globals.ElapsedTime + MinTimeInstructions;
				m_interpreter.enqueueAllActions();

				if (!m_interpreter.hasInstructions())
					ReleaseControlledGrid();
				m_navSet.OnStartOfCommands();
			}
			catch (Exception ex)
			{
				m_logger.alwaysLog("Exception: " + ex, "Update()", Logger.severity.ERROR);
				m_state = State.Halted;
			}
			finally
			{ lock_execution.ReleaseExclusive(); }
		}

		private bool MoveAndRotate()
		{
			INavigatorMover navM = m_navSet.Settings_Current.NavigatorMover;
			if (navM != null)
			{
				navM.Move();

				INavigatorRotator navR = m_navSet.Settings_Current.NavigatorRotator; // fetched here because mover might remove it
				if (navR != null)
					navR.Rotate();
				else
				{
					navR = navM as INavigatorRotator;
					if (navR != null)
						navR.Rotate();
				}

				m_interpreter.Mover.MoveAndRotate();
				return true;
			}
			return false;
		}

		private bool Rotate()
		{
			INavigatorRotator navR = m_navSet.Settings_Current.NavigatorRotator;
			if (navR != null)
			{
				//run the rotator by itself until direction is matched

				navR.Rotate();

				m_interpreter.Mover.MoveAndRotate();

				if (!m_navSet.DirectionMatched())
					return true;
				m_interpreter.Mover.StopRotate();
			}
			return false;
		}

		#region Control

		/// <summary>
		/// Checks if the Autopilot has permission to run.
		/// </summary>
		/// <returns>True iff the Autopilot has permission to run.</returns>
		private bool CheckControl()
		{
			// cache current grid in case it changes
			IMyCubeGrid myGrid = m_block.CubeGrid;

			if (m_controlledGrid != null)
			{
				if (m_controlledGrid != myGrid)
				{
					// a (de)merge happened
					ReleaseControlledGrid();
				}
				else if (CanControlBlockGrid(m_controlledGrid))
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

			if (!CanControlBlockGrid(myGrid) || !GridBeingControlled.Add(myGrid))
				return false;

			m_controlledGrid = myGrid;
			return true;
		}

		/// <summary>
		/// Checks if block and grid can be controlled.
		/// </summary>
		/// <returns>True iff block and grid can be controlled.</returns>
		private bool CanControlBlockGrid(IMyCubeGrid grid)
		{
			// is grid ready
			if (grid.IsStatic)
				return false;

			// is block ready
			if (!m_block.Controller.IsWorking
				|| !m_block.Controller.ControlThrusters)
				return false;

			MyCubeGrid mcg = grid as MyCubeGrid;
			if (mcg.HasMainCockpit() && !m_block.Controller.IsMainCockpit)
				return false;

			return true;
		}

		/// <summary>
		/// Release the grid so another Autopilot can control it.
		/// </summary>
		private void ReleaseControlledGrid()
		{
			if (m_controlledGrid == null)
				return;

			if (!GridBeingControlled.Remove(m_controlledGrid))
			{
				m_logger.alwaysLog("Failed to remove " + m_controlledGrid.DisplayName + " from GridBeingControlled", "ReleaseControlledGrid()", Logger.severity.FATAL);
				throw new InvalidOperationException("Failed to remove " + m_controlledGrid.DisplayName + " from GridBeingControlled");
			}

			//myLogger.debugLog("Released control of " + ControlledGrid.DisplayName, "ReleaseControlledGrid()", Logger.severity.DEBUG);
			m_controlledGrid = null;
		}

		#endregion

		#region Custom Info

		private void UpdateCustomInfo()
		{
			if (m_state == State.Halted)
				m_customInfo_build.AppendLine("Autopilot crashed, see log for details");
			else
				BuildCustomInfo();

			if (!m_customInfo_build.EqualsIgnoreCapacity( m_customInfo_send))
			{
				StringBuilder temp = m_customInfo_send;
				m_customInfo_send = m_customInfo_build;
				m_customInfo_build = temp;
				SendCustomInfo();
			}
			//else
			//	m_logger.debugLog("no change in custom info", "UpdateCustomInfo()");

			m_customInfo_build.Clear();
		}

		private void BuildCustomInfo()
		{
			if (m_interpreter.Errors.Length != 0)
			{
				m_customInfo_build.AppendLine("Errors:");
				m_customInfo_build.Append(m_interpreter.Errors);
				m_customInfo_build.AppendLine();
			}

			if (m_controlledGrid == null)
			{
				if (!m_block.Controller.ControlThrusters)
					m_customInfo_build.AppendLine("Disabled");
				else if (m_block.CubeGrid.IsStatic)
					m_customInfo_build.AppendLine("Grid is a station");
				//else if (m_block.CubeGrid.BigOwners.Count == 0)
				//	m_customInfo_build.AppendLine("Grid is unowned");
				else if (!m_block.Controller.IsWorking)
					m_customInfo_build.AppendLine("Not working");
				//else if (!m_block.CubeGrid.BigOwners.Contains(m_block.Controller.OwnerId))
				//	m_customInfo_build.AppendLine("Block cannot control grid");
				else
				{
					MyCubeGrid mcg = m_block.CubeGrid as MyCubeGrid;
					if (mcg.HasMainCockpit() && !m_block.Controller.IsMainCockpit)
						m_customInfo_build.AppendLine("Not main cockpit");
					else
						m_customInfo_build.AppendLine("Another autpilot controlling ship");
				}
				return;
			}

			bool moving = true;

			TimeSpan waitUntil = m_navSet.Settings_Current.WaitUntil;
			if (waitUntil > Globals.ElapsedTime)
			{
				moving = false;
				m_customInfo_build.Append("Waiting for ");
				m_customInfo_build.AppendLine(PrettySI.makePretty(waitUntil - Globals.ElapsedTime));
			}

			IMyPlayer controlling = MyAPIGateway.Players.GetPlayerControllingEntity(m_controlledGrid);
			if (controlling != null)
			{
				moving = false;
				m_customInfo_build.Append("Player controlling: ");
				m_customInfo_build.AppendLine(controlling.DisplayName);
			}

			if (!moving)
				return;

			Pathfinder.Pathfinder path = m_interpreter.Mover.myPathfinder;
			if (path != null && m_navSet.Settings_Current.CollisionAvoidance)
			{
				if (!path.CanMove || !path.CanRotate)
				{
					m_customInfo_build.AppendLine("Pathfinder:");
					if (!path.CanMove)
					{
						if (path.MoveObstruction != null)
						{
							m_customInfo_build.Append("Movement blocked by ");
							m_customInfo_build.AppendLine(path.MoveObstruction.GetNameForDisplay(m_block.CubeBlock.OwnerId));
						}
						else
							m_customInfo_build.AppendLine("Cannot move");
					}
					else if (!path.CanRotate)
					{
						if (path.RotateObstruction != null)
						{
							m_customInfo_build.Append("Rotation blocked by ");
							m_customInfo_build.AppendLine(path.RotateObstruction.GetNameForDisplay(m_block.CubeBlock.OwnerId));
						}
						else
							m_customInfo_build.AppendLine("Cannot rotate safely");
					}
					m_customInfo_build.AppendLine();
				}
			}

			INavigatorMover navM = m_navSet.Settings_Current.NavigatorMover;
			if (navM != null)
			{
				navM.AppendCustomInfo(m_customInfo_build);
				if (!float.IsNaN(m_navSet.Settings_Current.Distance))
				{
					m_customInfo_build.Append("Distance: ");
					m_customInfo_build.AppendLine(m_navSet.PrettyDistance());
				}
			}

			INavigatorRotator navR = m_navSet.Settings_Current.NavigatorRotator;
			if (navR != null && navR != navM)
				navR.AppendCustomInfo(m_customInfo_build);

			EnemyFinder ef = m_navSet.Settings_Current.EnemyFinder;
			if (ef != null && ef.Grid == null)
			{
				m_customInfo_build.Append("Enemy finder: ");
				switch (ef.m_reason)
				{
					case GridFinder.ReasonCannotTarget.None:
						m_customInfo_build.AppendLine("No enemy detected");
						break;
					case GridFinder.ReasonCannotTarget.Too_Far:
						m_customInfo_build.Append(ef.m_bestGrid.HostileName());
						m_customInfo_build.AppendLine(" is too far");
						break;
					case GridFinder.ReasonCannotTarget.Too_Fast:
						m_customInfo_build.Append(ef.m_bestGrid.HostileName());
						m_customInfo_build.AppendLine(" is too fast");
						break;
					case GridFinder.ReasonCannotTarget.Grid_Condition:
						m_customInfo_build.Append(ef.m_bestGrid.HostileName());
						m_customInfo_build.AppendLine(" cannot be targeted");
						break;
				}
			}

			string complaint = m_navSet.Settings_Current.Complaint;
			if (complaint != null)
				m_customInfo_build.AppendLine(complaint);
		}

		private void SendCustomInfo()
		{
			ByteConverter.AppendBytes(m_customInfo_message, m_block.CubeBlock.EntityId);
			ByteConverter.AppendBytes(m_customInfo_message, m_customInfo_send.ToString());

			m_logger.debugLog("sending message, length: " + m_customInfo_message.Count, "SendCustomInfo()");
			m_logger.debugLog("Message:\n" + m_customInfo_send, "SendCustomInfo()");
			byte[] asByteArray = m_customInfo_message.ToArray();
			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				MyAPIGateway.Multiplayer.SendMessageToOthers(ModId_CustomInfo, asByteArray);
				Autopilot_CustomInfo.MessageHandler(asByteArray);
			}, m_logger);

			m_customInfo_message.Clear();
		}

		#endregion Custom Info

		private void HandleMessage(Message msg)
		{
			m_message = msg;
			m_block.SetControl(true);
		}

	}
}
